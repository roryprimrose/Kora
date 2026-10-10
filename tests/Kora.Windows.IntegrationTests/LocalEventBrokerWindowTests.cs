using Avalonia.Automation.Peers;
using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Application;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Application.Maintenance;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Maintenance;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class LocalEventBrokerWindowTests
{
    [WindowsFact]
    public async Task Native_delivery_preserves_focus_pending_questions_history_activity_and_exact_command_parity()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Storage.InitializeAsync();
            var question = await WindowsSqliteTaskControlTests.WaitAsync(f.Storage);
            await using var broker = f.Broker();
            var viewer = f.Viewer(broker);
            var before = await f.Storage.Store.ReadRetentionAsync(f.Storage.Request.SessionId, f.Token);
            var history = await f.Storage.Store.ReadHistoryAsync(f.Storage.Request.SessionId, null, 50, f.Token);
            var tasks = f.Storage.Count("host_tasks");
            var audits = f.Storage.Count("security_audit_events");
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single());
            viewer.LocalEvents.Should().ContainSingle().Which.Event.Type.Should().Be(LocalEventType.UserAttention);
            viewer.PendingQuestions.Should().ContainSingle().Which.Key.Should().Be(question.Key);
            var window = new SessionsWindow(viewer);
            window.Show();
            try
            {
                var refresh = window.FindControl<Button>("ReadQueue")!;
                refresh.Focus().Should().BeTrue();
                await viewer.RefreshWorkAsync();
                window.FocusManager!.GetFocusedElement().Should().BeSameAs(refresh);
                await viewer.QuietRoutineNoticesAsync();
                viewer.RoutineQuietStatus.Should().Contain("On").And.Contain("Work and Maintenance").And.Contain("required questions");
                broker.RoutineQuiet.Enabled.Should().BeTrue();
                viewer.LocalEvents.Should().ContainSingle().Which.Event.Category.Should().Be(LocalEventCategory.Attention);
                viewer.PendingQuestions.Single().Key.Should().Be(question.Key);
                await viewer.ClearRoutineQuietAsync();
                await viewer.ResetRoutineQuietAsync();
                broker.RoutineQuiet.Enabled.Should().BeFalse();
                window.FocusManager!.GetFocusedElement().Should().BeSameAs(refresh);
                viewer.LocalEvents.Single().Reason.Should().Be(LocalEventReason.PresentedNoReplay);
                viewer.LocalEvents.Single().DisplaySummary.Should().Contain("No new notification");
                viewer.SelectLocalEvent(viewer.LocalEvents.Single());
                await viewer.ReviewLocalEventAsync();
                var selected = viewer.SelectedLocalEvent!;
                var typed = await broker.ExecuteAsync(new(LocalEventOperation.Status, selected.Event.Id, selected.Event.Revision),
                    RequestOrigin.LocalUi, () => true, f.Token);
                typed.Should().Be(selected);
                await viewer.DeferLocalEventAsync();
                viewer.SelectedLocalEvent!.DeferredUntil.Should().Be(question.ExpiresAt);
                viewer.SelectedLocalEvent.Event.ExpiresAt.Should().Be(question.ExpiresAt);
                await viewer.DismissLocalEventAsync();
                viewer.SelectedLocalEvent!.Reason.Should().Be(LocalEventReason.Dismissed);
                viewer.PendingQuestions.Single().Key.Should().Be(question.Key);
                (await f.Storage.Store.ReadQuestionPageAsync(f.Storage.Request.SessionId, null, 25, f.Token))
                    .Records.Single().Status.Should().Be(QuestionStatus.Pending);
                f.Storage.Count("host_tasks").Should().Be(tasks);
                f.Storage.Count("security_audit_events").Should().Be(audits);
                (await f.Storage.Store.ReadRetentionAsync(f.Storage.Request.SessionId, f.Token)).Should().Be(before);
                (await f.Storage.Store.ReadHistoryAsync(f.Storage.Request.SessionId, null, 50, f.Token)).Should().BeEquivalentTo(history);
                ControlAutomationPeer.CreatePeerForElement(window.FindControl<ListBox>("EventRecords")!)!
                    .GetName().Should().Contain("pending questions remain independent");
                ControlAutomationPeer.CreatePeerForElement(window.FindControl<Button>("DeferLocalEvent")!)!
                    .GetName().Should().Contain("no work deadline extension");
                var held = viewer.SelectedLocalEvent.Event;
                window.Close();
                viewer.LocalEvents.Should().BeEmpty();
                var closed = () => broker.ExecuteAsync(new(LocalEventOperation.Review, held.Id, held.Revision), RequestOrigin.LocalUi, () => true, f.Token);
                await closed.Should().ThrowAsync<InvalidOperationException>();
            }
            finally { window.Close(); }
        });
    }

    [WindowsFact]
    public async Task NativeQuietBindingsHideOnlyRoutineRowsAndPersistNoRunChoiceOrWorkEffect()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Storage.InitializeAsync();
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(f.Storage, HostTaskState.Succeeded);
            await using var queue = f.Queue();
            var service = f.Service(queue);
            await using var broker = f.Broker();
            var viewer = f.Viewer(broker, service);
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single());
            var window = new SessionsWindow(viewer);
            window.Show();
            try
            {
                var quietButton = window.FindControl<Button>("QuietRoutineNotices")!;
                quietButton.IsEnabled.Should().BeTrue();
                ControlAutomationPeer.CreatePeerForElement(quietButton)!.GetName().Should().Contain("Kora restarts");
                window.FindControl<Button>("ClearRoutineQuiet")!.IsEnabled.Should().BeTrue();
                window.FindControl<Button>("ResetRoutineQuiet")!.IsEnabled.Should().BeTrue();
                await viewer.QuietRoutineNoticesAsync();
                broker.RoutineQuiet.Enabled.Should().BeTrue();
                var current = await f.Storage.Store.ReadQueueAsync(f.Storage.Request.SessionId, f.Token);
                await service.ExecuteCommandAsync(new(SessionCommandOperation.QueueEnqueue, f.Storage.Request.SessionId.Value, 1)
                { QueueRevision = current.Revision, WorkRequestId = Guid.NewGuid(), TaskId = Guid.NewGuid() },
                    RequestOrigin.LocalUi, () => true, f.Token);
                var before = await f.Storage.Store.ReadRetentionAsync(f.Storage.Request.SessionId, f.Token);
                var tasks = f.Storage.Count("host_tasks");
                var history = await f.Storage.Store.ReadHistoryAsync(f.Storage.Request.SessionId, null, 50, f.Token);
                await viewer.RefreshWorkAsync();
                viewer.LocalEvents.Should().BeEmpty();
                viewer.LocalEventStatus.Should().Contain("1 routine rows hidden").And.Contain("1 current routine sources suppressed");
                viewer.WorkRecords.Should().Contain(row => row.Queue!.Entry.State == SessionQueueState.Pending);
                await viewer.ClearRoutineQuietAsync();
                viewer.LocalEvents.Should().ContainSingle().Which.Reason.Should().Be(LocalEventReason.RoutineSuppressedNoReplay);
                await viewer.ResetRoutineQuietAsync();
                (await f.Storage.Store.ReadRetentionAsync(f.Storage.Request.SessionId, f.Token)).Should().Be(before);
                (await f.Storage.Store.ReadHistoryAsync(f.Storage.Request.SessionId, null, 50, f.Token)).Should().BeEquivalentTo(history);
                f.Storage.Count("host_tasks").Should().Be(tasks);
                f.State.Load()!.Budgets.Should().BeEmpty();
                File.ReadAllText(Path.Combine(f.Storage.Paths.LocalRoot, "Preferences", "local-events.json"))
                    .Should().NotContain("Enabled").And.NotContain("RoutineQuiet");
                await using var restarted = f.Broker();
                restarted.RoutineQuiet.Enabled.Should().BeFalse();
                (await restarted.ObserveAsync(f.Storage.Request.SessionId, () => true, f.Token))
                    .Events.Should().ContainSingle().Which.Reason.Should().Be(LocalEventReason.RoutineSuppressedNoReplay);
                window.Close();
                viewer.CanChangeRoutineQuiet.Should().BeFalse();
                viewer.LocalEvents.Should().BeEmpty();
                var revision = broker.RoutineQuiet.Revision;
                await viewer.QuietRoutineNoticesAsync();
                broker.RoutineQuiet.Revision.Should().Be(revision);
            }
            finally { window.Close(); }
        });
    }

    [WindowsFact]
    public async Task Real_queue_revision_change_is_rejected_under_the_owning_source_lease_and_failed_broker_does_not_disable_work()
    {
        using var f = new Fixture();
        await f.Storage.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f.Storage, HostTaskState.Succeeded);
        await using var queue = f.Queue();
        var service = f.Service(queue);
        var snapshot = await f.Storage.Store.ReadQueueAsync(f.Storage.Request.SessionId, f.Token);
        await service.ExecuteCommandAsync(new(SessionCommandOperation.QueueEnqueue, f.Storage.Request.SessionId.Value, 1)
        { QueueRevision = snapshot.Revision, WorkRequestId = Guid.NewGuid(), TaskId = Guid.NewGuid() },
            RequestOrigin.LocalUi, () => true, f.Token);
        var source = f.Source();
        var observed = await source.ReadAsync(f.Storage.Request.SessionId, f.Token);
        observed.Should().ContainSingle().Which.Type.Should().Be(LocalEventType.Queued);
        var queueSnapshot = await f.Storage.Store.ReadQueueAsync(f.Storage.Request.SessionId, f.Token);
        var entry = queueSnapshot.Entries.Single();
        await service.ExecuteCommandAsync(new(SessionCommandOperation.QueueCancel, f.Storage.Request.SessionId.Value, 1)
        { QueueRevision = queueSnapshot.Revision, TaskId = entry.Request.TaskId.Value, TaskRevision = entry.Revision.Value },
            RequestOrigin.ActivatedVoice, () => true, f.Token);
        var reached = false;
        var stale = () => source.WithCurrentAsync(f.Storage.Request.SessionId, observed, () => { reached = true; return true; }, f.Token);
        await stale.Should().ThrowAsync<InvalidOperationException>();
        reached.Should().BeFalse();
        File.WriteAllText(Path.Combine(f.Storage.Paths.LocalRoot, "Preferences", "local-events.json"), "{\"Schema\":99}");
        await using var broker = f.Broker();
        var viewer = f.Viewer(broker, service);
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single());
        viewer.LocalEvents.Should().BeEmpty();
        viewer.LocalEventStatus.Should().Contain("held/unavailable");
        viewer.WorkRecords.Should().Contain(row => row.Queue!.Entry.State == SessionQueueState.Cancelled);
        viewer.CanRefreshWork.Should().BeTrue();
        viewer.Close();
    }

    [WindowsFact]
    public async Task Inventoried_retention_purges_owned_broker_receipts_before_deletion_and_preserves_unrelated_pending_work()
    {
        using var f = new Fixture();
        await f.Storage.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f.Storage, HostTaskState.Succeeded);
        await using var queue = f.Queue();
        var service = f.Service(queue);
        await using var broker = f.Broker();
        service.BindLocalEvents(broker);
        var own = f.Storage.Request.SessionId;
        await EnqueueAsync(own);
        await broker.ObserveAsync(own, () => true, f.Token);
        var ownQueue = await f.Storage.Store.ReadQueueAsync(own, f.Token);
        var entry = ownQueue.Entries.Single();
        await service.ExecuteCommandAsync(new(SessionCommandOperation.QueueRemove, own.Value, 1)
        { QueueRevision = ownQueue.Revision, TaskId = entry.Request.TaskId.Value, TaskRevision = entry.Revision.Value },
            RequestOrigin.LocalUi, () => true, f.Token);
        var other = (await service.CreateAsync(new("Unrelated user label"), RequestOrigin.LocalUi, f.Token)).Authority.SessionId;
        await EnqueueAsync(other);
        await broker.ObserveAsync(other, () => true, f.Token);
        f.State.Load()!.Receipts.Should().HaveCount(2);
        await using var admission = new DiagnosticRetentionAdmission(f.Storage.Store, f.Storage.Store, new(f.Storage.Tasks));
        var configuration = new SessionRetentionConfigurationService(new LocalSessionRetentionPreferences(f.Storage.Paths),
            new(), admission, f);
        configuration.Observe();
        await using var retention = new SessionRetentionService(f.Storage.Store, configuration, f.Access,
            f, f.Storage.Time, NullLogger<SessionRetentionService>.Instance, broker);
        var clock = await f.Storage.Store.ReadRetentionAsync(own, f.Token);
        f.Storage.Time.Now = clock.DeleteDue;
        var removed = new List<HostId<SessionIdentity>>();
        retention.Revoking += id =>
        {
            f.State.Load()!.Receipts.Should().NotContain(item => item.Event.SessionId == id);
            removed.Add(id);
        };
        var result = await retention.RunAsync(f.Token);
        result.Deleted.Should().Be(1);
        removed.Should().ContainSingle().Which.Should().Be(own);
        f.State.Load()!.Receipts.Should().ContainSingle().Which.Event.SessionId.Should().Be(other);
        (await f.Storage.Store.ReadRetentionAsync(own, f.Token)).Purged.Should().BeTrue();
        var disposed = () => f.Storage.Store.ReadWorkAsync(own, 1, new(), f.Token).AsTask();
        await disposed.Should().ThrowAsync<InvalidOperationException>();
        (await f.Storage.Store.ReadQueueAsync(other, f.Token)).Entries.Should().ContainSingle();

        async Task EnqueueAsync(HostId<SessionIdentity> session)
        {
            var current = await f.Storage.Store.ReadQueueAsync(session, f.Token);
            await service.ExecuteCommandAsync(new(SessionCommandOperation.QueueEnqueue, session.Value, 1)
            { QueueRevision = current.Revision, WorkRequestId = Guid.NewGuid(), TaskId = Guid.NewGuid() },
                RequestOrigin.LocalUi, () => true, f.Token);
        }
    }

    private sealed class Fixture : IReleaseMetadataClient, ICanonicalReleasePageOpener, IUiDispatcher, ISecurityAuditLog, IDisposable
    {
        internal InteractionStorageFixture Storage { get; } = new();
        internal WindowsSqliteSessionWorkspaceTests.Access Access { get; } = new();
        internal MaintenanceViewModel Maintenance { get; }
        internal LocalEventStateStore State { get; }
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal Fixture()
        {
            Maintenance = new(this, new AssemblyApplicationInfo(() => "1.0.0"), ReleaseArchitecture.X64,
                this, Storage.Time, this, this, NullLogger<MaintenanceViewModel>.Instance);
            Maintenance.BindGate(() => Access.CanControl);
            State = new(Storage.Paths);
            Directory.CreateDirectory(Path.Combine(Storage.Paths.LocalRoot, "Preferences"));
        }
        internal AuthorityLocalEventSource Source() => new(Storage.Store, Access, Maintenance, new(), Storage.Time);
        internal LocalEventBroker Broker() => new(Source(), State, Access, Storage.Time, this, NullLogger<LocalEventBroker>.Instance);
        internal SessionQueueService Queue() => new(Storage.Store, Storage.Store, new(Storage.Tasks), Access,
            new NoExecution(), new(), NullLogger<SessionQueueService>.Instance, Storage.Time);
        internal SessionWorkspaceService Service(SessionQueueService queue) => new(Storage.Store, new(Storage.Tasks), Access,
            NullLogger<SessionWorkspaceService>.Instance, queue);
        internal SessionsViewModel Viewer(LocalEventBroker broker, SessionWorkspaceService? service = null)
        {
            var sink = new WindowsSqliteEvidenceSink(Storage.Paths);
            sink.Initialize();
            return new(service ?? WindowsSqliteSessionWorkspaceTests.Service(Storage, Access),
                new(new WindowsSqliteEvidenceReader(sink), Access, Storage.Time, NullLogger<DurableEvidenceQuery>.Instance),
                Access, NullLogger<SessionsViewModel>.Instance, broker);
        }
        public Task<ReleaseCheck> CheckAsync(ReleaseChannel channel, string currentVersion, ReleaseArchitecture architecture, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Broker cannot check the network.");
        public Task OpenAsync(ReleaseVersion version, CancellationToken cancellationToken) => throw new InvalidOperationException("Broker cannot navigate.");
        public Task InvokeAsync(Func<Task> action) => action();
        public void Post(Action action) => action();
        public void Write(SecurityAuditEvent entry)
        {
            HostActivity.RequireCurrent().CorrelationId.Should().Be(entry.CorrelationId);
            entry.ApprovalId.Should().BeNull();
        }
        public void Dispose() { Maintenance.Dispose(); Storage.Dispose(); }
    }
    private sealed class NoExecution : IDeterministicVersionQueueAction
    {
        public Kora.Core.Tools.CapabilityReply Observe(CancellationToken token) => throw new InvalidOperationException("Broker cannot dispatch.");
    }
}
