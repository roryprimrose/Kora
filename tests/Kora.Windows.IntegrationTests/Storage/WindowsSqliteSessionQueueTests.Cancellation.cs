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
    public async Task SelectedMixedProfileCancellationCommitsOneExactAuditedSetAndRetainsLineageUnselectedBytesDependentsAndFairDispatch()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var session = f.Request.SessionId;
        var first = await EnqueueAsync(f, session);
        var second = await EnqueueAsync(f, session, profile: SessionQueueReadProfile.Capabilities);
        var dependent = await EnqueueAsync(f, session, dependency: first.Request.TaskId, profile: SessionQueueReadProfile.Readiness);
        var fourth = await EnqueueAsync(f, session, profile: SessionQueueReadProfile.Runtimes);
        var other = (await WindowsSqliteSessionWorkspaceTests.Service(f, new()).CreateAsync(new("Independent"),
            RequestOrigin.LocalUi, f.Token)).Authority.SessionId;
        var otherEntry = await EnqueueAsync(f, other);
        using var actions = new FixedRegistryFixture(f.Time);
        await using var queue = PauseQueue(f, new(), new DeterministicVersionQueueAction(actions.Registry));
        var initial = await f.Store.ReadQueueAsync(session, f.Token);
        var movedReview = (await queue.ExecuteCommandAsync(MoveCommand(initial, fourth, dependent, true),
            RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
        var ordered = (await queue.ExecuteCommandAsync(ConfirmMove(movedReview), RequestOrigin.LocalUi, () => true, f.Token)).Queue!;
        var original = QueueBytes(f);
        var retention = await f.Store.ReadRetentionAsync(session, f.Token);
        var review = (await queue.ExecuteCommandAsync(CancellationCommand(ordered, [first, second]),
            RequestOrigin.ActivatedVoice, () => true, f.Token)).QueueCancelReview!;
        QueueBytes(f).Should().Equal(original);
        f.Time.Now = f.Time.Now.AddMinutes(1);
        var committed = await queue.ExecuteCommandAsync(ConfirmCancellation(review), RequestOrigin.ActivatedVoice, () => true, f.Token);
        committed.Queue!.Entries.Should().Equal(fourth, dependent);
        committed.Queue.DispatchControl.Should().Be(ordered.DispatchControl);
        committed.Queue.Orders.Single().Tasks.Should().Equal(fourth.Request.TaskId, dependent.Request.TaskId);
        committed.QueueCancelReceipt!.Entries.Should().Equal(first, second);
        var after = QueueBytes(f);
        after.Where(bytes => bytes.StartsWith(fourth.Request.TaskId.Value.ToString("D"), StringComparison.Ordinal)
            || bytes.StartsWith(dependent.Request.TaskId.Value.ToString("D"), StringComparison.Ordinal)
            || bytes.StartsWith(otherEntry.Request.TaskId.Value.ToString("D"), StringComparison.Ordinal))
            .Should().Equal(original.Where(bytes => bytes.StartsWith(fourth.Request.TaskId.Value.ToString("D"), StringComparison.Ordinal)
                || bytes.StartsWith(dependent.Request.TaskId.Value.ToString("D"), StringComparison.Ordinal)
                || bytes.StartsWith(otherEntry.Request.TaskId.Value.ToString("D"), StringComparison.Ordinal)));
        foreach (var selected in new[] { first, second })
        {
            (await f.Store.ReadQueueEntryAsync(session, selected.Request.TaskId, f.Token)).Should().Be(selected with
                { State = SessionQueueState.Cancelled, Revision = new(selected.Revision.Value + 1) });
            (await f.Store.ReadTaskAsync(session, selected.Request.TaskId, f.Token))!.Task.State.Should().Be(HostTaskState.Cancelled);
        }
        (await f.Store.ReadRetentionAsync(session, f.Token)).Should().Be(retention);
        using (var raw = f.OpenRaw())
        using (var audit = raw.CreateCommand())
        {
            audit.CommandText = """
                SELECT COUNT(*) FROM security_audit_events
                WHERE json_extract(envelope,'$.Audit.ActionId')='queue.cancel-selected'
                    AND json_array_length(envelope,'$.QueueCancelReceipt.Entries')=2
                    AND json_array_length(envelope,'$.Changes')=6
                    AND json_extract(envelope,'$.RequestedAudit.Outcome')=0
                    AND json_extract(envelope,'$.Audit.Outcome')=1
                    AND length(json_extract(envelope,'$.TraceId'))=32
                    AND length(json_extract(envelope,'$.SpanId'))=16;
                """;
            ((long)audit.ExecuteScalar()!).Should().Be(1);
        }
        var dispatch = await queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueDispatch, session.Value, 1)
            { QueueRevision = committed.Queue.Revision }, RequestOrigin.LocalUi, () => true, f.Token);
        dispatch.QueueDispatch!.Select(receipt => receipt.Entry.Request.TaskId).Should().Equal(fourth.Request.TaskId, otherEntry.Request.TaskId);
        var work = await f.Store.ReadWorkAsync(session, 1, new(), f.Token);
        work.QueueRecords.Single(row => row.Entry.Request.TaskId == dependent.Request.TaskId).Eligibility.Should().Be(SessionQueueEligibility.DependencyNotSucceeded);
        work.QueueRecords.Where(row => row.Entry.State == SessionQueueState.Cancelled).Should().HaveCount(2);
        actions.Probe.Calls.Should().Be(0);
        var history = await f.Store.ReadHistoryAsync(session, null, 50, f.Token);
        history.Records.Should().Contain(row => row.TaskId == first.Request.TaskId.Value);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().BeNull();
        (await f.Store.ReadWorkAsync(session, 1, new(), f.Token)).QueueRecords
            .Single(row => row.Entry.Request.TaskId == dependent.Request.TaskId).Eligibility.Should().Be(SessionQueueEligibility.InterruptedNoReplay);
        (await f.Store.ReadQueueEntryAsync(session, first.Request.TaskId, f.Token))!.State.Should().Be(SessionQueueState.Cancelled);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(SessionQueueLimits.MaximumPendingPerSession)]
    public async Task EntireExpiredPendingSelectionRemainsCancelableAfterCapacityLoweringWithoutDeadlineRenewal(int count)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var entries = new List<SessionQueueEntry>();
        for (var index = 0; index < count; index++) { entries.Add(await EnqueueAsync(f, f.Request.SessionId, profile: (SessionQueueReadProfile)(index % 4))); }
        f.Time.Now = entries[0].ExpiresAt;
        await using var queue = new SessionQueueService(f.Store, f.Store, new(f.Tasks), new WindowsSqliteSessionWorkspaceTests.Access(),
            new NeverAction(), new(pendingPerSession: 1), NullLogger<SessionQueueService>.Instance, f.Time);
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        snapshot.Entries.Should().OnlyContain(entry => entry.State == SessionQueueState.Expired);
        var review = (await queue.ExecuteCommandAsync(CancellationCommand(snapshot, entries), RequestOrigin.LocalUi, () => true, f.Token)).QueueCancelReview!;
        var committed = await queue.ExecuteCommandAsync(ConfirmCancellation(review), RequestOrigin.LocalUi, () => true, f.Token);
        committed.Queue!.Entries.Should().BeEmpty();
        committed.Queue.DispatchControl.Should().Be(snapshot.DispatchControl);
        foreach (var entry in entries)
        {
            (await f.Store.ReadQueueEntryAsync(entry.Request.SessionId, entry.Request.TaskId, f.Token)).Should().Be(entry with
                { State = SessionQueueState.Cancelled, Revision = new(entry.Revision.Value + 1) });
        }

        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().BeNull();
    }

    [WindowsFact]
    public async Task ExactKnownPendingCleanupPreservesExistingRunOnlyHoldWithoutRestoringDispatchEligibility()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var first = await EnqueueAsync(f, f.Request.SessionId);
        var second = await EnqueueAsync(f, f.Request.SessionId);
        await using var queue = PauseQueue(f, new(), new NeverAction());
        var snapshot = (await queue.ExecuteCommandAsync(PauseCommand(await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token), true),
            RequestOrigin.LocalUi, () => true, f.Token)).Queue!;
        await f.Store.HoldDispatchControlAsync(snapshot, f.Token);
        var held = await f.Store.ReadQueueAsync(snapshot.SessionId, f.Token);
        held.DispatchControl!.Held.Should().BeTrue();
        var review = (await queue.ExecuteCommandAsync(CancellationCommand(held, [first]), RequestOrigin.LocalUi, () => true, f.Token)).QueueCancelReview!;
        var result = await queue.ExecuteCommandAsync(ConfirmCancellation(review), RequestOrigin.LocalUi, () => true, f.Token);
        result.Queue!.DispatchControl.Should().Be(held.DispatchControl);
        result.Queue.Entries.Should().Equal(second);
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().BeNull();
    }

    [Theory]
    [InlineData("admission")]
    [InlineData("remove")]
    [InlineData("enqueue")]
    [InlineData("reorder")]
    [InlineData("pause")]
    [InlineData("epoch")]
    [InlineData("member")]
    public async Task AnyConcurrentMutationOrOneChangedMemberRejectsEntireSelectionAtTheAuthoritativeFence(string mutation)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var first = await EnqueueAsync(f, f.Request.SessionId);
        var second = await EnqueueAsync(f, f.Request.SessionId);
        var third = await EnqueueAsync(f, f.Request.SessionId);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using var queue = PauseQueue(f, access, new NeverAction());
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var review = (await queue.ExecuteCommandAsync(CancellationCommand(snapshot, [first, second]),
            RequestOrigin.LocalUi, () => true, f.Token)).QueueCancelReview!;
        if (mutation is "admission") { await AdmitAsync(f, first); }
        if (mutation is "remove") { await RemoveAsync(f, third, snapshot.Revision); }
        if (mutation is "enqueue") { await EnqueueAsync(f, f.Request.SessionId); }
        if (mutation is "pause") { await queue.ExecuteCommandAsync(PauseCommand(snapshot, true), RequestOrigin.LocalUi, () => true, f.Token); }
        if (mutation is "epoch") { access.ControlRevision++; }
        if (mutation is "reorder")
        {
            var move = (await queue.ExecuteCommandAsync(MoveCommand(snapshot, third, second, true), RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
            await queue.ExecuteCommandAsync(ConfirmMove(move), RequestOrigin.LocalUi, () => true, f.Token);
        }
        var original = QueueBytes(f);
        var command = ConfirmCancellation(review);
        if (mutation is "member") { command = command with { QueueSelection = [command.QueueSelection[0], command.QueueSelection[1] with { Revision = new(2) }] }; }
        var confirm = () => queue.ExecuteCommandAsync(command, RequestOrigin.LocalUi, () => true, f.Token);
        await confirm.Should().ThrowAsync<InvalidOperationException>();
        QueueBytes(f).Should().Equal(original);
        (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, second.Request.TaskId, f.Token))!.State.Should().Be(SessionQueueState.Pending);
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("write")]
    [InlineData("cancel")]
    [InlineData("privacy")]
    [InlineData("readback")]
    [InlineData("uncertain")]
    [InlineData("control-readback")]
    [InlineData("task-readback")]
    public async Task RequiredAuditWriteReadbackAndPreCommitAdmissionFailuresLeaveNoPartialSelectedRowsTasksOrLineage(string boundary)
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
        var review = (await queue.ExecuteCommandAsync(CancellationCommand(snapshot, [first, second]),
            RequestOrigin.LocalUi, () => true, f.Token)).QueueCancelReview!;
        var bytes = QueueBytes(f);
        using var cancel = new CancellationTokenSource();
        checkpoint.Audit = (connection, transaction) =>
        {
            if (boundary is "audit") { throw new IOException("Required typed audit unavailable."); }
            if (boundary is "write")
            {
                using var fail = connection.CreateCommand();
                fail.Transaction = transaction;
                fail.CommandText = "CREATE TRIGGER refuse_second BEFORE UPDATE ON session_queue WHEN NEW.task_id=$id BEGIN SELECT RAISE(ABORT,'write unavailable'); END;";
                // SQLite trigger DDL cannot bind a parameter; the fixture-generated canonical GUID is not input.
                fail.CommandText = fail.CommandText.Replace("$id", "'" + second.Request.TaskId.Value.ToString("D") + "'", StringComparison.Ordinal);
                fail.ExecuteNonQuery();
            }
            if (boundary is "readback")
            {
                using var fail = connection.CreateCommand();
                fail.Transaction = transaction;
                fail.CommandText = "CREATE TRIGGER ignore_second BEFORE UPDATE ON session_queue WHEN NEW.task_id='" + second.Request.TaskId.Value.ToString("D")
                    + "' BEGIN SELECT RAISE(IGNORE); END;";
                fail.ExecuteNonQuery();
            }
            if (boundary is "control-readback" or "task-readback")
            {
                var target = boundary is "control-readback" ? HostActivity.RequireCurrent().Request.TaskId : second.Request.TaskId;
                using var fail = connection.CreateCommand();
                fail.Transaction = transaction;
                fail.CommandText = "CREATE TRIGGER ignore_task BEFORE UPDATE ON host_tasks WHEN NEW.task_id='" + target.Value.ToString("D")
                    + "' BEGIN SELECT RAISE(IGNORE); END;";
                fail.ExecuteNonQuery();
            }
            if (boundary is "cancel") { cancel.Cancel(); }
            if (boundary is "privacy") { access.ControlRevision++; }
        };
        checkpoint.Commit = (connection, transaction) =>
        {
            if (boundary is not "uncertain") { return; }
            using var fault = connection.CreateCommand();
            fault.Transaction = transaction;
            fault.CommandText = """
                CREATE TABLE selected_cancel_commit_fault(
                    task_id TEXT REFERENCES host_tasks(task_id) DEFERRABLE INITIALLY DEFERRED);
                INSERT INTO selected_cancel_commit_fault VALUES($missing);
                """;
            fault.Parameters.AddWithValue("$missing", Guid.NewGuid().ToString("D"));
            fault.ExecuteNonQuery();
        };
        var confirm = () => queue.ExecuteCommandAsync(ConfirmCancellation(review), RequestOrigin.LocalUi, () => true, cancel.Token);
        await confirm.Should().ThrowAsync<Exception>();
        checkpoint.Audit = null;
        checkpoint.Commit = null;
        QueueBytes(f).Should().Equal(bytes);
        foreach (var entry in new[] { first, second })
        {
            (await f.Store.ReadQueueEntryAsync(entry.Request.SessionId, entry.Request.TaskId, f.Token)).Should().Be(entry);
            (await f.Store.ReadTaskAsync(entry.Request.SessionId, entry.Request.TaskId, f.Token))!.Task.State.Should().Be(HostTaskState.IntentRecorded);
        }
        var after = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        after.Revision.Should().Be(snapshot.Revision);
        after.DispatchControl!.Held.Should().BeTrue();
        after.DispatchControl.Paused.Should().Be(snapshot.DispatchControl!.Paused);
        (await f.Store.FindReadyAsync(access.ControlRevision, new(), f.Token)).Should().BeNull();
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().BeNull();
    }

    [WindowsFact]
    public async Task SelectedCancellationDoesNotWaitForRunningCallbackAndKeepsExplicitPauseAndCapturedActiveReceipt()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var first = await EnqueueAsync(f, f.Request.SessionId);
        var second = await EnqueueAsync(f, f.Request.SessionId);
        var third = await EnqueueAsync(f, f.Request.SessionId);
        using var action = new HeldPauseAction(f.Token);
        await using var queue = PauseQueue(f, new(), action);
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var dispatch = queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueDispatch, snapshot.SessionId.Value, 1)
            { QueueRevision = snapshot.Revision }, RequestOrigin.LocalUi, () => true, f.Token);
        await action.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
        try
        {
            var current = await f.Store.ReadQueueAsync(snapshot.SessionId, f.Token);
            var running = current.Entries.Single(entry => entry.IsCurrent);
            var paused = (await queue.ExecuteCommandAsync(PauseCommand(current, true), RequestOrigin.LocalUi, () => true, f.Token)).Queue!;
            var review = (await queue.ExecuteCommandAsync(CancellationCommand(paused, [second]), RequestOrigin.LocalUi, () => true, f.Token)).QueueCancelReview!;
            var cancelled = (await queue.ExecuteCommandAsync(ConfirmCancellation(review), RequestOrigin.LocalUi, () => true, f.Token)).Queue!;
            cancelled.DispatchControl.Should().Be(paused.DispatchControl);
            cancelled.Entries.Where(entry => entry.IsPending).Should().Equal(third);
            (await f.Store.ReadQueueEntryAsync(snapshot.SessionId, first.Request.TaskId, f.Token)).Should().Be(running);
            dispatch.IsCompleted.Should().BeFalse();
            action.Release.Set();
            (await dispatch).QueueDispatch!.Select(receipt => receipt.Entry.Request.TaskId).Should().Equal(first.Request.TaskId);
        }
        finally { action.Release.Set(); await dispatch; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SimultaneousPhysicalAdmissionAndWholeSetCancellationSerializeWithoutEverCancellingAnAdmittedMember(bool cancellationFirst)
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
        var review = (await queue.ExecuteCommandAsync(CancellationCommand(snapshot, [first, second]),
            RequestOrigin.LocalUi, () => true, f.Token)).QueueCancelReview!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        checkpoint.Commit = (connection, transaction) =>
        {
            using var audit = connection.CreateCommand();
            audit.Transaction = transaction;
            audit.CommandText = "SELECT json_extract(envelope,'$.Audit.ActionId') FROM security_audit_events ORDER BY sequence DESC LIMIT 1;";
            var target = cancellationFirst ? "queue.cancel-selected" : "queue.admit.version";
            if (!string.Equals(audit.ExecuteScalar() as string, target, StringComparison.Ordinal)) { return; }
            entered.TrySetResult();
            release.Wait(f.Token);
        };
        Task<SessionCommandResult>? cancellation = null;
        Task<SessionQueueEntry>? admission = null;
        try
        {
            if (cancellationFirst)
            {
                cancellation = queue.ExecuteCommandAsync(ConfirmCancellation(review), RequestOrigin.LocalUi, () => true, f.Token);
                await entered.Task.WaitAsync(f.Token);
                admission = AdmitAsync(f, first);
                admission.IsCompleted.Should().BeFalse();
            }
            else
            {
                admission = AdmitAsync(f, first);
                await entered.Task.WaitAsync(f.Token);
                cancellation = queue.ExecuteCommandAsync(ConfirmCancellation(review), RequestOrigin.LocalUi, () => true, f.Token);
                cancellation.IsCompleted.Should().BeFalse();
            }
            release.Set();
            if (cancellationFirst)
            {
                (await cancellation!).QueueCancelReceipt!.Entries.Should().Equal(first, second);
                var loser = () => admission!;
                await loser.Should().ThrowAsync<InvalidDataException>();
                (await f.Store.ReadQueueAsync(snapshot.SessionId, f.Token)).Entries.Should().BeEmpty();
            }
            else
            {
                (await admission!).State.Should().Be(SessionQueueState.Running);
                var loser = () => cancellation!;
                await loser.Should().ThrowAsync<InvalidOperationException>();
                (await f.Store.ReadQueueEntryAsync(snapshot.SessionId, second.Request.TaskId, f.Token)).Should().Be(second);
            }
        }
        finally { release.Set(); checkpoint.Commit = null; }
    }

    internal static SessionCommand CancellationCommand(SessionQueueSnapshot snapshot, IEnumerable<SessionQueueEntry> entries) =>
        new(SessionCommandOperation.QueueCancelSelectedReview, snapshot.SessionId.Value, snapshot.Generation.Value)
        {
            QueueRevision = snapshot.Revision, QueueRunId = snapshot.DispatchControl!.RunId,
            QueueControlRevision = snapshot.DispatchControl.Revision,
            QueueSelection = [.. entries.Select(entry => new SessionQueueSelection(entry.Request.TaskId, entry.Revision))],
        };

    internal static SessionCommand ConfirmCancellation(SessionQueueCancelReview review) =>
        CancellationCommand(review.Queue, review.Entries) with
        { Operation = SessionCommandOperation.QueueCancelSelectedConfirm, QueueCancelReviewId = review.Id };
}
