using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionQueueTests
{
    [Theory]
    [InlineData(SessionQueueReadProfile.Version)]
    [InlineData(SessionQueueReadProfile.Capabilities)]
    [InlineData(SessionQueueReadProfile.Readiness)]
    [InlineData(SessionQueueReadProfile.Runtimes)]
    public async Task ExpiredCurrentRunReplacementIsOneReviewedNewTailDecisionWithUnchangedOldEvidenceAndNoDispatch(SessionQueueReadProfile profile)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var old = await EnqueueAsync(f, f.Request.SessionId, minutes: 1, profile: profile);
        var prerequisite = await EnqueueAsync(f, f.Request.SessionId);
        var dependent = await EnqueueAsync(f, f.Request.SessionId, dependency: old.Request.TaskId);
        using var registry = new FixedRegistryFixture(f.Time);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using var queue = new SessionQueueService(f.Store, f.Store, new(f.Tasks), access,
            new DeterministicVersionQueueAction(registry.Registry), new(pendingPerSession: 3, pendingLifetimeMinutes: 2),
            NullLogger<SessionQueueService>.Instance, f.Time);
        var bytes = QueueBytes(f);
        f.Time.Now = old.ExpiresAt.AddTicks(-1);
        var before = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var early = () => queue.ExecuteCommandAsync(ReviewRequeue(before, old), RequestOrigin.LocalUi, () => true, f.Token);
        await early.Should().ThrowAsync<InvalidOperationException>();
        f.Time.Now = old.ExpiresAt;
        before = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var paused = (await queue.ExecuteCommandAsync(PauseCommand(before, true), RequestOrigin.LocalUi, () => true, f.Token)).Queue!;
        var reviewed = await queue.ExecuteCommandAsync(ReviewRequeue(paused, old), RequestOrigin.LocalUi, () => true, f.Token);
        var review = reviewed.QueueRequeueReview!;
        review.Original.Should().Be(old, "the review carries stored Pending, not a fabricated Expired terminal row");
        review.EnqueuedAt.Should().Be(old.ExpiresAt);
        review.ExpiresAt.Should().Be(old.ExpiresAt.AddMinutes(2));
        QueueBytes(f).Should().Equal(bytes, "preview cannot mutate evidence, deadlines or activity");
        var result = await queue.ExecuteCommandAsync(ConfirmRequeue(review), RequestOrigin.LocalUi, () => true, f.Token);
        var fresh = result.QueueRequeueReceipt!.Fresh;
        fresh.Request.Should().Be(review.FreshRequest);
        fresh.Request.TaskId.Should().NotBe(old.Request.TaskId);
        fresh.Request.RequestId.Should().NotBe(old.Request.RequestId);
        fresh.ReadProfile.Should().Be(profile);
        fresh.State.Should().Be(SessionQueueState.Pending);
        fresh.ActiveBudgetMinutes.Should().BeNull();
        fresh.ActiveDeadlineAt.Should().BeNull();
        fresh.Result.Should().BeNull();
        result.Queue!.Entries.Should().Equal(prerequisite, dependent, fresh);
        result.Queue.Orders.Single().Tasks.Should().Equal(prerequisite.Request.TaskId, dependent.Request.TaskId, fresh.Request.TaskId);
        result.Queue.DispatchControl!.Paused.Should().BeTrue();
        result.Queue.DispatchControl.Held.Should().BeFalse();
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().BeNull();
        var removed = (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, old.Request.TaskId, f.Token))!;
        removed.Should().Be(old with { State = SessionQueueState.Removed, Revision = new(2) });
        var oldTask = (await f.Store.ReadTaskAsync(f.Request.SessionId, old.Request.TaskId, f.Token))!;
        oldTask.Task.State.Should().Be(HostTaskState.Cancelled);
        var work = await f.Store.ReadWorkAsync(f.Request.SessionId, 1, new(), f.Token);
        work.QueueRecords.Single(row => row.Entry.Request.TaskId == dependent.Request.TaskId).Entry.Dependency.Should().Be(old.Request.TaskId);
        work.QueueRecords.Single(row => row.Entry.Request.TaskId == fresh.Request.TaskId).Entry.ExpiresAt.Should().Be(review.ExpiresAt);
        AuthorityLocalEventSource.FromWork(work, f.Time.Now).Should().Contain(item => item.TaskId == fresh.Request.TaskId);
        registry.Probe.Calls.Should().Be(0);
        var replay = () => queue.ExecuteCommandAsync(ConfirmRequeue(review), RequestOrigin.LocalUi, () => true, f.Token);
        await replay.Should().ThrowAsync<InvalidOperationException>();
        using var raw = f.OpenRaw();
        using var audit = raw.CreateCommand();
        audit.CommandText = """
            SELECT COUNT(*) FROM security_audit_events WHERE json_extract(envelope,'$.Audit.ActionId')='queue.requeue-expired'
                AND json_extract(envelope,'$.RequestedAudit.Outcome')=0
                AND json_extract(envelope,'$.QueueRequeueReceipt.Original.State')=0
                AND length(json_extract(envelope,'$.TraceId'))=32 AND length(json_extract(envelope,'$.SpanId'))=16;
            """;
        ((long)audit.ExecuteScalar()!).Should().Be(1);
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("cancel")]
    [InlineData("private")]
    [InlineData("deadline")]
    [InlineData("readback")]
    public async Task FailedAtomicReplacementNeverRemovesSourceOrAdmitsFreshAndKeepsUncertainAdmissionHeld(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        var old = await EnqueueAsync(f, f.Request.SessionId, minutes: 1);
        f.Time.Now = old.ExpiresAt;
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using var queue = PauseQueue(f, access, new NeverAction());
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var review = (await queue.ExecuteCommandAsync(ReviewRequeue(snapshot, old), RequestOrigin.LocalUi, () => true, f.Token)).QueueRequeueReview!;
        var original = QueueBytes(f);
        var count = f.Count("security_audit_events");
        using var cancel = new CancellationTokenSource();
        checkpoint.Audit = (_, _) =>
        {
            if (string.Equals(boundary, "audit", StringComparison.Ordinal)) { throw new IOException("Required replacement audit unavailable."); }
            if (string.Equals(boundary, "cancel", StringComparison.Ordinal)) { cancel.Cancel(); }
            if (string.Equals(boundary, "private", StringComparison.Ordinal)) { access.ControlRevision++; }
            if (string.Equals(boundary, "deadline", StringComparison.Ordinal)) { f.Time.Now = review.ExpiresAt; }
        };
        if (string.Equals(boundary, "readback", StringComparison.Ordinal))
        {
            checkpoint.Commit = (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "UPDATE session_queue SET revision=revision+1;";
                command.ExecuteNonQuery();
                throw new InvalidDataException("Readback authority changed before physical commit.");
            };
        }
        var confirm = () => queue.ExecuteCommandAsync(ConfirmRequeue(review), RequestOrigin.LocalUi, () => true, cancel.Token);
        await confirm.Should().ThrowAsync<Exception>();
        checkpoint.Audit = null;
        checkpoint.Commit = null;
        QueueBytes(f).Should().Equal(original);
        f.Count("security_audit_events").Should().Be(count);
        (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, old.Request.TaskId, f.Token))!.State.Should().Be(SessionQueueState.Expired);
        (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, review.FreshRequest.TaskId, f.Token)).Should().BeNull();
        (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).DispatchControl!.Held.Should().BeTrue();
    }

    [Theory]
    [InlineData("queue")]
    [InlineData("pause")]
    [InlineData("private")]
    [InlineData("origin")]
    [InlineData("source")]
    [InlineData("ids")]
    public async Task AnyStaleExactReviewRefusesEntireProposalWithoutTargetSubstitution(string mismatch)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var old = await EnqueueAsync(f, f.Request.SessionId, minutes: 1);
        f.Time.Now = old.ExpiresAt;
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using var queue = PauseQueue(f, access, new NeverAction());
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var sourceEligible = true;
        var review = (await queue.ExecuteCommandAsync(ReviewRequeue(snapshot, old), RequestOrigin.LocalUi,
            () => sourceEligible, f.Token)).QueueRequeueReview!;
        var command = ConfirmRequeue(review);
        var origin = RequestOrigin.LocalUi;
        switch (mismatch)
        {
            case "queue": await EnqueueAsync(f, f.Request.SessionId); break;
            case "pause": await queue.ExecuteCommandAsync(PauseCommand(snapshot, true), RequestOrigin.LocalUi, () => true, f.Token); break;
            case "private": access.ControlRevision++; break;
            case "origin": origin = RequestOrigin.ActivatedVoice; break;
            case "source": sourceEligible = false; break;
            default: command = command with { FreshTaskId = Guid.NewGuid() }; break;
        }
        var original = QueueBytes(f);
        var confirm = () => queue.ExecuteCommandAsync(command, origin, () => true, f.Token);
        await confirm.Should().ThrowAsync<InvalidOperationException>();
        QueueBytes(f).Should().Equal(original);
        (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, review.FreshRequest.TaskId, f.Token)).Should().BeNull();
    }

    [WindowsFact]
    public async Task CancelReviewAdmitsNothingAndLoweredCapacityCannotInventASlot()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var old = await EnqueueAsync(f, f.Request.SessionId, minutes: 1);
        await EnqueueAsync(f, f.Request.SessionId);
        f.Time.Now = old.ExpiresAt;
        await using var queue = PauseQueue(f, new(), new NeverAction());
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var review = (await queue.ExecuteCommandAsync(ReviewRequeue(snapshot, old), RequestOrigin.LocalUi, () => true, f.Token)).QueueRequeueReview!;
        var original = QueueBytes(f);
        await queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueRequeueExpiredDiscard, snapshot.SessionId.Value)
            { QueueRequeueReviewId = review.Id }, RequestOrigin.LocalUi, () => true, f.Token);
        QueueBytes(f).Should().Equal(original);
        (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, review.FreshRequest.TaskId, f.Token)).Should().BeNull();
        var lower = () => f.Store.ReviewExpiredAsync(snapshot, old.Request.TaskId, old.Revision,
            new(new(Guid.NewGuid()), snapshot.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi), 1, new(1), 0, () => true, f.Token).AsTask();
        await lower.Should().ThrowAsync<InvalidOperationException>();
    }

    private static SessionCommand ReviewRequeue(SessionQueueSnapshot snapshot, SessionQueueEntry old) =>
        new(SessionCommandOperation.QueueRequeueExpiredReview, snapshot.SessionId.Value, snapshot.Generation.Value)
        {
            QueueRevision = snapshot.Revision, QueueRunId = snapshot.DispatchControl!.RunId,
            QueueControlRevision = snapshot.DispatchControl.Revision, TaskId = old.Request.TaskId.Value, TaskRevision = old.Revision.Value,
        };

    [WindowsFact]
    public async Task ReplacementPreservesLegacySourceBytesDigestsAndRestartNeverReplaysEitherDecision()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var old = await EnqueueAsync(f, f.Request.SessionId);
        var bytes = HostInteractionCodec.Encode(new LegacyQueuePayload(old.Request, old.Generation, old.Revision,
            old.Position, old.State, old.RunId, old.AdmissionRevision, old.EnqueuedAt, old.ExpiresAt, old.Dependency, old.DispatchOrder));
        StageKnownLegacyQueue(f, old, bytes);
        old = (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, old.Request.TaskId, f.Token))!;
        old.RecordVersion.Should().BeNull();
        RequireQueueDigest(f, old, bytes);
        var committedOriginal = OriginalQueueAudit(f, old.Position);
        f.Time.Now = old.ExpiresAt;
        var other = await EnqueueAsync(f, f.Request.SessionId, profile: SessionQueueReadProfile.Readiness);
        await using var queue = PauseQueue(f, new(), new NeverAction());
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var review = (await queue.ExecuteCommandAsync(ReviewRequeue(snapshot, old), RequestOrigin.LocalUi, () => true, f.Token)).QueueRequeueReview!;
        var fresh = (await queue.ExecuteCommandAsync(ConfirmRequeue(review), RequestOrigin.LocalUi, () => true, f.Token)).QueueRequeueReceipt!.Fresh;
        OriginalQueueAudit(f, old.Position).Should().Be(committedOriginal, "the original source bytes and digest remain immutable historical authority");
        var removed = (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, old.Request.TaskId, f.Token))!;
        removed.RecordVersion.Should().BeNull();
        removed.PendingLifetimeMinutes.Should().BeNull();
        removed.EnqueuedAt.Should().Be(old.EnqueuedAt);
        removed.ExpiresAt.Should().Be(old.ExpiresAt);
        fresh.RecordVersion.Should().Be(SessionQueueEntry.CapturedLifetimeRecordVersion);
        (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().Equal(other, fresh);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        await new HostTaskCoordinator(f.Tasks).RecoverAsync(100, f.Token);
        (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, fresh.Request.TaskId, f.Token))!.State.Should().Be(SessionQueueState.Interrupted);
        (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, old.Request.TaskId, f.Token))!.Should().Be(removed);
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().BeNull();
        var expiredPrevious = await f.Store.ReadQueueEntryAsync(f.Request.SessionId, other.Request.TaskId, f.Token);
        expiredPrevious!.State.Should().Be(SessionQueueState.Interrupted);
        await using var restarted = PauseQueue(f, new(), new NeverAction());
        var current = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var attempt = () => restarted.ExecuteCommandAsync(ReviewRequeue(current, fresh), RequestOrigin.LocalUi, () => true, f.Token);
        await attempt.Should().ThrowAsync<InvalidOperationException>();
        var currentOld = await EnqueueAsync(f, f.Request.SessionId, minutes: 1);
        f.Time.Now = currentOld.ExpiresAt;
        current = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var currentReview = (await restarted.ExecuteCommandAsync(ReviewRequeue(current, currentOld),
            RequestOrigin.LocalUi, () => true, f.Token)).QueueRequeueReview!;
        var replacement = await restarted.ExecuteCommandAsync(ConfirmRequeue(currentReview), RequestOrigin.LocalUi, () => true, f.Token);
        replacement.Queue!.Orders.Single(order => order.RunId == currentOld.RunId).Tasks.Should().Equal(currentReview.FreshRequest.TaskId);
        (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, fresh.Request.TaskId, f.Token))!.State.Should().Be(SessionQueueState.Interrupted);
    }

    [WindowsFact]
    public async Task ReplacementKeepsExactPendingPrerequisiteAndNeverRetargetsDependentsOrGrantsSuccess()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var prerequisite = await EnqueueAsync(f, f.Request.SessionId);
        var old = await EnqueueAsync(f, f.Request.SessionId, dependency: prerequisite.Request.TaskId, minutes: 1);
        var dependent = await EnqueueAsync(f, f.Request.SessionId, dependency: old.Request.TaskId);
        f.Time.Now = old.ExpiresAt;
        await using var queue = PauseQueue(f, new(), new NeverAction());
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var review = (await queue.ExecuteCommandAsync(ReviewRequeue(snapshot, old), RequestOrigin.LocalUi, () => true, f.Token)).QueueRequeueReview!;
        var fresh = (await queue.ExecuteCommandAsync(ConfirmRequeue(review), RequestOrigin.LocalUi, () => true, f.Token)).QueueRequeueReceipt!.Fresh;
        fresh.Dependency.Should().Be(prerequisite.Request.TaskId);
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().Be(prerequisite);
        await CompleteAsync(f, await AdmitAsync(f, prerequisite));
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().BeNull("the unchanged dependent now blocks FIFO on its Removed/Cancelled prerequisite");
        var work = await f.Store.ReadWorkAsync(f.Request.SessionId, 1, new(), f.Token);
        work.QueueRecords.Single(row => row.Entry.Request.TaskId == dependent.Request.TaskId).Entry.Dependency.Should().Be(old.Request.TaskId);
        await RemoveAsync(f, dependent, work.Queue.Revision);
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().Be(fresh, "only explicit removal of the independent blocked entry allows normal scheduling");
        var admitted = await AdmitAsync(f, fresh, budget: 1);
        admitted.ActiveBudgetMinutes.Should().Be(1);
        admitted.ActiveDeadlineAt.Should().Be(f.Time.Now.AddMinutes(1));
        admitted.EnqueuedAt.Should().Be(fresh.EnqueuedAt);
        admitted.ExpiresAt.Should().Be(fresh.ExpiresAt);
    }

    [WindowsFact]
    public async Task ReplacementDoesNotWaitForCurrentCallbackAndKeepsItsIndependentPauseFence()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var runningEntry = await EnqueueAsync(f, f.Request.SessionId);
        var old = await EnqueueAsync(f, f.Request.SessionId, minutes: 1);
        using var action = new HeldPauseAction(f.Token);
        await using var queue = PauseQueue(f, new(), action);
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var dispatch = queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueDispatch, snapshot.SessionId.Value, 1)
            { QueueRevision = snapshot.Revision }, RequestOrigin.LocalUi, () => true, f.Token);
        await action.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
        try
        {
            f.Time.Now = old.ExpiresAt;
            var current = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
            var paused = (await queue.ExecuteCommandAsync(PauseCommand(current, true), RequestOrigin.LocalUi, () => true, f.Token)).Queue!;
            var review = (await queue.ExecuteCommandAsync(ReviewRequeue(paused, old), RequestOrigin.LocalUi, () => true, f.Token)).QueueRequeueReview!;
            var replaced = await queue.ExecuteCommandAsync(ConfirmRequeue(review), RequestOrigin.LocalUi, () => true, f.Token);
            replaced.Queue!.DispatchControl!.Paused.Should().BeTrue();
            dispatch.IsCompleted.Should().BeFalse();
            (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, runningEntry.Request.TaskId, f.Token))!.IsCurrent.Should().BeTrue();
        }
        finally { action.Release.Set(); await dispatch; }
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("dispatch")]
    [InlineData("enqueue")]
    [InlineData("reorder")]
    public async Task PhysicalReplacementCommitFenceMakesConcurrentOldProposalsStaleWithoutPartialWrites(string race)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        var old = await EnqueueAsync(f, f.Request.SessionId, minutes: 1);
        var second = await EnqueueAsync(f, f.Request.SessionId);
        var third = await EnqueueAsync(f, f.Request.SessionId);
        f.Time.Now = old.ExpiresAt;
        await using var queue = PauseQueue(f, new(), new NeverAction());
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var review = (await queue.ExecuteCommandAsync(ReviewRequeue(snapshot, old), RequestOrigin.LocalUi, () => true, f.Token)).QueueRequeueReview!;
        var move = (await queue.ExecuteCommandAsync(MoveCommand(snapshot, third, second, true),
            RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        checkpoint.Commit = (_, _) =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10), f.Token)) { throw new TimeoutException("Replacement commit was not released."); }
        };
        var replacement = queue.ExecuteCommandAsync(ConfirmRequeue(review), RequestOrigin.LocalUi, () => true, f.Token);
        entered.Wait(TimeSpan.FromSeconds(10), f.Token).Should().BeTrue();
        Task competing = race switch
        {
            "remove" => RemoveAsync(f, old, snapshot.Revision),
            "dispatch" => AdmitAsync(f, old),
            "enqueue" => EnqueueAsync(f, f.Request.SessionId, expected: snapshot.Revision),
            _ => queue.ExecuteCommandAsync(ConfirmMove(move), RequestOrigin.LocalUi, () => true, f.Token),
        };
        try
        {
            competing.IsCompleted.Should().BeFalse("the existing physical admission lease is shared by all queue mutations");
            release.Set();
            var receipt = (await replacement).QueueRequeueReceipt!;
            var stale = () => competing;
            if (race is "dispatch") { await stale.Should().ThrowAsync<InvalidDataException>(); }
            else { await stale.Should().ThrowAsync<InvalidOperationException>(); }
            var current = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
            current.Entries.Should().Equal(second, third, receipt.Fresh);
            current.Orders.Single().Tasks.Should().Equal(second.Request.TaskId, third.Request.TaskId, receipt.Fresh.Request.TaskId);
            (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, old.Request.TaskId, f.Token))!.State.Should().Be(SessionQueueState.Removed);
        }
        finally { release.Set(); checkpoint.Commit = null; await replacement; }
    }

    private static SessionCommand ConfirmRequeue(SessionQueueRequeueReview review) =>
        ReviewRequeue(review.Queue, review.Original) with
        {
            Operation = SessionCommandOperation.QueueRequeueExpiredConfirm, FreshRequestId = review.FreshRequest.RequestId.Value,
            FreshTaskId = review.FreshRequest.TaskId.Value, QueueRequeueReviewId = review.Id,
        };

    private static string OriginalQueueAudit(InteractionStorageFixture f, long position)
    {
        using var raw = f.OpenRaw();
        using var command = raw.CreateCommand();
        command.CommandText = "SELECT envelope FROM security_audit_events WHERE sequence=$sequence;";
        command.Parameters.AddWithValue("$sequence", position);
        return (string)command.ExecuteScalar()!;
    }
}
