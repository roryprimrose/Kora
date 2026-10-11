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
        public CapabilityReply Observe(CancellationToken token)
        {
            Reads++;
            BeforeObserve?.Invoke();
            return new(CapabilityOutcome.Succeeded, "observed", Version: new("1.0.0", "Fixed local read."));
        }
    }
}
