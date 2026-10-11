using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    private void RequireNewQueueWork(SqliteConnection connection, HostRequest control, HostRequest work,
        HostId<TaskIdentity>? dependency, SessionQueueLimits limits, int freedSlots = 0)
    {
        if (work.SessionId != control.SessionId || work.TaskId == control.TaskId || work.RequestId == control.RequestId
            || work.Origin != control.Origin || work.InvocationId is not null || ReadTask(connection, work.TaskId) is not null
            || QueueRequestExists(connection, work.RequestId)
            || ReadQueueRows(connection, control.SessionId).Count(entry => entry.IsPending && entry.RunId == runId)
                - freedSlots >= limits.PendingPerSession)
        {
            throw new InvalidOperationException("The exact work identity conflicts or the resulting pending queue exceeds its confirmed capacity.");
        }
        if (dependency is { } prerequisite && (prerequisite == work.TaskId || ReadTask(connection, prerequisite) is null))
        {
            throw new InvalidOperationException("An existing exact prerequisite task is required.");
        }
    }

    private SessionQueueEntry NewQueueEntry(SqliteConnection connection, HostRequest work, HostRevision generation,
        long admissionRevision, HostId<TaskIdentity>? dependency, SessionQueueLimits limits, SessionQueueReadProfile profile,
        DateTimeOffset enqueuedAt)
    {
        var entry = new SessionQueueEntry(work, generation, new(1), NextQueuePosition(connection), SessionQueueState.Pending,
            runId, admissionRevision, enqueuedAt, enqueuedAt.AddMinutes(limits.PendingLifetimeMinutes), dependency,
            RecordVersion: profile == SessionQueueReadProfile.Version ? SessionQueueEntry.CapturedLifetimeRecordVersion
                : SessionQueueEntry.FixedReadRecordVersion, PendingLifetimeMinutes: limits.PendingLifetimeMinutes,
            Profile: profile == SessionQueueReadProfile.Version ? null : profile);
        entry.Validate();
        return entry;
    }

    private static SessionQueueEntry RemovedQueueEntry(SessionQueueEntry entry, SessionQueueState outcome) =>
        entry with { Revision = new(checked(entry.Revision.Value + 1)), State = outcome };

    public ValueTask<SessionQueueRequeueReview> ReviewExpiredAsync(SessionQueueSnapshot expected, HostId<TaskIdentity> task,
        HostRevision entryRevision, HostRequest fresh, long admissionRevision, SessionQueueLimits limits,
        long configurationRevision, Func<bool> eligible, CancellationToken token) => new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            var snapshot = QueueSnapshot(connection, expected.SessionId);
            var control = QueueControl(connection, expected.SessionId, admissionRevision);
            if (snapshot.Generation != expected.Generation || snapshot.Revision != expected.Revision
                || expected.DispatchControl != control || control.Held || !eligible()
                || !RequireSession(connection, expected.SessionId).Authority.IsActive)
            {
                throw new InvalidOperationException("Refresh the exact unchanged current-run queue and private pause receipt.");
            }
            var original = ReadQueueRows(connection, expected.SessionId).SingleOrDefault(entry => entry.Request.TaskId == task)
                ?? throw new InvalidOperationException("The exact stored pending source is missing.");
            var originalTask = ReadTask(connection, task) ?? throw new InvalidDataException("The original task is missing.");
            var now = time.GetUtcNow();
            SessionQueueRequeue.RequireExpired(original, originalTask, runId, expected.Generation, admissionRevision, now);
            if (original.Revision != entryRevision || ReadRun(connection, task) != runId
                || fresh.SessionId != expected.SessionId || fresh.TaskId == task || fresh.RequestId == original.Request.RequestId
                || fresh.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice) || fresh.InvocationId is not null
                || ReadTask(connection, fresh.TaskId) is not null || QueueRequestExists(connection, fresh.RequestId)
                || ReadQueueRows(connection, expected.SessionId).Count(entry => entry.IsPending && entry.RunId == runId) > limits.PendingPerSession)
            {
                throw new InvalidOperationException("The source revision, fresh identity or resulting confirmed capacity is invalid.");
            }
            ValidateRequeueDependency(connection, original, fresh.TaskId);
            token.ThrowIfCancellationRequested();
            database.VerifyFiles();
            if (!eligible()) { throw new InvalidOperationException("Private requeue review admission changed."); }
            return new SessionQueueRequeueReview(Guid.NewGuid(), snapshot, original, originalTask, fresh, admissionRevision,
                limits, configurationRevision, now, now.AddMinutes(limits.PendingLifetimeMinutes));
        }, token));

    public ValueTask<SessionQueueSnapshot> RequeueExpiredAsync(HostRequest control, SessionQueueRequeueReview review,
        SessionQueueLimits limits, long configurationRevision, Func<bool> eligible, CancellationToken token) =>
        RunHostMutationAsync(control, "queue.requeue-expired", (connection, transaction, intent, audit) =>
        {
            RequireFreshMetadataIntent(intent);
            var expected = review.Queue;
            var snapshot = RequireQueueRevision(connection, control, expected.Generation, expected.Revision);
            var previous = QueueControl(connection, control.SessionId, review.AdmissionRevision);
            var original = ReadQueueRows(connection, control.SessionId).SingleOrDefault(entry => entry.Request.TaskId == review.Original.Request.TaskId);
            if (control.SessionId != expected.SessionId || control.Origin != review.FreshRequest.Origin
                || control.InvocationId is not null || previous != expected.DispatchControl || previous.Held
                || previous.RunId != runId || configurationRevision != review.ConfigurationRevision || limits != review.Limits
                || original != review.Original || ReadTask(connection, review.Original.Request.TaskId) != review.OriginalTask
                || ReadRun(connection, review.Original.Request.TaskId) != runId || !eligible())
            {
                throw new InvalidOperationException("The entire expired replacement review is stale. Refresh and review again.");
            }
            SessionQueueRequeue.RequireExpired(original!, review.OriginalTask, runId, snapshot.Generation, review.AdmissionRevision, time.GetUtcNow());
            if (review.EnqueuedAt < original!.ExpiresAt || review.EnqueuedAt.Offset != TimeSpan.Zero
                || review.ExpiresAt != review.EnqueuedAt.AddMinutes(limits.PendingLifetimeMinutes) || review.ExpiresAt <= time.GetUtcNow())
            {
                throw new InvalidOperationException("The separately reviewed fresh pending deadline is no longer current.");
            }
            RequireNewQueueWork(connection, control, review.FreshRequest, original.Dependency, limits, freedSlots: 1);
            ValidateRequeueDependency(connection, original, review.FreshRequest.TaskId);
            var fresh = NewQueueEntry(connection, review.FreshRequest, expected.Generation, review.AdmissionRevision,
                original.Dependency, limits, original.ReadProfile, review.EnqueuedAt);
            var removed = RemovedQueueEntry(original, SessionQueueState.Removed);
            var cancelled = review.OriginalTask.Next(HostTaskState.Cancelled);
            var freshTask = new HostTaskRecord(fresh.Request, new(1), HostTaskState.IntentRecorded);
            var terminal = intent.Next(HostTaskState.Succeeded);
            var currentOrder = snapshot.Orders.SingleOrDefault(order => order.RunId == runId && order.Generation == expected.Generation);
            var pending = ReadQueueRows(connection, control.SessionId)
                .Where(entry => entry.RunId == runId && entry.Generation == expected.Generation).ToArray();
            var ids = SessionQueueOrdering.Pending(pending, control.SessionId, snapshot.Orders)
                .Where(entry => entry.Request.TaskId != original.Request.TaskId).Select(entry => entry.Request.TaskId).Append(fresh.Request.TaskId);
            var order = currentOrder is null
                ? new SessionQueueOrder(1, Guid.NewGuid(), control.SessionId, expected.Generation, runId, 1, [.. ids])
                : currentOrder with { Revision = checked(currentOrder.Revision + 1), Tasks = [.. ids] };
            var receipt = new SessionQueueRequeueReceipt(review.Id, expected.Revision, previous, original,
                review.OriginalTask, fresh, configurationRevision);
            receipt.Validate();
            // Unknown COMMIT/readback/delivery never publishes optimistic replacement or releases the prior pause fence.
            queueControls[control.SessionId] = previous with { Held = true };
            var sequence = AppendAudit(connection, transaction, intent, RequireSession(connection, control.SessionId).Authority,
                audit, changes: [QueueChange(removed), TaskChange(cancelled), QueueChange(fresh), TaskChange(freshTask),
                    QueueOrderChange(order), TaskChange(terminal)], requestedAudit: audit.WithOutcome(SecurityAuditOutcome.Requested),
                queueRequeueReceipt: receipt);
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, cancelled);
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, freshTask);
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, terminal);
            Execute(connection, transaction, "INSERT INTO host_task_runs VALUES($task,$run);",
                ("$task", Id(fresh.Request.TaskId)), ("$run", runId.ToString("D")));
            WriteQueue(connection, transaction, removed, sequence);
            WriteQueue(connection, transaction, fresh, sequence);
            WriteQueueOrder(connection, transaction, order, sequence);
            order.Validate(ReadQueueRows(connection, control.SessionId));
            var rows = ReadQueueRows(connection, control.SessionId);
            if (rows.Single(entry => entry.Request.TaskId == removed.Request.TaskId) != removed
                || rows.Single(entry => entry.Request.TaskId == fresh.Request.TaskId) != fresh
                || ReadTask(connection, cancelled.Request.TaskId) != cancelled || ReadTask(connection, freshTask.Request.TaskId) != freshTask
                || ReadTask(connection, terminal.Request.TaskId) != terminal)
            {
                throw new InvalidDataException("The complete expired replacement readback is unconfirmed; admission remains held.");
            }
            TouchActivity(connection, transaction, control.SessionId);
            return QueueSnapshot(connection, control.SessionId) with { DispatchControl = previous, RequeueReceipt = receipt };
        }, token, () => eligible() && review.ExpiresAt > time.GetUtcNow(), requireIdle: false,
        committed: snapshot => queueControls[control.SessionId] = snapshot.DispatchControl!);

    private static void ValidateRequeueDependency(SqliteConnection connection, SessionQueueEntry original, HostId<TaskIdentity> fresh)
    {
        var visited = new HashSet<HostId<TaskIdentity>> { original.Request.TaskId, fresh };
        var rows = ReadQueueRows(connection);
        var dependency = original.Dependency;
        while (dependency is { } id)
        {
            if (!visited.Add(id) || ReadTask(connection, id) is not { } task)
            {
                throw new InvalidOperationException("The exact original dependency lineage is missing or cyclic.");
            }
            var queued = rows.SingleOrDefault(entry => entry.Request.TaskId == id);
            if (queued is null || !queued.IsPending)
            {
                if (task.State != HostTaskState.Succeeded) { throw new InvalidOperationException("The exact prerequisite has not succeeded or is not pending before this tail."); }
                return;
            }
            if (queued.Request.SessionId != original.Request.SessionId || queued.RunId != original.RunId
                || queued.Generation != original.Generation || queued.AdmissionRevision != original.AdmissionRevision
                || task.State != HostTaskState.IntentRecorded)
            {
                throw new InvalidOperationException("The original pending prerequisite is not current same-session work before the new tail.");
            }
            dependency = queued.Dependency;
        }
    }
}
