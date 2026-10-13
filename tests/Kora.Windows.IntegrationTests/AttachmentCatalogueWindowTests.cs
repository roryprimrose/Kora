using System.Text;

using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Controls;
using Kora.Tools.Files;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class AttachmentCatalogueWindowTests
{
    [WindowsFact]
    public async Task NativeExactIdSearchIsExplicitVolatileAndCanonicalWithEnterClearAndEscape()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var summary = Summary(fixture.Request);
            var calls = 0;
            var revocations = 0;
            var seen = new List<(SessionAttachmentQuery Query, string? Cursor)>();
            var cursor = "YQ==." + new string('a', 64);
            using var model = new AttachmentCatalogueViewModel((query, next, _, _) =>
            {
                calls++;
                seen.Add((query, next));
                var matches = query.Matches(summary);
                return Task.FromResult(new SessionAttachmentPage(query, matches ? [summary] : [],
                    next is null && matches ? cursor : null, DateTimeOffset.UnixEpoch, 1, new string('a', 64), 1, false));
            }, (_, _, _) => throw new InvalidOperationException("Search must not navigate."),
                access, () => true, NullLogger<AttachmentCatalogueViewModel>.Instance);
            model.SelectionRevoked += _ => revocations++;
            var window = new AttachmentCatalogueWindow(model);
            window.Show();
            try
            {
                var mode = window.FindControl<ComboBox>("AttachmentQueryMode")!;
                var field = window.FindControl<TextBox>("AttachmentQueryText")!;
                var search = window.FindControl<Button>("RefreshPage")!;
                model.Mode.Should().Be(AttachmentCatalogueMode.Browse);
                field.IsEnabled.Should().BeFalse();
                calls.Should().Be(0);
                mode.SelectedItem = AttachmentCatalogueMode.ExactId;
                model.CanSearch.Should().BeFalse();
                foreach (var invalid in new[] { "", " ", Guid.Empty.ToString("D"),
                    summary.Session.Value.ToString("D").ToUpperInvariant(), summary.Session.Value.ToString("N"),
                    summary.Session.Value.ToString("D") + "\n", "\u0000", new string('a', 4097) })
                {
                    field.Text = invalid;
                    model.QueryText.Should().Be(invalid);
                    search.IsEnabled.Should().BeFalse();
                    model.QueryHint.Should().Contain("Invalid exact session ID");
                    field.Focus();
                    field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                    calls.Should().Be(0);
                }
                await model.Refresh();
                model.Status.Should().Contain("InvalidDataException").And.Contain("canonical ID");
                calls.Should().Be(0);
                field.Text = summary.Session.Value.ToString("D");
                field.Focus();
                field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                await window.Operation;
                model.Records.Should().ContainSingle();
                model.SelectedRecord.Should().BeNull("search never selects or navigates automatically");
                seen.Single().Query.ExactId.Should().Be(summary.Session.Value);
                model.Select(summary);
                model.IncludeRemoved = true;
                model.QueryText.Should().Be(summary.Session.Value.ToString("D"));
                model.Mode.Should().Be(AttachmentCatalogueMode.ExactId);
                model.Records.Should().BeEmpty();
                model.SelectedRecord.Should().BeNull();
                revocations.Should().Be(1);
                calls.Should().Be(1);
                search.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Operation;
                model.CanNext.Should().BeTrue();
                window.FindControl<Button>("NextPage")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Operation;
                seen.Last().Cursor.Should().Be(cursor);
                seen.Last().Query.Should().Be(new SessionAttachmentQuery(true, field.Text));
                field.Text = Guid.NewGuid().ToString("D");
                model.CanNext.Should().BeFalse();
                model.Records.Should().BeEmpty();
                search.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Operation;
                model.Status.Should().Contain("not found").And.Contain("no fallback");
                window.FindControl<Button>("ClearQuery")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.Mode.Should().Be(AttachmentCatalogueMode.Browse);
                model.QueryText.Should().BeEmpty();
                model.IncludeRemoved.Should().BeTrue();
                calls.Should().Be(4, "Clear and all edits are memory-only");
                search.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Operation;
                seen.Last().Query.Should().Be(new SessionAttachmentQuery(true));
                seen.Last().Cursor.Should().BeNull();
                model.Mode = AttachmentCatalogueMode.ExactId;
                field.Text = summary.Session.Value.ToString("D");
                field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                window.IsVisible.Should().BeFalse();
                model.QueryText.Should().BeEmpty();
                model.Status.Should().BeEmpty();
                model.Records.Should().BeEmpty();
                model.CanEdit.Should().BeFalse();
                model.Mode.Should().Be(AttachmentCatalogueMode.Browse);
                model.IncludeRemoved.Should().BeFalse();
            }
            finally { window.Close(); }
        });
    }

    [WindowsFact]
    public async Task CatalogueNativeMetadataWindowIsContentFreeAndDoneNavigationUsesExistingExactValidatedSourceWithoutPathReads()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
            fixture.Request = new(new(Guid.NewGuid()), fixture.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
            await fixture.RunAsync(async () =>
            {
                await fixture.Store.RecordControlIntentAsync(fixture.Request, fixture.Token);
                byte[] bytes = Encoding.UTF8.GetBytes("PRIVATE_NATIVE_BODY_726143");
                _ = await fixture.Store.Attach(fixture.Request, new(1), new(
                    new(Guid.NewGuid(), Guid.NewGuid(), fixture.Request, new(@"C:\Synthetic\private.md", "exact", bytes.Length, fixture.Time.Now)),
                    bytes, fixture.Time.Now), bytes, () => true, fixture.Token);
            });
            fixture.Request = new(new(Guid.NewGuid()), fixture.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
            await fixture.RunAsync(async () =>
            {
                await fixture.Store.RecordControlIntentAsync(fixture.Request, fixture.Token);
                await fixture.Store.ChangeIdleLifecycleAsync(fixture.Request, new(1), false, () => true, fixture.Token);
            });
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var sessions = WindowsSqliteSessionWorkspaceTests.Service(fixture, access);
            var inspector = new NoCapture();
            var audit = new Audit();
            using var preview = new LocalFilePreview(inspector, audit, fixture.Time, NullLogger<LocalFilePreview>.Instance);
            await using var files = new SessionFileAttachmentService(sessions, access,
                new LocalSessionFileAttach(inspector, audit, fixture.Time, NullLogger<LocalFilePreview>.Instance),
                new LocalFileSearch(preview, new LocalFileLexicalRetrieval(), audit, fixture.Time, NullLogger<LocalFileSearch>.Instance),
                fixture.Time, audit, NullLogger<SessionFileAttachmentService>.Instance);
            var catalogue = new SessionAttachmentCatalogueService(sessions, files);
            var failures = new List<string>();
            using var attachments = new SessionAttachmentWindowController(failures.Add, files, sessions, access, inspector);
            LocalFilePreviewWindow? body = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<LocalFilePreviewWindow>((view, _) => body = view);
            var model = new AttachmentCatalogueViewModel(catalogue.Read, async (exact, current, token) =>
            {
                var target = await catalogue.ResolveExact(exact, current, token);
                target.Authority.IsActive.Should().BeFalse();
                await attachments.Open(target, false, current, token, exact);
                _ = await catalogue.ResolveExact(exact, current, token);
            }, access, () => true, NullLogger<AttachmentCatalogueViewModel>.Instance);
            model.SelectionRevoked += attachments.CloseExact;
            var window = new AttachmentCatalogueWindow(model);
            window.Show();
            try
            {
                model.Records.Should().BeEmpty("opening does not enumerate, capture or read a body");
                model.Mode = AttachmentCatalogueMode.ExactId;
                model.QueryText = fixture.Request.SessionId.Value.ToString("D");
                await model.Refresh();
                window.UpdateLayout();
                var slot = model.Records.Single();
                slot.SessionState.Should().Be(AttachmentSessionState.Done);
                var list = window.GetVisualDescendants().OfType<ScrollableListBox>().Single();
                list.SelectedItem = slot;
                ControlAutomationPeer.CreatePeerForElement(list)!.GetName().Should().Contain("no filenames");
                var visible = Text(window);
                visible.Should().NotContain("PRIVATE_NATIVE_BODY").And.NotContain("Synthetic")
                    .And.Contain(slot.File.Digest).And.Contain(slot.Session.Value.ToString("D"));
                model.SelectedDetails.Should().Contain(slot.File.SourceId.ToString("D"))
                    .And.Contain(slot.File.RevisionId.ToString("D")).And.Contain(slot.File.ItemId.ToString("D"));
                var clock = await fixture.Store.ReadRetentionAsync(slot.Session, fixture.Token);
                var tasks = fixture.Count("host_tasks");
                await model.Navigate();
                body.Should().NotBeNull();
                body!.IsVisible.Should().BeTrue();
                Text(body).Should().Contain("PRIVATE_NATIVE_BODY_726143");
                inspector.Calls.Should().Be(0);
                (await fixture.Store.ReadRetentionAsync(slot.Session, fixture.Token)).Should().Be(clock);
                fixture.Count("host_tasks").Should().Be(tasks);
                model.IncludeRemoved = true;
                body.IsVisible.Should().BeFalse();
                model.Records.Should().BeEmpty();
                files.Revoke();
                body.IsVisible.Should().BeFalse();
                model.Revoke();
                model.Records.Should().BeEmpty();
                model.SelectedDetails.Should().BeEmpty();
                failures.Should().BeEmpty();
            }
            finally { window.Close(); attachments.Close(); }
        });
    }

    [WindowsFact]
    public async Task CatalogueControllerRevokesActualLifecyclePublisherAndPrivateClosureAndCannotReopenAfterDisposal()
            {
                await HeadlessSession.RunAsync(async () =>
                {
                    var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
                    Directory.CreateDirectory(parent);
                    using var fixture = new NativeUxFixtureSession(parent);
                    await fixture.InitializeAsync();
                    var owner = fixture.LifecycleTarget!;
                    var request = new HostRequest(new(Guid.NewGuid()), owner.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
                    using (var root = HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Request))
                    {
                        await fixture.Interactions.RecordControlIntentAsync(request, fixture.Token);
                        byte[] bytes = Encoding.UTF8.GetBytes("private owned fixture attachment");
                        await fixture.Interactions.Attach(request, owner.Generation, new(
                            new(Guid.NewGuid(), Guid.NewGuid(), request, new(@"C:\Synthetic\private.md", "exact", bytes.Length, DateTimeOffset.UnixEpoch)),
                            bytes, DateTimeOffset.UnixEpoch), bytes, () => true, fixture.Token);
                (await fixture.Interactions.ReadAttachmentPage(new(), null, () => true, fixture.Token)).Records.Should().ContainSingle();
                    }
                    var inspector = new NoCapture();
                    var audit = new Audit();
                    using var preview = new LocalFilePreview(inspector, audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
                    await using var files = new SessionFileAttachmentService(fixture.Sessions, fixture.Access,
                        new LocalSessionFileAttach(inspector, audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance),
                        new LocalFileSearch(preview, new LocalFileLexicalRetrieval(), audit, TimeProvider.System, NullLogger<LocalFileSearch>.Instance),
                        TimeProvider.System, audit, NullLogger<SessionFileAttachmentService>.Instance);
                    fixture.Main.BindSessionAttachments(files);
                    using (var viewer = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request))
                    {
                        (await new SessionAttachmentCatalogueService(fixture.Sessions, files).Read(new(), null, () => true, fixture.Token))
                            .Records.Should().ContainSingle();
                    }
                    using var attachments = new SessionAttachmentWindowController(fixture.Main.ReportHostInteractionFailure,
                        files, fixture.Sessions, fixture.Access, inspector);
                    using var controller = new AttachmentCatalogueWindowController(fixture.Main, fixture.Sessions, files,
                        attachments, fixture.Access, NullLogger<AttachmentCatalogueViewModel>.Instance,
                        new(fixture.Sessions, files));
                    AttachmentCatalogueWindow? window = null;
                    using var opened = Window.WindowOpenedEvent.AddClassHandler<AttachmentCatalogueWindow>((view, _) => window = view);
                    controller.Open();
                    var model = (AttachmentCatalogueViewModel)window!.DataContext!;
                    model.Records.Should().BeEmpty();
                    model.Mode = AttachmentCatalogueMode.ExactId;
                    model.QueryText = owner.SessionId.Value.ToString("D");
                    await model.Refresh();
                    model.Status.Should().NotContain("unavailable");
                    model.Records.Should().ContainSingle();
                    await Task.Run(fixture.AdvanceLifecycleTargetAsync);
                    model.Records.Should().BeEmpty();
                    model.SelectedDetails.Should().BeEmpty();
                    model.QueryText.Should().BeEmpty();
                    model.Mode.Should().Be(AttachmentCatalogueMode.Browse);
                    await model.Refresh();
                    files.Revoke();
                    model.Records.Should().BeEmpty();
                    await model.Refresh();
                    model.Mode = AttachmentCatalogueMode.ExactId;
                    model.QueryText = owner.SessionId.Value.ToString("D");
                    var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.Closed += (_, _) => closed.TrySetResult();
                    fixture.Main.CloseForObservedPrivacyEvent("Synthetic privacy closure", hidePresentation: true);
                    await closed.Task.WaitAsync(fixture.Token);
                    await fixture.Main.PrivacyClosureTask;
                    window.IsVisible.Should().BeFalse();
                    model.Records.Should().BeEmpty();
                    model.Status.Should().BeEmpty();
                    model.QueryText.Should().BeEmpty();
                    inspector.Calls.Should().Be(0);
                    controller.Dispose();
                    controller.Open();
                    window.IsVisible.Should().BeFalse();
                });
            }

    [Theory]
    [InlineData("query")]
    [InlineData("id")]
    [InlineData("mode")]
    [InlineData("clear")]
    [InlineData("control")]
    [InlineData("private")]
    [InlineData("cancel")]
    [InlineData("revoke")]
    [InlineData("dispose")]
    public async Task CatalogueDiscardsLateMetadataAndSelectionAfterQueryPrivateOwnerCancellationOrPublisherRevocation(string boundary)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var summary = Summary(fixture.Request);
        var page = new SessionAttachmentPage(new(), [summary], null, DateTimeOffset.UnixEpoch, 1, new string('a', 64), 1, false);
        var calls = 0;
        using var model = new AttachmentCatalogueViewModel(async (query, _, _, token) =>
        {
            if (++calls != 1) { entered.SetResult(); await release.Task.WaitAsync(fixture.Token); }
            return page with { Query = query };
        }, (_, _, _) => throw new InvalidOperationException("No body navigation admitted."),
            access, () => true, NullLogger<AttachmentCatalogueViewModel>.Instance);
        model.Mode = AttachmentCatalogueMode.ExactId;
        model.QueryText = summary.Session.Value.ToString("D");
        await model.Refresh();
        model.Select(summary);
        model.CanNavigate.Should().BeTrue();
        var pending = model.Refresh();
        await entered.Task.WaitAsync(fixture.Token);
        model.Records.Should().BeEmpty();
        if (boundary is "query") { model.IncludeRemoved = true; }
        if (boundary is "id") { model.QueryText = Guid.NewGuid().ToString("D"); }
        if (boundary is "mode") { model.Mode = AttachmentCatalogueMode.Browse; }
        if (boundary is "clear") { model.ClearQuery(); }
        if (boundary is "control") { access.ControlRevision++; model.SynchronizeAuthority(); }
        if (boundary is "private") { access.CanInspect = false; model.SynchronizeAuthority(); }
        if (boundary is "cancel") { model.Cancel(); }
        if (boundary is "revoke") { model.Revoke(); }
        if (boundary is "dispose") { model.Dispose(); }
        release.SetResult();
        await pending;
        model.Records.Should().BeEmpty();
        model.SelectedDetails.Should().BeEmpty();
        model.CanNext.Should().BeFalse();
        model.CanNavigate.Should().BeFalse();
        if (boundary is "control" or "private" or "cancel" or "revoke" or "dispose")
        {
            model.QueryText.Should().BeEmpty();
            model.Mode.Should().Be(AttachmentCatalogueMode.Browse);
        }
        model.Dispose();
        model.Status.Should().BeEmpty();
    }

    [WindowsFact]
    public async Task CatalogueLateNavigationNeverDisclosesAfterOriginalSelectionIsRevoked()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var summary = Summary(fixture.Request);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disclosed = false;
        using var model = new AttachmentCatalogueViewModel((query, _, _, _) => Task.FromResult(
            new SessionAttachmentPage(query, [summary], null, DateTimeOffset.UnixEpoch, 1, new string('a', 64), 1, false)),
            async (exact, admitted, _) =>
            {
                exact.Should().BeSameAs(summary);
                entered.SetResult();
                await release.Task.WaitAsync(fixture.Token);
                if (admitted()) { disclosed = true; }
            }, access, () => true, NullLogger<AttachmentCatalogueViewModel>.Instance);
        await model.Refresh();
        model.Select(summary);
        var navigation = model.Navigate();
        await entered.Task.WaitAsync(fixture.Token);
        model.IncludeRemoved = true;
        release.SetResult();
        await navigation;
        disclosed.Should().BeFalse();
        model.Records.Should().BeEmpty();
        model.SelectedRecord.Should().BeNull();
    }

    private static string Text(Window window) =>
        string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));

    private static SessionAttachmentSummary Summary(HostRequest request) => new(Guid.NewGuid(), new(Guid.NewGuid()),
        request.SessionId, new(1), new(1), new(1), AttachmentSessionState.Active,
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('a', 64)), 1, "text/plain", DateTimeOffset.UnixEpoch, AttachmentCopyStatus.Confirmed);

    private sealed class NoCapture : ILocalFileInspector, IUserFilePicker
    {
        internal int Calls { get; private set; }
        public Task<ILocalFileSelection> InspectAsync(string selectedPath, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Catalogue must never inspect a source path.");
        }
        public Task<string?> SelectAsync(CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Catalogue must never open a picker.");
        }
    }

    private sealed class Audit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) { }
    }
}
