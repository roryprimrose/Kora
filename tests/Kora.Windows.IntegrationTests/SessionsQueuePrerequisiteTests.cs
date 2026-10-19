using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed partial class SessionsQueuePrerequisiteTests
{
    [WindowsFact]
    public async Task ActualClosedTargetPickerResetsEveryChoiceAndAllSixDescriptorsAndFixedRuntimeReachRetainedStatus()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
            using var registry = new WindowsSqliteSessionQueueTests.FixedRegistryFixture(f.Time);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            await using var queue = new SessionQueueService(f.Store, f.Store, new(f.Tasks), access,
                new DeterministicVersionQueueAction(registry.Registry), new(), NullLogger<SessionQueueService>.Instance, f.Time);
            var model = Model(f, access, queue);
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                var picker = window.FindControl<ComboBox>("QueueCapabilities")!;
                var button = window.FindControl<Button>("EnqueueCapability")!;
                picker.SelectedItem.Should().BeNull();
                model.CanEnqueueCapability.Should().BeFalse();
                await model.EnqueueCapabilityAsync();
                (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().BeEmpty();
                model.Status.Should().Contain("explicit exact descriptor target");
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                foreach (var option in model.QueueCapabilities)
                {
                    model.ChooseNoQueuePrerequisite();
                    model.CanSelectQueueCapability.Should().BeTrue();
                    picker.SelectedItem = option;
                    model.SelectedQueueCapability.Should().BeSameAs(option);
                    option.Label.Should().Contain(option.Descriptor.Id).And.Contain("schema 1")
                        .And.Contain(option.Descriptor.Input.ToString()).And.Contain(option.Descriptor.Output.ToString())
                        .And.Contain("Available").And.Contain("not live qualification");
                    model.CanEnqueueCapability.Should().BeTrue();
                    button.IsEnabled.Should().BeTrue();
                    await model.EnqueueCapabilityAsync();
                    model.QueueRecords[^1].Target.Should().Be(new SessionQueueCapabilityTarget(option.Descriptor.Id));
                    model.WorkRecords.Single(row => row.TaskId == model.QueueRecords[^1].Request.TaskId.Value)
                        .State.Should().Contain("capabilities.get").And.Contain(option.Descriptor.Id);
                    model.SelectedQueueCapability.Should().BeNull();
                    picker.SelectedItem.Should().BeNull();
                    model.CanEnqueueCapability.Should().BeFalse();
                    model.CanEnqueueVersion.Should().BeFalse();
                }
                model.ChooseNoQueuePrerequisite();
                await model.EnqueueRuntimeStatusAsync();
                var entries = model.QueueRecords.ToArray();
                entries.Should().HaveCount(7);
                entries[^1].Target.Should().BeNull();
                model.WorkRecords.Single(row => row.TaskId == entries[^1].Request.TaskId.Value)
                    .State.Should().Contain("runtime.get_status").And.Contain("local.inference");
                await model.PauseQueueAsync();
                await model.DispatchQueueAsync();
                model.QueueRecords.Should().HaveCount(7);
                await model.ResumeQueueAsync();
                await model.DispatchQueueAsync();
                model.QueueRecords.Should().BeEmpty();
                foreach (var entry in entries)
                {
                    model.SelectWorkRecord(model.WorkRecords.Single(row => row.TaskId == entry.Request.TaskId.Value));
                    await model.InspectWorkAsync();
                    model.Detail.Should().Contain("Succeeded").And.Contain(entry.Profile!.Value.ToString());
                    var retained = (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, entry.Request.TaskId, f.Token))!;
                    SessionQueueReadProfiles.Accepts(retained.ReadProfile, retained.Result!, retained.Target).Should().BeTrue();
                    if (entry.Target is { } target) { model.Detail.Should().Contain(target.Id); }
                    else { model.Detail.Should().Contain("local.inference").And.Contain("NotObserved").And.Contain("\"toolLoopQualified\":false"); }
                }
                model.ChooseNoQueuePrerequisite();
                picker.SelectedItem = model.QueueCapabilities[0];
                await model.RefreshWorkAsync();
                model.SelectedQueueCapability.Should().BeNull();
                picker.SelectedItem = model.QueueCapabilities[0];
                access.ControlRevision++;
                model.CanEnqueueCapability.Should().BeFalse();
                await model.EnqueueCapabilityAsync();
                (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().BeEmpty();
                registry.Probe.Calls.Should().Be(0);
            }
            finally { window.Close(); model.Close(); }
            model.SelectedQueueCapability.Should().BeNull();
        });
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("oversized")]
    [InlineData("other-session")]
    [InlineData("generation")]
    [InlineData("unavailable")]
    public async Task NativeMetadataBoundaryLabelsEmptyOrUnavailableWithoutInventingRowsOrDependency(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var projected = new MetadataQueueStore(f.Store, snapshot => boundary switch
        {
            "empty" => snapshot with { Tasks = [] },
            "oversized" => snapshot with { Tasks = [.. Enumerable.Repeat(snapshot.Tasks[0], 51)] },
            "other-session" => snapshot with { Tasks = [snapshot.Tasks[0] with
            { Task = new(new(snapshot.Tasks[0].Task.Request.RequestId, new(Guid.NewGuid()),
                snapshot.Tasks[0].Task.Request.TaskId, RequestOrigin.LocalUi), snapshot.Tasks[0].Task.Revision, snapshot.Tasks[0].Task.State) }] },
            "generation" => snapshot with { Tasks = [snapshot.Tasks[0] with { Generation = new(2) }] },
            _ => throw new InvalidOperationException("Metadata observation unavailable."),
        });
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var action = new NeverAction();
        await using var queue = new SessionQueueService(projected, f.Store, new(f.Tasks), access, action,
            new(), NullLogger<SessionQueueService>.Instance, f.Time);
        var model = Model(f, access, queue);
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.QueuePrerequisites.Should().BeEmpty();
        model.QueuePrerequisiteStatus.Should().Contain(boundary is "empty" ? "No task metadata rows" : "unavailable");
        model.ChooseNoQueuePrerequisite();
        if (boundary is "unavailable")
        {
            model.CanEnqueueVersion.Should().BeFalse();
            (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().BeEmpty();
        }
        else
        {
            model.CanEnqueueVersion.Should().BeTrue("explicit None retains the original queue route, not a fabricated dependency");
            await model.EnqueueVersionAsync();
            (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Single().Dependency.Should().BeNull();
        }
        action.Reads.Should().Be(0);
        model.Close();
    }

    [WindowsFact]
    public async Task NativePickerUsesOnlyTheExistingFiftyTaskProjectionWithoutActivityRenewalOrOmittedTaskScan()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        for (var index = 0; index < 52; index++)
        {
            var request = new HostRequest(new(Guid.NewGuid()), f.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
            using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            var task = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
            await f.Tasks.CommitAsync(task, 0, f.Token);
            await f.Tasks.CommitAsync(task.Next(HostTaskState.Succeeded), 1, f.Token);
        }
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var action = new NeverAction();
        await using var queue = Queue(f, access, action);
        var model = Model(f, access, queue);
        var retained = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
        var audit = f.Count("security_audit_events");
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.QueuePrerequisites.Should().HaveCount(50);
        model.WorkStatus.Should().Contain("3 other task records");
        model.QueuePrerequisites.Should().OnlyContain(row => row.SessionId == f.Request.SessionId);
        var previous = model.QueuePrerequisites[0];
        model.SelectQueuePrerequisite(previous);
        f.Time.Now = f.Time.Now.AddMinutes(1);
        await model.RefreshWorkAsync();
        model.SelectedQueuePrerequisite.Should().BeNull();
        model.CanEnqueueVersion.Should().BeFalse();
        model.SelectQueuePrerequisite(previous);
        model.CanEnqueueVersion.Should().BeFalse("old copied metadata cannot retarget a new observation");
        model.ChooseNoQueuePrerequisite();
        model.CanEnqueueVersion.Should().BeTrue();
        f.Count("security_audit_events").Should().Be(audit);
        (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retained);
        action.Reads.Should().Be(0);
        model.Close();
    }

    [Theory]
    [InlineData("EnqueueVersion", SessionQueueReadProfile.Version)]
    [InlineData("EnqueueCapabilities", SessionQueueReadProfile.Capabilities)]
    [InlineData("EnqueueReadiness", SessionQueueReadProfile.Readiness)]
    [InlineData("EnqueueRuntimes", SessionQueueReadProfile.Runtimes)]
    [InlineData("EnqueueCapability", SessionQueueReadProfile.Capability)]
    [InlineData("EnqueueRuntimeStatus", SessionQueueReadProfile.RuntimeStatus)]
    public async Task ActualNativeButtonsForwardExactIndependentPrerequisiteAndClearNextDecision(string button, SessionQueueReadProfile profile)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var action = new NeverAction();
            await using var queue = Queue(f, access, action);
            var model = Model(f, access, queue);
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.QueuePrerequisiteStatus.Should().Contain("None").And.Contain("Only exact Succeeded")
                    .And.Contain("No automatic run");
                model.CanEnqueueVersion.Should().BeTrue();
                await model.EnqueueVersionAsync();
                var none = model.QueueRecords.Single();
                none.Dependency.Should().BeNull();
                model.CanEnqueueVersion.Should().BeFalse();
                window.FindControl<Button>("NoQueuePrerequisite")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.CanEnqueueVersion.Should().BeTrue();
                var choice = model.QueuePrerequisites.Single(row => row.TaskId == f.Request.TaskId);
                var work = model.WorkRecords.Single(row => row.TaskId == none.Request.TaskId.Value);
                model.SelectWorkRecord(work);
                var picker = window.FindControl<ListBox>("QueuePrerequisites")!;
                AutomationProperties.GetName(picker).Should().Contain("50").And.Contain("UTC").And.Contain("revision");
                picker.SelectedItem = choice;
                model.SelectedQueuePrerequisite.Should().BeSameAs(choice);
                model.SelectedWorkRecord.Should().BeSameAs(work);
                await model.RefreshWorkAsync();
                model.SelectedQueuePrerequisite.Should().BeNull("even identical-time equal metadata must not renew selection through UI binding");
                model.CanEnqueueVersion.Should().BeFalse();
                choice = model.QueuePrerequisites.Single(row => row.TaskId == f.Request.TaskId);
                picker.SelectedItem = choice;
                if (profile == SessionQueueReadProfile.Capability)
                {
                    model.SelectQueueCapability(model.QueueCapabilities.Single(row =>
                        string.Equals(row.Descriptor.Id, ReadOnlyCapabilityCatalog.Version, StringComparison.Ordinal)));
                }
                model.SelectedQueuePrerequisite.Should().BeSameAs(choice);
                choice.Label.Should().Contain(f.Request.TaskId.Value.ToString("D"))
                    .And.Contain(f.Request.SessionId.Value.ToString("D")).And.Contain("Succeeded")
                    .And.Contain("revision 2").And.Contain("observed UTC").And.Contain("host metadata source");
                var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                model.PropertyChanged += (_, _) =>
                {
                    if (model.CanRead && model.QueueRecords.Count == 2) { completed.TrySetResult(); }
                };
                var enqueue = window.FindControl<Button>(button)!;
                AutomationProperties.GetName(enqueue).Should().Contain("optional prerequisite").And.Contain("no automatic dispatch");
                enqueue.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await completed.Task.WaitAsync(f.Token);
                var received = model.QueueRecords[^1];
                received.Dependency.Should().Be(f.Request.TaskId);
                received.ReadProfile.Should().Be(profile);
                received.Target.Should().Be(profile == SessionQueueReadProfile.Capability ? new SessionQueueCapabilityTarget(ReadOnlyCapabilityCatalog.Version) : null);
                model.SelectedQueueCapability.Should().BeNull();
                received.Request.TaskId.Should().NotBe(choice.TaskId).And.NotBe(none.Request.TaskId);
                received.Request.RequestId.Should().NotBe(none.Request.RequestId);
                received.PendingLifetimeMinutes.Should().Be(30);
                received.ExpiresAt.Should().Be(received.EnqueuedAt.AddMinutes(30));
                model.SelectedQueuePrerequisite.Should().BeNull();
                model.CanEnqueueVersion.Should().BeFalse();
                model.QueuePrerequisiteStatus.Should().Contain("Explicit choice required");
                model.WorkRecords.Single(row => row.TaskId == received.Request.TaskId.Value).Deadline
                    .Should().Contain(f.Request.TaskId.Value.ToString("D"));
                (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, received.Request.TaskId, f.Token))!
                    .Dependency.Should().Be(f.Request.TaskId);
                action.Reads.Should().Be(0);
                (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
                var original = (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries;
                await model.EnqueueReadinessAsync();
                (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().Equal(original);
                model.Status.Should().Contain("explicitly choose None").And.Contain("Refresh");
            }
            finally { window.Close(); model.Close(); }
            model.QueuePrerequisites.Should().BeEmpty();
        });
    }

    [Theory]
    [InlineData("work-refresh")]
    [InlineData("queue-refresh")]
    [InlineData("task-revision")]
    [InlineData("private-call")]
    [InlineData("owner")]
    [InlineData("close")]
    [InlineData("other-session")]
    [InlineData("session-generation")]
    public async Task StaleNativeChoicesRefuseWithoutFallbackOrRetargetAndKeepOriginalQueue(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var action = new NeverAction();
        await using var queue = Queue(f, access, action);
        var model = Model(f, access, queue);
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        var original = model.QueuePrerequisites.Single(row => row.TaskId == f.Request.TaskId);
        model.SelectQueuePrerequisite(original);
        switch (boundary)
        {
            case "work-refresh": await model.RefreshWorkAsync(); break;
            case "queue-refresh": await model.ReadQueueAsync(); break;
            case "task-revision": await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded); break;
            case "private-call": access.ControlRevision++; break;
            case "owner": access.CanControl = false; break;
            case "close": model.Close(); break;
            case "session-generation":
                await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
                await WindowsSqliteSessionWorkspaceTests.Service(f, access)
                    .ChangeLifecycleAsync(f.Request.SessionId, new(1), false, RequestOrigin.LocalUi, f.Token);
                break;
            case "other-session":
                await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
                var service = WindowsSqliteSessionWorkspaceTests.Service(f, access);
                var other = await service.CreateAsync(new("Same name is not selection"), RequestOrigin.LocalUi, f.Token);
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == other.Authority.SessionId));
                model.SelectQueuePrerequisite(original);
                break;
        }
        await model.EnqueueVersionAsync();
        (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().BeEmpty();
        action.Reads.Should().Be(0);
        original.Revision.Value.Should().Be(1, "the captured metadata is immutable despite a new source observation");
        original.State.Should().Be(HostTaskState.IntentRecorded);
        model.Close();
    }

    public static TheoryData<string, SessionQueueReadProfile, bool> PhysicalGuardCases
    {
        get
        {
            var cases = new TheoryData<string, SessionQueueReadProfile, bool>();
            foreach (var boundary in new[] { "choice", "refresh", "privacy", "owner", "close", "missing", "revision" })
            {
                foreach (var profile in new[] { SessionQueueReadProfile.Version, SessionQueueReadProfile.Capability, SessionQueueReadProfile.RuntimeStatus })
                {
                    cases.Add(boundary, profile, false);
                    cases.Add(boundary, profile, true);
                }
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(PhysicalGuardCases))]
    public async Task RealPhysicalEnqueueCommitRechecksCapturedChoicePrivateSourceAndTaskRevision(string boundary, SessionQueueReadProfile profile, bool queued)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var action = new NeverAction();
        await using var queue = Queue(f, access, action);
        var model = Model(f, access, queue);
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        if (queued) { await model.EnqueueVersionAsync(); }
        var originalQueue = (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries;
        var sourceId = queued ? originalQueue.Single().Request.TaskId : f.Request.TaskId;
        model.SelectQueuePrerequisite(model.QueuePrerequisites.Single(row => row.TaskId == sourceId));
        if (profile == SessionQueueReadProfile.Capability) { model.SelectQueueCapability(model.QueueCapabilities[0]); }
        var audit = f.Count("security_audit_events");
        var crossed = false;
        checkpoint.Commit = (connection, transaction) =>
        {
            crossed = true;
            checkpoint.Commit = null;
            if (boundary is "choice" or "refresh")
            {
                model.SelectQueuePrerequisite(null);
                // A passive/source invalidation must revoke the captured choice even while the command is busy.
                model.RevokeSessionList();
            }
            if (boundary is "privacy") { access.ControlRevision++; }
            if (boundary is "owner") { access.CanControl = false; }
            if (boundary is "close") { model.Close(); }
            if (boundary is "missing" or "revision")
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = boundary is "missing" ? "DELETE FROM host_tasks WHERE task_id=$task;"
                    : "UPDATE host_tasks SET revision=revision+1 WHERE task_id=$task;";
                command.Parameters.AddWithValue("$task", sourceId.Value.ToString("D"));
                command.ExecuteNonQuery();
            }
        };
        await (profile switch
        {
            SessionQueueReadProfile.Capability => model.EnqueueCapabilityAsync(),
            SessionQueueReadProfile.RuntimeStatus => model.EnqueueRuntimeStatusAsync(),
            _ => model.EnqueueVersionAsync(),
        });
        crossed.Should().BeTrue();
        checkpoint.Commit = null;
        (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().Equal(originalQueue);
        f.Count("security_audit_events").Should().Be(audit);
        (await f.Tasks.ReadTaskAsync(sourceId, f.Token))!.Revision.Value.Should().Be(queued ? 1 : 2);
        action.Reads.Should().Be(0);
        model.Close();
    }

    private static SessionQueueService Queue(InteractionStorageFixture f, WindowsSqliteSessionWorkspaceTests.Access access, NeverAction action) =>
        new(f.Store, f.Store, new(f.Tasks), access, action, new(), NullLogger<SessionQueueService>.Instance, f.Time);

    private static SessionsViewModel Model(InteractionStorageFixture f, WindowsSqliteSessionWorkspaceTests.Access access, SessionQueueService queue)
    {
        var service = new SessionWorkspaceService(f.Store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance, queue);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        return new(service, new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), access, f.Time,
            NullLogger<DurableEvidenceQuery>.Instance), access, NullLogger<SessionsViewModel>.Instance);
    }

    private sealed class NeverAction : IDeterministicVersionQueueAction
    {
        internal int Reads { get; private set; }
        public CapabilityReply Observe(SessionQueueReadProfile profile, CancellationToken token, SessionQueueCapabilityTarget? target = null)
        {
            Reads++;
            throw new InvalidOperationException("Native selection and enqueue must never dispatch.");
        }
    }

    // Only read metadata is faulted; every enqueue/admission/receipt below uses the real private SQLite store.
    private sealed class MetadataQueueStore(WindowsSqliteHostInteractionStore store,
        Func<SessionWorkSnapshot, SessionWorkSnapshot> project) : ISessionQueueStore, ISessionWorkStore
    {
        public async ValueTask<SessionWorkSnapshot> ReadWorkAsync(HostId<SessionIdentity> session, long revision,
            SessionQueueLimits limits, CancellationToken token) => project(await store.ReadWorkAsync(session, revision, limits, token));
        public ValueTask<SessionQueueSnapshot> ReadQueueAsync(HostId<SessionIdentity> session, CancellationToken token) => store.ReadQueueAsync(session, token);
        public ValueTask<SessionQueueEntry?> ReadQueueEntryAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task,
            CancellationToken token) => store.ReadQueueEntryAsync(session, task, token);
        public ValueTask<SessionQueueSnapshot> SetDispatchPausedAsync(HostRequest control, HostRevision generation,
            long revision, Guid run, long pause, long admission, bool paused, Func<bool> eligible, CancellationToken token) =>
            store.SetDispatchPausedAsync(control, generation, revision, run, pause, admission, paused, eligible, token);
        public ValueTask HoldDispatchControlAsync(SessionQueueSnapshot expected, CancellationToken token) => store.HoldDispatchControlAsync(expected, token);
        public ValueTask<SessionQueueSnapshot> MovePendingAsync(HostRequest control, SessionQueueMoveReview review,
            Func<bool> eligible, CancellationToken token) => store.MovePendingAsync(control, review, eligible, token);
        public ValueTask<SessionQueueSnapshot> CancelSelectedPendingAsync(HostRequest control, SessionQueueCancelReview review,
            Func<bool> eligible, CancellationToken token) => store.CancelSelectedPendingAsync(control, review, eligible, token);
        public ValueTask<SessionQueueRequeueReview> ReviewExpiredAsync(SessionQueueSnapshot expected, HostId<TaskIdentity> task,
            HostRevision revision, HostRequest fresh, long admission, SessionQueueLimits limits, long configuration,
            Func<bool> eligible, CancellationToken token) =>
            store.ReviewExpiredAsync(expected, task, revision, fresh, admission, limits, configuration, eligible, token);
        public ValueTask<SessionQueueSnapshot> RequeueExpiredAsync(HostRequest control, SessionQueueRequeueReview review,
            SessionQueueLimits limits, long configuration, Func<bool> eligible, CancellationToken token) =>
            store.RequeueExpiredAsync(control, review, limits, configuration, eligible, token);
        public ValueTask<SessionQueueSnapshot> EnqueueAsync(HostRequest control, HostRequest work, HostRevision generation,
            long revision, long admission, HostId<TaskIdentity>? dependency, SessionQueueLimits limits,
            Func<bool> eligible, CancellationToken token, SessionQueueReadProfile profile = SessionQueueReadProfile.Version,
            HostRevision? expectedDependencyRevision = null, SessionQueueCapabilityTarget? target = null,
            SessionQueueDispatchControl? expectedControl = null) =>
            store.EnqueueAsync(control, work, generation, revision, admission, dependency, limits, eligible, token,
                profile, expectedDependencyRevision, target, expectedControl);
        public ValueTask<SessionQueueSnapshot> RemovePendingAsync(HostRequest control, HostRevision generation, long revision,
            HostId<TaskIdentity>? task, HostRevision? entryRevision, SessionQueueState outcome, Func<bool> eligible, CancellationToken token) =>
            store.RemovePendingAsync(control, generation, revision, task, entryRevision, outcome, eligible, token);
        public ValueTask<SessionQueueEntry?> FindReadyAsync(long revision, SessionQueueLimits limits, CancellationToken token) =>
            store.FindReadyAsync(revision, limits, token);
        public ValueTask<SessionQueueAdmissionReceipt> AdmitAsync(SessionQueueEntry expected, long revision,
            SessionQueueLimits limits, Func<bool> eligible, CancellationToken token) => store.AdmitAsync(expected, revision, limits, eligible, token);
        public ValueTask<SessionQueueEntry> CompleteAsync(SessionQueueEntry expected, SessionQueueState outcome,
            Func<bool> eligible, CancellationToken token, CapabilityReply? result = null) => store.CompleteAsync(expected, outcome, eligible, token, result);
    }
}
