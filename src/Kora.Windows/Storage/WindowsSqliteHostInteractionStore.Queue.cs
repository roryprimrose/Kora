using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    private void MigrateQueue(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ValidateConsolidatedAuthority(connection);
        SessionHistoryPersistence.Validate(connection);
        using var guard = connection.CreateCommand();
        guard.Transaction = transaction;
        guard.CommandText = """
            SELECT 1 FROM security_audit_events a, json_each(a.envelope,'$.Changes') c
            WHERE json_extract(c.value,'$.Kind')='queue' LIMIT 1;
            """;
        if (guard.ExecuteScalar() is not null)
        {
            throw new InvalidDataException("Missing committed queue authority cannot be migrated as empty.");
        }
        Execute(connection, transaction, HostInteractionSchema.QueueTable);
        checkpoint?.BeforeCommit(connection, transaction);
    }

    public ValueTask<SessionQueueSnapshot> ReadQueueAsync(HostId<SessionIdentity> session, CancellationToken token) =>
        new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            var snapshot = QueueSnapshot(connection, session);
            database.VerifyFiles();
            return snapshot;
        }, token));

    public ValueTask<SessionQueueEntry?> ReadQueueEntryAsync(HostId<SessionIdentity> session,
        HostId<TaskIdentity> task, CancellationToken token) => new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            _ = QueueSnapshot(connection, session);
            var entry = ReadQueueRows(connection, session).SingleOrDefault(entry => entry.Request.TaskId == task);
            database.VerifyFiles();
            return entry is null ? null : ProjectQueue(connection, entry);
        }, token));

    public ValueTask<SessionQueueSnapshot> EnqueueAsync(HostRequest control, HostRequest work,
        HostRevision generation, long expectedRevision, long admissionRevision, HostId<TaskIdentity>? dependency,
        SessionQueueLimits limits, Func<bool> eligible, CancellationToken token) =>
        RunHostMutationAsync(control, "queue.enqueue.version", (connection, transaction, intent, audit) =>
        {
            RequireFreshMetadataIntent(intent);
            _ = RequireQueueRevision(connection, control, generation, expectedRevision);
            if (work.SessionId != control.SessionId || work.TaskId == control.TaskId
                || work.RequestId == control.RequestId || work.Origin != control.Origin || work.InvocationId is not null
                || ReadTask(connection, work.TaskId) is not null || QueueRequestExists(connection, work.RequestId)
                || ReadQueueRows(connection, control.SessionId).Count(entry => entry.IsPending && entry.RunId == runId) >= limits.PendingPerSession)
            {
                throw new InvalidOperationException("The exact work identity conflicts or the pending queue is full.");
            }
            if (dependency is { } prerequisite && (prerequisite == work.TaskId || ReadTask(connection, prerequisite) is null))
            {
                throw new InvalidOperationException("An existing exact prerequisite task is required.");
            }
            var now = time.GetUtcNow();
            var entry = new SessionQueueEntry(work, generation, new(1),
                NextQueuePosition(connection), SessionQueueState.Pending, runId, admissionRevision,
                now, now.Add(SessionQueuePolicy.PendingLifetime), dependency);
            entry.Validate();
            var task = new HostTaskRecord(work, new(1), HostTaskState.IntentRecorded);
            var sequence = AppendAudit(connection, transaction, intent, RequireSession(connection, control.SessionId).Authority,
                audit, changes: [QueueChange(entry), TaskChange(task)]);
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, task);
            Execute(connection, transaction, "INSERT INTO host_task_runs VALUES($task,$run);",
                ("$task", Id(work.TaskId)), ("$run", runId.ToString("D")));
            WriteQueue(connection, transaction, entry, sequence);
            return QueueSnapshot(connection, control.SessionId);
        }, token, eligible, requireIdle: false);

    public ValueTask<SessionQueueSnapshot> RemovePendingAsync(HostRequest control, HostRevision generation,
        long expectedRevision, HostId<TaskIdentity>? task, HostRevision? entryRevision, SessionQueueState outcome,
        Func<bool> eligible, CancellationToken token) =>
        RunHostMutationAsync(control, task is null ? "queue.clear" : "queue.remove", (connection, transaction, intent, audit) =>
        {
            RequireFreshMetadataIntent(intent);
            _ = RequireQueueRevision(connection, control, generation, expectedRevision);
            if (outcome is not (SessionQueueState.Cancelled or SessionQueueState.Removed)
                || (task is null) != (entryRevision is null))
            {
                throw new InvalidOperationException("Only exact pending cancellation/removal or confirmed clear is admitted.");
            }
            var rows = ReadQueueRows(connection, control.SessionId).Where(entry => entry.RunId == runId
                && entry.IsPending && (task is null || entry.Request.TaskId == task)).ToArray();
            if (task is not null && (rows.Length != 1 || rows[0].Revision != entryRevision))
            {
                throw new InvalidOperationException("The exact pending entry revision conflicts; admitted work is not removable.");
            }
            var changed = rows.Select(entry => entry with { Revision = new(checked(entry.Revision.Value + 1)), State = outcome }).ToArray();
            var tasksChanged = rows.Select(entry =>
                (ReadTask(connection, entry.Request.TaskId) ?? throw new InvalidDataException("Missing queue task."))
                .Next(HostTaskState.Cancelled)).ToArray();
            var sequence = AppendAudit(connection, transaction, intent, RequireSession(connection, control.SessionId).Authority,
                audit, changes: [.. changed.Select(QueueChange), .. tasksChanged.Select(TaskChange)]);
            for (var index = 0; index < rows.Length; index++)
            {
                WindowsSqliteHostTaskStore.WriteTask(connection, transaction, tasksChanged[index]);
                WriteQueue(connection, transaction, changed[index], sequence);
            }
            return QueueSnapshot(connection, control.SessionId);
        }, token, eligible, requireIdle: false);

    public ValueTask<SessionQueueEntry?> FindReadyAsync(long admissionRevision, SessionQueueLimits limits, CancellationToken token) =>
        new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            var ready = ReadyQueue(connection, admissionRevision, limits);
            database.VerifyFiles();
            return ready;
        }, token));

    public ValueTask<SessionQueueEntry> AdmitAsync(SessionQueueEntry expected, long admissionRevision,
        SessionQueueLimits limits, Func<bool> eligible, CancellationToken token) =>
        RunHostMutationAsync(expected.Request, "queue.admit.version", (connection, transaction, intent, audit) =>
        {
            if (intent.State != HostTaskState.IntentRecorded || ReadRun(connection, intent.Request.TaskId) != runId
                || ReadyQueue(connection, admissionRevision, limits) != expected)
            {
                throw new InvalidOperationException("Queue admission changed, is blocked or no longer owns the fair slot.");
            }
            var session = RequireSession(connection, expected.Request.SessionId).Authority;
            if (!session.IsActive || session.Generation != expected.Generation)
            {
                throw new InvalidOperationException("The exact active session generation is required.");
            }
            var admitted = expected with { Revision = new(checked(expected.Revision.Value + 1)),
                State = SessionQueueState.Running, DispatchOrder = NextQueuePosition(connection) };
            var dispatched = intent.Next(HostTaskState.DispatchRecorded);
            var sequence = AppendAudit(connection, transaction, intent, session, audit,
                changes: [QueueChange(admitted), TaskChange(dispatched)]);
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, dispatched);
            WriteQueue(connection, transaction, admitted, sequence);
            return admitted;
        }, token, eligible, requireIdle: false);

    public ValueTask<SessionQueueEntry> CompleteAsync(SessionQueueEntry expected, SessionQueueState outcome,
        Func<bool> eligible, CancellationToken token) =>
        RunHostMutationAsync(expected.Request, "queue.receipt.version", (connection, transaction, intent, audit) =>
        {
            var current = ReadQueueRows(connection, expected.Request.SessionId)
                .SingleOrDefault(entry => entry.Request.TaskId == expected.Request.TaskId);
            if (current != expected || !expected.IsCurrent || expected.RunId != runId
                || intent.State != HostTaskState.DispatchRecorded
                || outcome is not (SessionQueueState.Succeeded or SessionQueueState.Failed or SessionQueueState.Unknown))
            {
                throw new InvalidOperationException("Only the exact admitted current-run callback can commit an outcome.");
            }
            var next = current with { Revision = new(checked(current.Revision.Value + 1)), State = outcome };
            var task = intent.Next(outcome switch
            {
                SessionQueueState.Succeeded => HostTaskState.Succeeded,
                SessionQueueState.Failed => HostTaskState.Failed,
                _ => HostTaskState.Unknown,
            });
            var session = RequireSession(connection, expected.Request.SessionId).Authority;
            if (!session.IsActive || session.Generation != expected.Generation)
            {
                throw new InvalidOperationException("Session authority changed before the exact receipt.");
            }
            var sequence = AppendAudit(connection, transaction, intent, session, audit,
                changes: [QueueChange(next), TaskChange(task)]);
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, task);
            WriteQueue(connection, transaction, next, sequence);
            return next;
        }, token, eligible, requireIdle: false);

    private SessionQueueEntry? ReadyQueue(SqliteConnection connection, long admissionRevision, SessionQueueLimits limits)
    {
        var entries = ReadQueueRows(connection);
        if (entries.Count(entry => entry.IsCurrent && entry.RunId == runId) >= limits.ExecutionSlots) { return null; }
        var states = new Dictionary<HostId<TaskIdentity>, HostTaskState>();
        foreach (var entry in entries)
        {
            if (entry.Dependency is { } dependency && ReadTask(connection, dependency) is { } task)
            {
                states[dependency] = task.State;
            }
        }
        // Unclassified current effects, previous-run uncertainty and pending questions fail closed
        // for the addressed session only. No descriptor can claim an arbitrary resource lease.
        var blocked = entries.Select(entry => entry.Request.SessionId).Distinct().Where(session =>
        {
            return HasUnclassifiedWorkOrWait(connection, session) || !RequireSession(connection, session).Authority.IsActive;
        }).ToHashSet();
        return SessionQueuePolicy.SelectReady(entries.Where(entry => !blocked.Contains(entry.Request.SessionId))
            .Select(entry => entry.RunId != runId && entry.IsPending ? entry with { State = SessionQueueState.Interrupted } : entry).ToArray(),
            states, runId, time.GetUtcNow(), admissionRevision, limits);
    }

    private SessionQueueSnapshot RequireQueueRevision(SqliteConnection connection, HostRequest control,
        HostRevision generation, long revision)
    {
        var snapshot = QueueSnapshot(connection, control.SessionId);
        if (snapshot.Generation != generation || snapshot.Revision != revision
            || !RequireSession(connection, control.SessionId).Authority.IsActive)
        {
            throw new InvalidOperationException("The exact active session generation or queue revision conflicts.");
        }
        return snapshot;
    }

    private SessionQueueSnapshot QueueSnapshot(SqliteConnection connection, HostId<SessionIdentity> session, DateTimeOffset? observedAt = null)
    {
        var authority = RequireSession(connection, session);
        if (authority.State == 2) { throw new InvalidOperationException("Disposed sessions have no browsable queue."); }
        var rows = ReadQueueRows(connection, session);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(audit_sequence),0) FROM session_queue WHERE session_id=$session;";
        command.Parameters.AddWithValue("$session", Id(session));
        var revision = (long)command.ExecuteScalar()!;
        return new(session, authority.Authority.Generation, revision,
            rows.Where(entry => entry.IsCurrent || entry.State == SessionQueueState.Unknown || entry.RunId == runId && entry.IsPending)
                .Select(entry => ProjectQueue(connection, entry, observedAt)).ToArray());
    }

    private SessionQueueEntry ProjectQueue(SqliteConnection connection, SessionQueueEntry entry, DateTimeOffset? observedAt = null)
    {
        var task = ReadTask(connection, entry.Request.TaskId)!;
        if (entry.RunId != runId && (entry.IsPending || entry.IsCurrent))
        {
            return entry with { State = entry.IsCurrent ? SessionQueueState.Unknown : SessionQueueState.Interrupted };
        }
        return entry.IsPending && task.State == HostTaskState.Interrupted ? entry with { State = SessionQueueState.Interrupted }
            : entry.IsCurrent && task.State == HostTaskState.Unknown ? entry with { State = SessionQueueState.Unknown }
            : entry.IsPending && entry.ExpiresAt <= (observedAt ?? time.GetUtcNow()) ? entry with { State = SessionQueueState.Expired } : entry;
    }

    private static long NextQueuePosition(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sequence+1 FROM authority_head WHERE singleton=1;";
        return (long)command.ExecuteScalar()!;
    }

    private static bool QueueRequestExists(SqliteConnection connection, HostId<RequestIdentity> request)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM host_tasks WHERE request_id=$request LIMIT 1;";
        command.Parameters.AddWithValue("$request", Id(request));
        return command.ExecuteScalar() is not null;
    }

    private static List<SessionQueueEntry> ReadQueueRows(SqliteConnection connection, HostId<SessionIdentity>? session = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT task_id,session_id,revision,payload,audit_sequence FROM session_queue"
            + (session is null ? "" : " WHERE session_id=$session") + " ORDER BY audit_sequence,task_id;";
        if (session is { } id) { command.Parameters.AddWithValue("$session", Id(id)); }
        using var reader = command.ExecuteReader();
        var entries = new List<SessionQueueEntry>();
        while (reader.Read())
        {
            var entry = HostInteractionCodec.Decode<SessionQueueEntry>(reader.GetString(3));
            entry.Validate();
            if (!Same(Id(entry.Request.TaskId), reader.GetString(0)) || !Same(Id(entry.Request.SessionId), reader.GetString(1))
                || entry.Revision.Value != reader.GetInt64(2))
            {
                throw new InvalidDataException("Queue row identity or revision conflicts with its payload.");
            }
            ValidateRowAuthority(connection, reader.GetInt64(4), QueueChange(entry));
            entries.Add(entry);
        }
        return entries;
    }

    private static AuthorityChange QueueChange(SessionQueueEntry entry) =>
        Change("queue", Id(entry.Request.TaskId), entry.Revision.Value, HostInteractionCodec.Encode(entry));

    private static void WriteQueue(SqliteConnection connection, SqliteTransaction transaction, SessionQueueEntry entry, long sequence) =>
        Execute(connection, transaction, """
            INSERT INTO session_queue VALUES($task,$session,$revision,$payload,$audit)
            ON CONFLICT(task_id) DO UPDATE SET revision=excluded.revision,payload=excluded.payload,audit_sequence=excluded.audit_sequence;
            """, ("$task", Id(entry.Request.TaskId)), ("$session", Id(entry.Request.SessionId)),
            ("$revision", entry.Revision.Value), ("$payload", HostInteractionCodec.Encode(entry)), ("$audit", sequence));

    private static void ValidateQueue(SqliteConnection connection)
    {
        foreach (var entry in ReadQueueRows(connection))
        {
            var task = ReadTask(connection, entry.Request.TaskId);
            var expectedState = entry.State switch
            {
                SessionQueueState.Pending => task?.State is HostTaskState.IntentRecorded or HostTaskState.Interrupted,
                SessionQueueState.Running => task?.State is HostTaskState.DispatchRecorded or HostTaskState.Unknown,
                SessionQueueState.Succeeded => task?.State == HostTaskState.Succeeded,
                SessionQueueState.Failed => task?.State == HostTaskState.Failed,
                SessionQueueState.Cancelled or SessionQueueState.Removed => task?.State == HostTaskState.Cancelled,
                SessionQueueState.Unknown => task?.State == HostTaskState.Unknown,
                _ => false,
            };
            if (task is null || task.Request != entry.Request || ReadRun(connection, entry.Request.TaskId) != entry.RunId || !expectedState)
            {
                throw new InvalidDataException("The queue has lost its exact durable task/run identity.");
            }
        }
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM (
                SELECT json_extract(c.value,'$.Id') AS id, MAX(a.sequence) AS sequence
                FROM security_audit_events a, json_each(a.envelope,'$.Changes') c
                WHERE json_extract(c.value,'$.Kind')='queue' GROUP BY json_extract(c.value,'$.Id')
            ) latest LEFT JOIN session_queue q ON q.task_id=latest.id
            WHERE q.audit_sequence IS NULL OR q.audit_sequence<>latest.sequence LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null)
        {
            throw new InvalidDataException("Committed queue authority is missing or stale; replacement is forbidden.");
        }
    }
}
