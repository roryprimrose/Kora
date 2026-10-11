using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Hosting;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class SessionsViewModelQueueTests
{
    [WindowsFact]
    public async Task NativeAdjacentReviewCancelConfirmAndStaleSelectionShareExactAuditedQueueWithoutChangingDraftOrDispatch()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var action = new VersionAction();
        await using var queue = new SessionQueueService(f.Store, f.Store, new(f.Tasks), access, action,
            new(), NullLogger<SessionQueueService>.Instance, f.Time);
        var service = new SessionWorkspaceService(f.Store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance, queue);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var viewer = new SessionsViewModel(service, new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink),
            access, f.Time, NullLogger<DurableEvidenceQuery>.Instance), access, NullLogger<SessionsViewModel>.Instance);
        try
        {
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single());
            await viewer.EnqueueVersionAsync();
            await viewer.EnqueueCapabilitiesAsync();
            var original = viewer.QueueRecords.ToArray();
            viewer.NameDraft = "unchanged draft";
            viewer.SelectWorkRecord(viewer.WorkRecords.Single(row => row.TaskId == original[1].Request.TaskId.Value));
            viewer.CanMoveQueueUp.Should().BeTrue();
            viewer.CanMoveQueueDown.Should().BeFalse();
            viewer.QueueMoveStatus.Should().Contain("Last pending");
            await viewer.ReviewQueueMoveUpAsync();
            viewer.CanConfirmQueueMove.Should().BeTrue();
            viewer.QueueMoveStatus.Should().Contain("Current:").And.Contain("Proposed:").And.Contain("capabilities.list")
                .And.Contain(original[0].Request.TaskId.Value.ToString("D")).And.Contain(original[0].ExpiresAt.ToString("O"));
            viewer.QueueRecords.Should().Equal(original);
            await viewer.CancelQueueMoveAsync();
            viewer.CanConfirmQueueMove.Should().BeFalse();
            viewer.QueueRecords.Should().Equal(original);
            viewer.NameDraft.Should().Be("unchanged draft");
            await viewer.ReviewQueueMoveUpAsync();
            await viewer.ConfirmQueueMoveAsync();
            viewer.QueueRecords.Should().Equal(original.Reverse());
            viewer.WorkRecords.Where(row => row.Queue?.Entry.IsPending == true).Select(row => row.TaskId)
                .Should().Equal(original.Reverse().Select(entry => entry.Request.TaskId.Value));
            viewer.WorkRecords[0].Revisions.Should().Contain("original arrival").And.Contain("pending dispatch order");
            action.Reads.Should().Be(0);
            viewer.NameDraft.Should().Be("unchanged draft");
            viewer.SelectWorkRecord(viewer.WorkRecords.Single(row => row.TaskId == original[0].Request.TaskId.Value));
            await viewer.ReviewQueueMoveUpAsync();
            viewer.SelectWorkRecord(viewer.WorkRecords.Single(row => row.TaskId == original[1].Request.TaskId.Value));
            viewer.CanConfirmQueueMove.Should().BeFalse();
            await viewer.ConfirmQueueMoveAsync();
            (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().Equal(original.Reverse());
            await viewer.RefreshWorkAsync();
            viewer.SelectWorkRecord(viewer.WorkRecords.Single(row => row.TaskId == original[1].Request.TaskId.Value));
            viewer.CanMoveQueueUp.Should().BeFalse();
            viewer.QueueMoveStatus.Should().Contain("stale");
        }
        finally { viewer.Close(); }
        viewer.QueueMoveStatus.Should().NotContain("task ");
    }

    [WindowsFact]
    public async Task Native_fixed_profile_choices_enqueue_shared_actions_and_present_private_exact_receipts()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        using var actions = new WindowsSqliteSessionQueueTests.FixedRegistryFixture(f.Time);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using var queue = new SessionQueueService(f.Store, f.Store, new(f.Tasks), access,
            new DeterministicVersionQueueAction(actions.Registry), new(), NullLogger<SessionQueueService>.Instance, f.Time);
        var service = new SessionWorkspaceService(f.Store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance, queue);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var viewer = new SessionsViewModel(service, new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink),
            access, f.Time, NullLogger<DurableEvidenceQuery>.Instance), access, NullLogger<SessionsViewModel>.Instance);
        try
        {
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single());
            await viewer.EnqueueVersionAsync();
            await viewer.EnqueueCapabilitiesAsync();
            await viewer.EnqueueReadinessAsync();
            await viewer.EnqueueRuntimesAsync();
            viewer.QueueRecords.Select(entry => entry.ReadProfile).Should().Equal(Enum.GetValues<SessionQueueReadProfile>());
            viewer.WorkRecords.Should().Contain(row => row.State.Contains(ReadOnlyCapabilityCatalog.RuntimeList, StringComparison.Ordinal));
            actions.Probe.Calls.Should().Be(0);
            await viewer.DispatchQueueAsync();
            viewer.QueueRecords.Should().BeEmpty();
            viewer.Detail.Should().Contain("4.5.6").And.Contain("recorded-observations").And.Contain("recorded-observation");
            var row = viewer.WorkRecords.Single(item => item.Queue?.Entry.ReadProfile == SessionQueueReadProfile.Readiness);
            row.Queue!.Entry.Result.Should().BeNull();
            viewer.SelectWorkRecord(row);
            await viewer.InspectWorkAsync();
            viewer.Detail.Should().Contain("recorded-observations").And.Contain("No completed observation is recorded.");
            actions.Probe.Calls.Should().Be(0);
        }
        finally { viewer.Close(); }
        viewer.Detail.Should().BeEmpty();
    }

    [WindowsFact]
    public async Task NativePauseCanAcknowledgeDuringHeldBatchAndResumeNeverDispatchesOrAltersPendingWork()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var action = new VersionAction();
        await using var queue = new SessionQueueService(fixture.Store, fixture.Store, new(fixture.Tasks), access, action,
            new(), NullLogger<SessionQueueService>.Instance, fixture.Time);
        var service = new SessionWorkspaceService(fixture.Store, new(fixture.Tasks), access,
            NullLogger<SessionWorkspaceService>.Instance, queue);
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
        sink.Initialize();
        var viewer = new SessionsViewModel(service, new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink),
            access, fixture.Time, NullLogger<DurableEvidenceQuery>.Instance), access, NullLogger<SessionsViewModel>.Instance);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single());
        await viewer.EnqueueVersionAsync();
        await viewer.EnqueueVersionAsync();
        var pending = viewer.QueueRecords[^1];
        action.BeforeObserve = () =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10), fixture.Token)) { throw new TimeoutException("Held native read was not released."); }
        };
        var dispatch = viewer.DispatchQueueAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), fixture.Token);
        try
        {
            viewer.CanChangeQueuePause.Should().BeTrue();
            viewer.CanRefreshWork.Should().BeTrue();
            await viewer.RefreshWorkAsync();
            await viewer.PauseQueueAsync();
            viewer.WorkStatus.Should().Contain("paused True").And.Contain("pause blocks new admissions");
            dispatch.IsCompleted.Should().BeFalse();
            release.Set();
            await dispatch;
            action.Reads.Should().Be(1);
            viewer.QueueRecords.Should().ContainSingle().Which.Should().Be(pending);
            await viewer.ResumeQueueAsync();
            viewer.WorkStatus.Should().Contain("paused False");
            action.Reads.Should().Be(1);
            viewer.QueueRecords.Should().ContainSingle().Which.Should().Be(pending);
            await viewer.DispatchQueueAsync();
            action.Reads.Should().Be(2);
        }
        finally { release.Set(); await dispatch; viewer.Close(); }
        viewer.CanChangeQueuePause.Should().BeFalse();
    }

    [WindowsFact]
    public async Task Native_queue_uses_the_same_exact_revisions_workflow_without_selection_dispatch_or_late_privacy_resume()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var action = new VersionAction();
        await using var queue = new SessionQueueService(fixture.Store, fixture.Store, new(fixture.Tasks), access, action,
            new(), NullLogger<SessionQueueService>.Instance, fixture.Time);
        var service = new SessionWorkspaceService(fixture.Store, new(fixture.Tasks), access,
            NullLogger<SessionWorkspaceService>.Instance, queue);
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
        sink.Initialize();
        var viewer = new SessionsViewModel(service, new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink),
            access, fixture.Time, NullLogger<DurableEvidenceQuery>.Instance), access, NullLogger<SessionsViewModel>.Instance);
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single());
        action.Reads.Should().Be(0);
        viewer.CanControlQueue.Should().BeTrue("selection now loads the passive authoritative work snapshot, never dispatching");
        await viewer.ReadQueueAsync();
        viewer.CanControlQueue.Should().BeTrue();
        await viewer.EnqueueVersionAsync();
        var first = viewer.QueueRecords.Single();
        action.Reads.Should().Be(0);
        viewer.SelectQueueEntry(first);
        viewer.CanCancelQueueEntry.Should().BeTrue();
        await viewer.CancelQueueEntryAsync();
        viewer.QueueRecords.Should().BeEmpty();
        (await fixture.Store.ReadQueueEntryAsync(first.Request.SessionId, first.Request.TaskId, fixture.Token))!
            .State.Should().Be(SessionQueueState.Cancelled);
        await viewer.EnqueueVersionAsync();
        await viewer.ClearQueueAsync();
        viewer.QueueRecords.Should().BeEmpty();
        await viewer.EnqueueVersionAsync();
        await viewer.DispatchQueueAsync();
        action.Reads.Should().Be(1);
        viewer.QueueRecords.Should().BeEmpty();
        viewer.Detail.Should().Contain("1.0.0");
        await viewer.EnqueueVersionAsync();
        access.ControlRevision++;
        await viewer.DispatchQueueAsync();
        action.Reads.Should().Be(1, "renewed private admission must not resume eligibility captured before its epoch changed");
        viewer.QueueRecords.Should().ContainSingle();
        viewer.Close();
        viewer.QueueRecords.Should().BeEmpty();
        viewer.CanControlQueue.Should().BeFalse();
    }

    private sealed class VersionAction : IDeterministicVersionQueueAction
    {
        internal Action? BeforeObserve { get; set; }
        internal int Reads { get; private set; }
        public CapabilityReply Observe(SessionQueueReadProfile profile, CancellationToken token)
        {
            Reads++;
            BeforeObserve?.Invoke();
            return new(CapabilityOutcome.Succeeded, "observed", Version: new("1.0.0", "Fixed local read."));
        }
    }
}
