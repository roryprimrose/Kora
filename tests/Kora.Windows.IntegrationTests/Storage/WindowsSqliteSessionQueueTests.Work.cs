using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionQueueTests
{
    [WindowsFact]
    public async Task Work_snapshot_is_atomic_passive_ordered_and_preserves_cancelled_receipts_and_history_parity()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var first = await EnqueueAsync(fixture, fixture.Request.SessionId);
        var second = await EnqueueAsync(fixture, fixture.Request.SessionId);
        var audits = fixture.Count("security_audit_events");
        var snapshot = await fixture.Store.ReadWorkAsync(first.Request.SessionId, 1, new(), fixture.Token);
        fixture.Count("security_audit_events").Should().Be(audits);
        snapshot.QueueRecords.Select(row => row.Entry.Position).Should().BeInAscendingOrder();
        snapshot.PendingCount.Should().Be(2);
        snapshot.PendingCapacity.Should().Be(10);
        snapshot.QueueRecords[0].Eligibility.Should().Be(SessionQueueEligibility.Ready);
        snapshot.QueueRecords[1].Eligibility.Should().Be(SessionQueueEligibility.EarlierPendingEntry);
        snapshot.Tasks.Should().NotContain(task => task.Task.Request.TaskId == first.Request.TaskId);
        var running = await AdmitAsync(fixture, first);
        snapshot = await fixture.Store.ReadWorkAsync(first.Request.SessionId, 1, new(), fixture.Token);
        snapshot.QueueRecords[0].ActiveDeadline.Should().Be(fixture.Time.Now.Add(SessionQueuePolicy.ActiveDeadline));
        snapshot.QueueRecords[1].Eligibility.Should().Be(SessionQueueEligibility.SessionCurrent);
        await CompleteAsync(fixture, running);
        await RemoveAsync(fixture, second, (await fixture.Store.ReadQueueAsync(second.Request.SessionId, fixture.Token)).Revision);
        snapshot = await fixture.Store.ReadWorkAsync(first.Request.SessionId, 1, new(), fixture.Token);
        snapshot.Queue.Entries.Should().BeEmpty();
        snapshot.QueueRecords.Select(row => row.Entry.State).Should().Equal(SessionQueueState.Succeeded, SessionQueueState.Cancelled);
        var history = await fixture.Store.ReadHistoryAsync(first.Request.SessionId, null, 50, fixture.Token);
        history.Records.Should().Contain(row => row.TaskId == second.Request.TaskId.Value && row.TaskState == HostTaskState.Cancelled);
        var clockChanged = snapshot with { ObservedAt = snapshot.ObservedAt.AddDays(1) };
        clockChanged.Queue.Should().BeSameAs(snapshot.Queue);
    }

    [WindowsFact]
    public async Task Work_wait_questions_expiry_unknown_restart_and_disposition_are_never_empty_success_or_replay()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var question = await WindowsSqliteTaskControlTests.WaitAsync(fixture);
        var waiting = await fixture.Store.ReadWorkAsync(fixture.Request.SessionId, 1, new(), fixture.Token);
        waiting.PendingQuestions.Should().ContainSingle().Which.Key.Should().Be(question.Key);
        waiting.Tasks.Should().ContainSingle().Which.CurrentSource.Should().BeTrue();
        var queued = await EnqueueAsync(fixture, fixture.Request.SessionId);
        var blocked = await fixture.Store.ReadWorkAsync(fixture.Request.SessionId, 1, new(), fixture.Token);
        blocked.QueueRecords.Single().Eligibility.Should().Be(SessionQueueEligibility.UnclassifiedWorkOrWait);
        fixture.Time.Now = fixture.Time.Now.Add(SessionQueuePolicy.PendingLifetime);
        var expired = await fixture.Store.ReadWorkAsync(fixture.Request.SessionId, 1, new(), fixture.Token);
        expired.QueueRecords.Single().Eligibility.Should().Be(SessionQueueEligibility.Expired);
        expired.PendingCount.Should().Be(1);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        var restarted = await fixture.Store.ReadWorkAsync(fixture.Request.SessionId, 1, new(), fixture.Token);
        restarted.QueueRecords.Single().Eligibility.Should().Be(SessionQueueEligibility.InterruptedNoReplay);
        restarted.Queue.Entries.Should().BeEmpty();
        (await fixture.Store.ReadQueueEntryAsync(fixture.Request.SessionId, queued.Request.TaskId, fixture.Token))!
            .State.Should().Be(SessionQueueState.Interrupted);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelledRead = () => fixture.Store.ReadWorkAsync(fixture.Request.SessionId, 1, new(), cancellation.Token).AsTask();
        await cancelledRead.Should().ThrowAsync<OperationCanceledException>();
    }

    [WindowsFact]
    public async Task Work_observation_and_exact_queue_commands_share_limits_reasons_without_audit_or_content_egress()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var entry = await EnqueueAsync(fixture, fixture.Request.SessionId);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using var queue = new SessionQueueService(fixture.Store, fixture.Store, new(fixture.Tasks), access,
            new NeverAction(), new(3, 2), NullLogger<SessionQueueService>.Instance, fixture.Time);
        var service = new SessionWorkspaceService(fixture.Store, new(fixture.Tasks), access,
            NullLogger<SessionWorkspaceService>.Instance, queue);
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var audits = fixture.Count("security_audit_events");
        var native = await service.ReadWorkAsync(entry.Request.SessionId, fixture.Token);
        var command = await service.ExecuteCommandAsync(new(SessionCommandOperation.QueueList, entry.Request.SessionId.Value),
            RequestOrigin.ActivatedVoice, () => true, fixture.Token);
        command.Work.Should().BeEquivalentTo(native);
        native.PendingCapacity.Should().Be(3);
        native.ExecutionSlots.Should().Be(2);
        fixture.Count("security_audit_events").Should().Be(audits);
        root.Activity!.Tags.Should().NotContain(tag => tag.Value != null && tag.Value.Contains("Same label", StringComparison.Ordinal));
        var admitted = await AdmitAsync(fixture, entry);
        await CompleteAsync(fixture, admitted, SessionQueueState.Unknown);
        await EnqueueAsync(fixture, fixture.Request.SessionId);
        var unknown = await service.ReadWorkAsync(entry.Request.SessionId, fixture.Token);
        unknown.QueueRecords.Should().OnlyContain(row => row.Eligibility == SessionQueueEligibility.UnknownQuarantine);
    }

    private sealed class NeverAction : IDeterministicVersionQueueAction
    {
        public Kora.Core.Tools.CapabilityReply Observe(CancellationToken token) =>
            throw new InvalidOperationException("Passive work inspection must not execute.");
    }

    [WindowsFact]
    public async Task Work_snapshot_bounds_recent_receipts_and_reports_omitted_nonqueue_tasks_without_losing_exact_history()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        SessionQueueEntry? first = null;
        for (var index = 0; index < SessionWorkSnapshot.RecentQueueRecords + 2; index++)
        {
            var entry = await EnqueueAsync(fixture, fixture.Request.SessionId);
            first ??= entry;
            await RemoveAsync(fixture, entry, (await fixture.Store.ReadQueueAsync(entry.Request.SessionId, fixture.Token)).Revision);
        }
        var snapshot = await fixture.Store.ReadWorkAsync(fixture.Request.SessionId, 1, new(), fixture.Token);
        snapshot.QueueRecords.Should().HaveCount(SessionWorkSnapshot.RecentQueueRecords);
        snapshot.OmittedQueueRecords.Should().Be(2);
        snapshot.Tasks.Should().HaveCount(SessionWorkSnapshot.MaximumRecords);
        snapshot.OmittedTasks.Should().BeGreaterThan(0);
        snapshot.Queue.Entries.Should().BeEmpty();
        snapshot.QueueRecords.Should().OnlyContain(row => row.Entry.State == SessionQueueState.Cancelled);
        (await fixture.Store.ReadQueueEntryAsync(first!.Request.SessionId, first.Request.TaskId, fixture.Token))!
            .State.Should().Be(SessionQueueState.Cancelled);
    }

    [WindowsFact]
    public async Task Concurrent_work_refresh_and_queue_cancellation_never_present_a_mixed_capacity_revision_or_state()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var entry = await EnqueueAsync(fixture, fixture.Request.SessionId);
        var queue = await fixture.Store.ReadQueueAsync(entry.Request.SessionId, fixture.Token);
        var reads = Enumerable.Range(0, 8).Select(_ =>
            fixture.Store.ReadWorkAsync(entry.Request.SessionId, 1, new(), fixture.Token).AsTask()).ToArray();
        await RemoveAsync(fixture, entry, queue.Revision);
        foreach (var snapshot in await Task.WhenAll(reads))
        {
            var row = snapshot.QueueRecords.Single();
            if (row.Entry.State == SessionQueueState.Pending)
            {
                snapshot.PendingCount.Should().Be(1);
                snapshot.Queue.Revision.Should().Be(queue.Revision);
                snapshot.Queue.Entries.Should().ContainSingle().Which.Revision.Should().Be(row.Entry.Revision);
            }
            else
            {
                row.Entry.State.Should().Be(SessionQueueState.Cancelled);
                snapshot.PendingCount.Should().Be(0);
                snapshot.Queue.Entries.Should().BeEmpty();
                snapshot.Queue.Revision.Should().BeGreaterThan(queue.Revision);
            }
        }
    }
}
