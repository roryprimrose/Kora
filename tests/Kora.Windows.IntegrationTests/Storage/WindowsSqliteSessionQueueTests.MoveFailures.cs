using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionQueueTests
{
    [Theory]
    [InlineData("audit")]
    [InlineData("cancel")]
    [InlineData("privacy")]
    [InlineData("deadline")]
    public async Task FailedAdjacentCommitRetainsExplicitHoldWithoutOrderPublicationActivityRenewalOrRetry(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        var first = await EnqueueAsync(f, f.Request.SessionId);
        var second = await EnqueueAsync(f, f.Request.SessionId);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using var queue = PauseQueue(f, access, new NeverAction());
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var review = (await queue.ExecuteCommandAsync(MoveCommand(snapshot, second, first, true),
            RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
        var original = QueueBytes(f);
        var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
        var auditCount = f.Count("security_audit_events");
        using var cancel = new CancellationTokenSource();
        checkpoint.Audit = (_, _) =>
        {
            if (boundary is "audit") { throw new IOException("Required order audit unavailable."); }
            if (boundary is "cancel") { cancel.Cancel(); }
            if (boundary is "privacy") { access.ControlRevision++; }
            if (boundary is "deadline") { f.Time.Now = first.ExpiresAt; }
        };
        f.Time.Now = f.Time.Now.AddMinutes(1);
        var confirm = () => queue.ExecuteCommandAsync(ConfirmMove(review), RequestOrigin.LocalUi, () => true, cancel.Token);
        await confirm.Should().ThrowAsync<Exception>();
        checkpoint.Audit = null;
        var held = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        held.DispatchControl!.Held.Should().BeTrue();
        held.Orders.Should().BeEmpty();
        held.Entries.Should().Equal(first, second);
        QueueBytes(f).Should().Equal(original);
        f.Count("security_audit_events").Should().Be(auditCount);
        (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
        (await f.Store.FindReadyAsync(access.ControlRevision, new(), f.Token)).Should().BeNull();
        var retry = () => queue.ExecuteCommandAsync(ConfirmMove(review), RequestOrigin.LocalUi, () => true, f.Token);
        await retry.Should().ThrowAsync<InvalidOperationException>();
    }

    [WindowsFact]
    public async Task LostPostCommitDeliveryHoldsExactCommittedOrderWithoutClaimingRollback()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        var first = await EnqueueAsync(f, f.Request.SessionId);
        var second = await EnqueueAsync(f, f.Request.SessionId);
        await using var queue = PauseQueue(f, new(), new NeverAction());
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var review = (await queue.ExecuteCommandAsync(MoveCommand(snapshot, second, first, true),
            RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
        var committing = false;
        var consumingChecks = 0;
        checkpoint.Commit = (_, _) => committing = true;
        bool Admission() => !committing || ++consumingChecks == 1;
        var confirm = () => queue.ExecuteCommandAsync(ConfirmMove(review), RequestOrigin.LocalUi, Admission, f.Token);
        await confirm.Should().ThrowAsync<InvalidOperationException>();
        checkpoint.Commit = null;
        var committed = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        committed.Entries.Should().Equal(second, first);
        committed.DispatchControl!.Held.Should().BeTrue();
        committed.Orders.Single().Tasks.Should().Equal(second.Request.TaskId, first.Request.TaskId);
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().BeNull();
    }

    [WindowsFact]
    public async Task PhysicalOrderCommitFenceSerializesConcurrentAdmissionAndRejectsTheOldHeadWithoutWaitingForCallbacks()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        var first = await EnqueueAsync(f, f.Request.SessionId);
        var second = await EnqueueAsync(f, f.Request.SessionId);
        await using var queue = PauseQueue(f, new(), new NeverAction());
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var review = (await queue.ExecuteCommandAsync(MoveCommand(snapshot, second, first, true),
            RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        checkpoint.Commit = (_, _) =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10), f.Token)) { throw new TimeoutException("Order commit not released."); }
        };
        var move = queue.ExecuteCommandAsync(ConfirmMove(review), RequestOrigin.LocalUi, () => true, f.Token);
        entered.Wait(TimeSpan.FromSeconds(10), f.Token).Should().BeTrue();
        var admission = AdmitAsync(f, first);
        try
        {
            admission.IsCompleted.Should().BeFalse();
            release.Set();
            (await move).Outcome.Should().Be("committed");
            var oldHead = () => admission;
            await oldHead.Should().ThrowAsync<InvalidOperationException>();
            (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().Be(second);
        }
        finally { release.Set(); checkpoint.Commit = null; await move; }
    }

    [WindowsFact]
    public async Task CurrentRunSwapPreservesExactAbsentTwoThreeFourPayloadsAndHistoricalDigests()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var completed = await CompleteAsync(f, await AdmitAsync(f, await EnqueueAsync(f, f.Request.SessionId)));
        completed.RecordVersion.Should().Be(3);
        var legacy = await EnqueueAsync(f, f.Request.SessionId);
        var oldBytes = HostInteractionCodec.Encode(new LegacyQueuePayload(legacy.Request, legacy.Generation, legacy.Revision,
            legacy.Position, legacy.State, legacy.RunId, legacy.AdmissionRevision, legacy.EnqueuedAt, legacy.ExpiresAt, legacy.Dependency, legacy.DispatchOrder));
        StageKnownLegacyQueue(f, legacy, oldBytes);
        legacy = (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, legacy.Request.TaskId, f.Token))!;
        legacy.RecordVersion.Should().BeNull();
        var captured = await EnqueueAsync(f, f.Request.SessionId);
        captured.RecordVersion.Should().Be(2);
        var profile = await EnqueueAsync(f, f.Request.SessionId, profile: SessionQueueReadProfile.Capabilities);
        profile.RecordVersion.Should().Be(4);
        var original = QueueBytes(f);
        var history = await f.Store.ReadHistoryAsync(f.Request.SessionId, null, 50, f.Token);
        await using var queue = PauseQueue(f, new(), new NeverAction());
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var review = (await queue.ExecuteCommandAsync(MoveCommand(snapshot, profile, captured, true),
            RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
        await queue.ExecuteCommandAsync(ConfirmMove(review), RequestOrigin.LocalUi, () => true, f.Token);
        QueueBytes(f).Should().Equal(original);
        var later = await f.Store.ReadHistoryAsync(f.Request.SessionId, null, 50, f.Token);
        later.Records.Take(history.Records.Length).Should().Equal(history.Records);
        RequireQueueDigest(f, legacy, oldBytes);
        RequireQueueDigest(f, captured, QueuePayload(f, captured));
        RequireQueueDigest(f, completed, QueuePayload(f, completed));
        RequireQueueDigest(f, profile, QueuePayload(f, profile));
    }

    [WindowsFact]
    public async Task SchemaNineMigrationCreatesNoFabricatedOrderOrIntentAndKeepsAllQueueAndHistoryBytes()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        _ = await EnqueueAsync(f, f.Request.SessionId);
        _ = await EnqueueAsync(f, f.Request.SessionId, profile: SessionQueueReadProfile.Runtimes);
        var original = QueueBytes(f);
        var audits = f.Count("security_audit_events");
        var tasks = f.Count("host_task_events");
        var history = f.Count("session_history");
        f.Mutate("DROP TABLE session_queue_order; PRAGMA user_version=9;");
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        QueueBytes(f).Should().Equal(original);
        f.Count("security_audit_events").Should().Be(audits);
        f.Count("host_task_events").Should().Be(tasks);
        f.Count("session_history").Should().Be(history);
        f.Count("session_queue_order").Should().Be(0);
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().BeNull();
    }

    [Theory]
    [InlineData("DELETE FROM session_queue_order;")]
    [InlineData("UPDATE session_queue_order SET payload=json_set(payload,'$.Version',99);")]
    [InlineData("UPDATE session_queue_order SET payload=json_set(payload,'$.Tasks[0]',json_extract(payload,'$.Tasks[1]'));")]
    [InlineData("UPDATE session_queue_order SET run_id='11111111-1111-1111-1111-111111111111';")]
    [InlineData("DROP TABLE session_queue_order; PRAGMA user_version=9;")]
    public async Task LostCorruptDuplicateOrDowngradedCommittedOrderFailsClosedInsteadOfDefaultFifo(string mutation)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var first = await EnqueueAsync(f, f.Request.SessionId);
        var second = await EnqueueAsync(f, f.Request.SessionId);
        await using var queue = PauseQueue(f, new(), new NeverAction());
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var review = (await queue.ExecuteCommandAsync(MoveCommand(snapshot, second, first, true),
            RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
        await queue.ExecuteCommandAsync(ConfirmMove(review), RequestOrigin.LocalUi, () => true, f.Token);
        f.Mutate(mutation);
        f.Reopen();
        var initialize = () => f.Store.InitializeAsync(f.Token).AsTask();
        await initialize.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task OldEmptyOrderLineageNeverFabricatesIntentOrBlocksDefaultFifoInANewSessionGeneration()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var first = await EnqueueAsync(f, f.Request.SessionId);
        var second = await EnqueueAsync(f, f.Request.SessionId);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using var queue = PauseQueue(f, access, new NeverAction());
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var review = (await queue.ExecuteCommandAsync(MoveCommand(snapshot, second, first, true),
            RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
        var moved = (await queue.ExecuteCommandAsync(ConfirmMove(review), RequestOrigin.LocalUi, () => true, f.Token)).Queue!;
        await queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueClear, f.Request.SessionId.Value, 1)
            { QueueRevision = moved.Revision }, RequestOrigin.LocalUi, () => true, f.Token);
        var old = (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Orders.Single();
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, access);
        await service.ChangeLifecycleAsync(f.Request.SessionId, new(1), false, RequestOrigin.LocalUi, f.Token);
        await service.ChangeLifecycleAsync(f.Request.SessionId, new(2), true, RequestOrigin.LocalUi, f.Token);
        for (var index = 0; index < 2; index++)
        {
            var current = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
            await queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueEnqueue, f.Request.SessionId.Value, 3)
            {
                QueueRevision = current.Revision, WorkRequestId = Guid.NewGuid(), TaskId = Guid.NewGuid(),
            }, RequestOrigin.LocalUi, () => true, f.Token);
        }
        var newer = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        newer.Orders.Should().ContainSingle();
        newer.Orders[0].Should().BeEquivalentTo(old);
        newer.Entries.Should().BeInAscendingOrder(entry => entry.Position);
        var newReview = (await queue.ExecuteCommandAsync(MoveCommand(newer, newer.Entries[1], newer.Entries[0], true),
            RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
        var reordered = (await queue.ExecuteCommandAsync(ConfirmMove(newReview), RequestOrigin.LocalUi, () => true, f.Token)).Queue!;
        reordered.Orders.Should().HaveCount(2);
        reordered.Orders.Single(order => order.Generation.Value == 1).Should().BeEquivalentTo(old);
        reordered.Orders.Single(order => order.Generation.Value == 3).Tasks.Should().Equal(newer.Entries.Reverse().Select(entry => entry.Request.TaskId));
    }

    [WindowsFact]
    public async Task ExplicitClearRetainsOrderReceiptThenInventoriedRetentionDeletionRemovesOrderRowsWithoutRenewal()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var first = await EnqueueAsync(f, f.Request.SessionId);
        var second = await EnqueueAsync(f, f.Request.SessionId);
        await using var queue = PauseQueue(f, new(), new NeverAction());
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var review = (await queue.ExecuteCommandAsync(MoveCommand(snapshot, second, first, true),
            RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
        var moved = (await queue.ExecuteCommandAsync(ConfirmMove(review), RequestOrigin.LocalUi, () => true, f.Token)).Queue!;
        var clock = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
        await queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueClear, f.Request.SessionId.Value, 1)
            { QueueRevision = moved.Revision }, RequestOrigin.LocalUi, () => true, f.Token);
        var cleared = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        cleared.Orders.Single().Tasks.Should().BeEmpty();
        (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(clock);
        f.Time.Now = clock.DeleteDue;
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Retention);
        var deleted = await f.Store.ApplyRetentionAsync(() => true, (_, _) => ValueTask.CompletedTask, f.Token);
        deleted.Deleted.Should().Be(1);
        f.Count("session_queue_order").Should().Be(0);
        f.Count("session_queue").Should().Be(0);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Purged.Should().BeTrue();
    }
}
