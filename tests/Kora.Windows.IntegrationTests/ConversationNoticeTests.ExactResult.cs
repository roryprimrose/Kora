using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Documentation;
using Kora.Application.Presentation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Presentation;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class ConversationNoticeTests
{
    [Fact]
    public async Task UnavailableNativeNoticeReaderRecordsFailedOperationWithoutRenewingOrDisclosingTheResult()
    {
        using var f = new Fixture();
        await f.Initialize();
        var request = await f.Submit(ModelTurnOutcome.Succeeded);
        await using var broker = f.Broker();
        var clipboard = new ResultClipboard();
        using var details = new DetailWindowController(new EmbeddedUserDocumentationProvider(), () => f.Access.CanInspect,
            new(NullLogger<NativeDetailRenderer>.Instance), clipboard,
            (_, _, _) => throw new InvalidOperationException("fake unavailable native reader"),
            NullLogger<DetailWindowController>.Instance);
        HostActivity? operation = null;
        var viewer = f.Viewer(broker, (content, current, resolve, token) =>
        {
            operation = HostActivity.RequireCurrent();
            return details.OpenConversationDetailAsync(content, null, current, resolve, token);
        });
        viewer.SetRoutineQuietSurfaceOpen(true);
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single());
        await viewer.RefreshWorkAsync();
        viewer.SelectLocalEvent(viewer.LocalEvents.Single());
        var retention = await f.Storage.Store.ReadRetentionAsync(request.SessionId, f.Token);
        var tasks = f.Storage.Count("host_tasks");
        var audits = f.Storage.Count("security_audit_events");
        await viewer.ReadExactNoticeResultAsync(() => true);
        operation.Should().NotBeNull();
        operation!.Outcome.Should().Be(HostOperationOutcome.Failed);
        viewer.Status.Should().Contain("unavailable");
        viewer.Detail.Should().NotContain("private answer marker");
        clipboard.Writes.Should().Be(0);
        f.Storage.Count("host_tasks").Should().Be(tasks);
        f.Storage.Count("security_audit_events").Should().Be(audits);
        (await f.Storage.Store.ReadRetentionAsync(request.SessionId, f.Token)).Should().Be(retention);
        viewer.Close();
    }

    [WindowsFact]
    public async Task NativeTerminalNoticeReadsItsCompleteAnswerInIntegratedDetailsAndReturnsToExactNotice()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            var service = WindowsSqliteSessionWorkspaceTests.Service(f.Storage, f.Access);
            var first = await service.CreateAsync(new("Duplicate title"), RequestOrigin.LocalUi, f.Token);
            var second = await service.CreateAsync(new("Duplicate title"), RequestOrigin.LocalUi, f.Token);
            const string answer = "## Exact retained result\n\nFirst paragraph.\n\nLast paragraph: immutable ending.";
            var exact = await f.Submit(ModelTurnOutcome.Succeeded, first.Authority.SessionId, answer);
            await f.Submit(ModelTurnOutcome.Succeeded, first.Authority.SessionId, "newer answer must never replace the exact old result");
            await f.Submit(ModelTurnOutcome.Succeeded, second.Authority.SessionId, "same title foreign session answer");
            using var native = new NativeUxFixtureSession(Path.GetFullPath("."), silentCaption: true);
            await native.InitializeAsync();
            await using var broker = f.Broker();
            ResponseWindow? primary = null;
            var clipboard = new ResultClipboard();
            using var details = new DetailWindowController(native.Documents, () => f.Access.CanInspect,
                new(NullLogger<NativeDetailRenderer>.Instance), clipboard,
                (state, result, copy) => new SessionDetailView(primary!.SessionView, state, result, copy, native.Main),
                NullLogger<DetailWindowController>.Instance);
            var viewer = f.Viewer(broker, (content, admitted, resolve, token) =>
                details.OpenConversationDetailAsync(content, primary, admitted, resolve, token));
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single(row => row.Authority.SessionId == exact.SessionId));
            var owner = new SessionsPresentationOwner(viewer);
            primary = new(native.Main);
            primary.AttachSessions(viewer, owner, static () => { }, static () => { });
            primary.ShowSessions(expanded: true);
            var window = new SessionsWindow(viewer, owner);
            window.Show();
            try
            {
                await viewer.RefreshWorkAsync();
                viewer.ComposerBody = "independent unsent input";
                var notice = viewer.LocalEvents.Single(item => item.Event.TaskId == exact.TaskId);
                var list = window.FindControl<ListBox>("EventRecords")!;
                list.SelectedItem = notice;
                var gesture = window.FindControl<Button>("ReadExactNoticeResult")!;
                gesture.IsEnabled.Should().BeTrue();
                primary.SessionView.DetailsVisible.Should().BeFalse("observing and selecting a notice cannot disclose its body");
                var before = await f.Storage.Store.ReadRetentionAsync(exact.SessionId, f.Token);
                var tasks = f.Storage.Count("host_tasks");
                var audits = f.Storage.Count("security_audit_events");
                var selectedWork = viewer.SelectedWorkRecord;
                gesture.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.ConversationOperation;
                primary.UpdateLayout();
                primary.SessionView.DetailsVisible.Should().BeTrue(viewer.Status);
                var reader = primary.SessionView.GetVisualDescendants().OfType<DetailContentView>().Single();
                reader.FindControl<CheckBox>("SourceToggle")!.IsChecked = true;
                reader.FindControl<TextBox>("ContentReader")!.Text.Should().Be(answer);
                reader.FindControl<TextBlock>("ProvenanceLabel")!.Text.Should().Contain("Historical");
                reader.FindControl<TextBlock>("ReferenceLabel")!.Text.Should().Contain(exact.TaskId.Value.ToString("D"));
                reader.FindControl<Button>("CopySource")!.IsEnabled.Should().BeFalse();
                reader.FindControl<CheckBox>("DisclosureConfirmation")!.IsVisible.Should().BeFalse();
                reader.FindControl<TextBox>("SearchInput")!.Text = "immutable ending";
                reader.FindControl<Button>("FirstMatch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var back = primary.SessionView.FindControl<Button>("BackToConversation")!;
                back.IsEnabled.Should().BeTrue();
                back.Content.Should().Be("Back to notice");
                back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.FocusManager!.GetFocusedElement().Should().BeSameAs(gesture);
                viewer.SelectedLocalEvent.Should().Be(notice);
                viewer.SelectedSessionRecord!.Authority.SessionId.Should().Be(exact.SessionId);
                viewer.SelectedWorkRecord.Should().BeSameAs(selectedWork);
                viewer.ConversationMessages.Should().BeEmpty("reading a result does not populate or guess a conversation page");
                viewer.ComposerBody.Should().Be("independent unsent input");
                f.Storage.Count("host_tasks").Should().Be(tasks);
                f.Storage.Count("security_audit_events").Should().Be(audits);
                clipboard.Writes.Should().Be(0);
                (await f.Storage.Store.ReadRetentionAsync(exact.SessionId, f.Token)).Should().Be(before);
                (await f.Storage.Store.ReadWorkAsync(exact.SessionId, f.Access.ControlRevision, new(), f.Token))
                    .Tasks.Single(item => item.Task.Request == exact).CurrentSource.Should().BeFalse();

                f.Storage.Mutate("DELETE FROM conversation_messages WHERE request_id='" + exact.RequestId.Value.ToString("D") + "';");
                await details.RevalidateConversationDetailsAsync();
                reader.FindControl<TextBox>("ContentReader")!.Text.Should().BeEmpty();
                primary.SessionView.DetailsVisible.Should().BeFalse("old source failure cannot disclose a cached body or follow a newer task");
            }
            finally
            {
                viewer.ComposerBody = string.Empty;
                window.Close();
                primary.Close();
            }
        });
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("private")]
    [InlineData("call")]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("unavailable")]
    [InlineData("question")]
    [InlineData("done")]
    [InlineData("closed")]
    [InlineData("system")]
    public async Task ExactResultFailsClosedWithoutBodyEffectsOrRetargeting(string fault)
    {
        using var f = new Fixture();
        await f.Initialize();
        var outcome = fault is "question" ? (ModelTurnOutcome?)null : fault is "unknown" ? ModelTurnOutcome.Unknown
            : fault is "unavailable" ? ModelTurnOutcome.Unavailable : ModelTurnOutcome.Succeeded;
        var request = await f.Submit(outcome);
        if (fault is "question") { await f.Clarify(request); }
        await using var broker = f.Broker();
        var opened = 0;
        var viewer = f.Viewer(broker, (_, _, _, _) => { opened++; return Task.FromResult("opened"); });
        viewer.SetRoutineQuietSurfaceOpen(true);
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single());
        await viewer.RefreshWorkAsync();
        var notice = viewer.LocalEvents.Single();
        viewer.SelectLocalEvent(notice);
        viewer.CanReadExactNoticeResult.Should().Be(fault is not "question");
        if (fault is "expired") { f.Storage.Time.Now = notice.Event.ExpiresAt; }
        if (fault is "private") { f.Access.CanInspect = false; f.Access.ControlRevision++; }
        if (fault is "call") { f.Access.CanControl = false; f.Access.ControlRevision++; }
        if (fault is "missing") { f.Storage.Mutate("DELETE FROM conversation_messages;"); }
        if (fault is "done")
        {
            await WindowsSqliteSessionWorkspaceTests.Service(f.Storage, f.Access)
                .ChangeLifecycleAsync(request.SessionId, new(1), false, RequestOrigin.LocalUi, f.Token);
        }
        if (fault is "closed") { viewer.SetRoutineQuietSurfaceOpen(false); }
        var tasks = f.Storage.Count("host_tasks");
        var audits = f.Storage.Count("security_audit_events");
        using (var root = HostActivity.BeginRoot(
            new(new(Guid.NewGuid()), request.SessionId, new(Guid.NewGuid()), fault is "system" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Presentation))
        { await viewer.ReadExactNoticeResultAsync(() => true); }
        opened.Should().Be(0);
        if (fault is "unknown" or "unavailable")
        { viewer.Status.Should().Contain("Unavailable").And.Contain("no retained ASSISTANT"); }
        viewer.Detail.Should().NotContain("private answer marker");
        f.Storage.Count("host_tasks").Should().Be(tasks);
        f.Storage.Count("security_audit_events").Should().Be(audits);
        viewer.Close();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("selection")]
    [InlineData("call")]
    [InlineData("window")]
    [InlineData("cancel")]
    public async Task LateReaderCallbacksMustResolveFreshAndCannotPublishAfterRetirement(string fault)
    {
        using var f = new Fixture();
        await f.Initialize();
        var request = await f.Submit(ModelTurnOutcome.Succeeded);
        await using var broker = f.Broker();
        Func<bool>? live = null;
        Func<CancellationToken, Task<AdmittedDetailContent>>? resolve = null;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewer = f.Viewer(broker, async (_, current, read, _) =>
        {
            live = current;
            resolve = read;
            entered.SetResult();
            await release.Task;
            if (fault is "cancel") { throw new OperationCanceledException(); }
            return "late successful reader must not publish";
        });
        viewer.SetRoutineQuietSurfaceOpen(true);
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single());
        await viewer.RefreshWorkAsync();
        viewer.SelectLocalEvent(viewer.LocalEvents.Single());
        var pending = viewer.ReadExactNoticeResultAsync(() => true);
        await entered.Task;
        if (fault is "source") { f.Storage.Mutate("DELETE FROM conversation_messages;"); }
        if (fault is "selection") { viewer.SelectLocalEvent(null); }
        if (fault is "call") { f.Access.ControlRevision++; f.Access.CanControl = false; }
        if (fault is "window") { viewer.Close(); }
        if (fault is not "cancel")
        {
            using var root = HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Storage);
            await resolve!.Invoking(read => read(f.Token)).Should().ThrowAsync<Exception>();
        }
        if (fault is "selection" or "call" or "window") { live!().Should().BeFalse(); }
        release.SetResult();
        await pending;
        viewer.Status.Should().NotContain("late successful reader");
        viewer.Close();
    }

    private sealed class ResultClipboard : IDetailClipboard
    {
        internal int Writes { get; private set; }
        public Task WritePlainTextAsync(IDetailView view, string source) { Writes++; return Task.CompletedTask; }
    }
}
