using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Interaction;
using Kora.Core.Diagnostics;
using Kora.Core.Storage;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class AllWorkWindowTests
{
    [WindowsFact]
    public async Task OwnedControllerRevokesLifecycleRetentionAndPrivacyWithoutReopeningOrCollectingInBackground()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            using var sessions = new SessionsWindowController(f.Main, f.Sessions, f.Evidence, f.Access,
                NullLogger<SessionsViewModel>.Instance);
            using var controller = new AllWorkWindowController(f.Main, f.Sessions, f.Access,
                NullLogger<AllWorkViewModel>.Instance, sessions);
            AllWorkWindow? opened = null;
            using var windows = Window.WindowOpenedEvent.AddClassHandler<AllWorkWindow>((window, _) => opened = window);
            sessions.Bind();
            controller.Bind();
            sessions.OpenAllWork = controller.Open;
            controller.Open();
            var model = (AllWorkViewModel)opened!.DataContext!;
            model.Records.Should().BeEmpty();
            await model.RefreshAsync();
            model.Records.Should().NotBeEmpty();
            await Task.Run(f.AdvanceLifecycleTargetAsync);
            opened.IsVisible.Should().BeTrue();
            model.Records.Should().BeEmpty();
            model.SelectedDetails.Should().BeEmpty();
            await model.RefreshAsync();
            var target = f.LifecycleTarget!.SessionId;
            using (var root = HostActivity.BeginRoot(new(new(Guid.NewGuid()), target, new(Guid.NewGuid()), RequestOrigin.LocalUi),
                HostActivityLayer.Desktop, HostOperation.Request))
            {
                var observed = await f.Sessions.ReadRetentionAsync(target, f.Token);
                await f.Sessions.SetRetentionHoldAsync(observed, true, RequestOrigin.LocalUi, () => true, f.Token);
            }
            model.Records.Should().BeEmpty();
            model.CanNext.Should().BeFalse();
            await model.RefreshAsync();
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            opened.Closed += (_, _) => closed.TrySetResult();
            f.Main.CloseForObservedPrivacyEvent("Synthetic privacy closure", hidePresentation: true);
            await closed.Task.WaitAsync(TestContext.Current.CancellationToken);
            await f.Main.PrivacyClosureTask;
            opened.IsVisible.Should().BeFalse();
            model.Records.Should().BeEmpty();
            model.Status.Should().BeEmpty();
            controller.Dispose();
            controller.Open();
            opened.IsVisible.Should().BeFalse();
        });
    }

    [Theory]
    [InlineData("close")]
    [InlineData("privacy")]
    [InlineData("revision")]
    [InlineData("filter")]
    [InlineData("cancel")]
    [InlineData("revoke")]
    public async Task InFlightPrivateSnapshotNeverRepopulatesClosedOrChangedOverview(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var held = new GatedWorkStore(f.Store);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = new SessionWorkspaceService(held, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
        var model = new AllWorkViewModel(service, access, NullLogger<AllWorkViewModel>.Instance,
            (_, _, _) => throw new InvalidOperationException("A passive test must not navigate."));
        await model.RefreshAsync();
        model.Records.Should().NotBeEmpty();
        held.Hold = true;
        var read = model.RefreshAsync();
        await held.Entered.Task.WaitAsync(f.Token);
        model.Records.Should().BeEmpty("old private rows clear before an async refresh");
        model.CanEdit.Should().BeTrue("input is never disabled by a passive read");
        if (boundary is "close") { model.Close(); }
        if (boundary is "privacy") { access.CanInspect = false; }
        if (boundary is "revision") { access.ControlRevision++; }
        if (boundary is "filter") { model.Filter = SessionListFilter.Done; }
        if (boundary is "cancel") { model.Cancel(); }
        if (boundary is "revoke") { model.Revoke(); }
        held.Release.SetResult();
        await read;
        model.Records.Should().BeEmpty();
        model.SelectedDetails.Should().BeEmpty();
        model.CanNext.Should().BeFalse();
        model.CanNavigate.Should().BeFalse();
        model.Close();
        model.Status.Should().BeEmpty();
        model.NeedsAnswer.Should().BeFalse();
        held.Writes.Should().Be(0);
    }

    private sealed class GatedWorkStore(WindowsSqliteHostInteractionStore store) : ISessionWorkspaceStore, ISessionWorkStore
    {
        internal bool Hold { get; set; }
        internal int Writes { get; private set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<SessionWorkSnapshot> ReadWorkAsync(HostId<SessionIdentity> session, long admissionRevision,
            SessionQueueLimits limits, CancellationToken token)
        {
            var result = await store.ReadWorkAsync(session, admissionRevision, limits, token);
            if (Hold)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TestContext.Current.CancellationToken);
            }
            return result;
        }
        public ValueTask<SessionPage<SessionWorkspaceEntry>> ReadMetadataPageAsync(Guid? after, int limit, CancellationToken token) =>
            store.ReadMetadataPageAsync(after, limit, token);
        public ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken token) =>
            store.ReadMetadataAsync(session, token);
        public ValueTask<SessionDispositionPreview> PreviewDispositionAsync(HostId<SessionIdentity> session,
            HostRevision generation, long metadataRevision, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionDispositionReceipt> DisposeSessionAsync(HostRequest request, SessionDispositionPreview preview,
            Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken token)
        {
            Writes++;
            throw new NotSupportedException();
        }
        public ValueTask<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken token) =>
            throw new NotSupportedException();
        public ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task,
            CancellationToken token) => throw new NotSupportedException();
        public ValueTask<HostTaskObservation> CancelWaitingTaskAsync(HostRequest control, HostTaskCancellationTarget target,
            Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> CreateNamedSessionAsync(HostRequest request, SessionName name,
            Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> RenameSessionAsync(HostRequest request, HostRevision generation, long metadataRevision,
            SessionName name, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session, Guid? after,
            int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session, Guid? after,
            int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request, HostRevision generation,
            bool active, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
    }

    [WindowsFact]
    public async Task NativeOverviewIsReachableScrollableFocusPreservingAndExactNavigationRemainsPassive()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var question = await WindowsSqliteTaskControlTests.WaitAsync(f);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var service = WindowsSqliteSessionWorkspaceTests.Service(f, access);
            for (var i = 0; i < 12; i++) { await service.CreateAsync(new("Same inert label"), RequestOrigin.LocalUi, f.Token); }
            var sink = new WindowsSqliteEvidenceSink(f.Paths);
            sink.Initialize();
            var sessions = new SessionsViewModel(service,
                new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
                access, NullLogger<SessionsViewModel>.Instance);
            var sessionWindow = new SessionsWindow(sessions);
            var overview = new AllWorkViewModel(service, access, NullLogger<AllWorkViewModel>.Instance, sessions.SelectExactAsync);
            var window = new AllWorkWindow(overview);
            var opens = 0;
            sessionWindow.AllWorkRequested += (_, _) => { opens++; window.Show(); };
            sessionWindow.Show();
            sessionWindow.FindControl<Button>("AllWork")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            opens.Should().Be(1);
            overview.Records.Should().BeEmpty("opening does not collect work or grant authority");
            try
            {
                var refresh = window.FindControl<Button>("Refresh")!;
                refresh.Focus().Should().BeTrue();
                await overview.RefreshAsync();
                refresh.IsFocused.Should().BeTrue();
                overview.Records.Should().HaveCount(10);
                var list = window.FindControl<ListBox>("Records")!;
                list.Should().BeOfType<Kora.Controls.ScrollableListBox>();
                ControlAutomationPeer.CreatePeerForElement(list)!.GetProvider<IScrollProvider>().Should().NotBeNull();
                overview.CanNext.Should().BeTrue();
                await overview.RefreshAsync(next: true);
                overview.Records.Should().HaveCount(3);
                overview.CanNext.Should().BeFalse();
                await overview.RefreshAsync();
                overview.NeedsAnswer = true;
                overview.Records.Should().BeEmpty();
                SessionWorkOverviewRow? waiting = null;
                do
                {
                    await overview.RefreshAsync(next: overview.CanNext);
                    waiting ??= overview.Records.FirstOrDefault(row => row.Session.Authority.SessionId == f.Request.SessionId);
                } while (overview.CanNext);
                // Return to the exact observed source page if the question was on the first page.
                if (!overview.Records.Contains(waiting!))
                {
                    await overview.RefreshAsync();
                }
                waiting = overview.Records.Single();
                overview.Select(waiting);
                overview.SelectedDetails.Should().Contain(question.Key.QuestionId.Value.ToString("D"))
                    .And.Contain("Question IDs are observed metadata only").And.NotContain(question.Spec.Text);
                var clock = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                var tasks = f.Count("host_tasks");
                var audits = f.Count("security_audit_events");
                var beforeQuestion = await f.Store.ReadQuestionPageAsync(f.Request.SessionId, null, 50, f.Token);
                await overview.NavigateAsync();
                sessions.SelectedSessionRecord!.Authority.SessionId.Should().Be(f.Request.SessionId);
                sessions.PendingQuestions.Should().ContainSingle().Which.Key.Should().Be(question.Key);
                sessions.CanCancelWork.Should().BeFalse();
                sessions.CanCancelTask.Should().BeFalse();
                f.Count("host_tasks").Should().Be(tasks);
                f.Count("security_audit_events").Should().Be(audits);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(clock);
                (await f.Store.ReadQuestionPageAsync(f.Request.SessionId, null, 50, f.Token)).Records.Should()
                    .BeEquivalentTo(beforeQuestion.Records, options => options.WithStrictOrdering());
                overview.Revoke();
                overview.Records.Should().BeEmpty();
                overview.SelectedRecord.Should().BeNull();
                overview.SelectedDetails.Should().BeEmpty();
                overview.CanNext.Should().BeFalse();
                overview.Status.Should().Contain("revoked");
                await overview.RefreshAsync();
                window.Close();
                overview.Records.Should().BeEmpty();
                overview.Filter.Should().Be(SessionListFilter.All);
                overview.NeedsAnswer.Should().BeFalse();
                overview.Status.Should().BeEmpty();
                overview.CanRead.Should().BeFalse();
                await overview.RefreshAsync();
                overview.Records.Should().BeEmpty();
            }
            finally { window.Close(); sessionWindow.Close(); }
        });
    }

    [WindowsFact]
    public async Task UnavailableAndDoneSourcesDoNotBecomeAllClearResumeOrCancellationTargets()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, access);
        await service.ChangeLifecycleAsync(f.Request.SessionId, new(1), false, RequestOrigin.LocalUi, f.Token);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var sessions = new SessionsViewModel(service,
            new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance);
        var overview = new AllWorkViewModel(service, access, NullLogger<AllWorkViewModel>.Instance, sessions.SelectExactAsync);
        try
        {
            overview.Filter = SessionListFilter.Done;
            await overview.RefreshAsync();
            overview.Select(overview.Records.Single());
            overview.SelectedDetails.Should().Contain("stored Done");
            var before = await f.Store.ReadSessionAsync(f.Request.SessionId, f.Token);
            await overview.NavigateAsync();
            sessions.SelectedSessionRecord!.Authority.Should().Be(before);
            sessions.CanCancelWork.Should().BeFalse();
            (await f.Store.ReadSessionAsync(f.Request.SessionId, f.Token)).Should().Be(before);
            access.CanInspect = false;
            overview.Close();
            overview.SelectedDetails.Should().BeEmpty();
            overview.Records.Should().BeEmpty();
            overview.CanNavigate.Should().BeFalse();
        }
        finally { overview.Close(); sessions.Close(); }
    }
}
