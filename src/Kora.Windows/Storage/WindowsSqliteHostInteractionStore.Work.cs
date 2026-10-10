using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    public ValueTask<SessionWorkSnapshot> ReadWorkAsync(HostId<SessionIdentity> session,
        long admissionRevision, SessionQueueLimits limits, CancellationToken token) =>
        WithCurrentWorkAsync(session, admissionRevision, limits, static snapshot => snapshot, token);

    public ValueTask<T> WithCurrentWorkAsync<T>(HostId<SessionIdentity> session,
        long admissionRevision, SessionQueueLimits limits, Func<SessionWorkSnapshot, T> observation,
        CancellationToken token) => new(Task.Run(() =>
    {
        using var lease = database.AcquireReadLease(token);
        using var connection = Open(created: false, token);
        using var transaction = connection.BeginTransaction(deferred: true);
        var authority = RequireSession(connection, session);
        if (authority.State == 2) { throw new InvalidOperationException("Disposed sessions have no live work surface. Use exact redacted history."); }
        var now = time.GetUtcNow();
        var allQueue = ReadQueueRows(connection);
        var projectedQueue = allQueue.Select(entry => ProjectQueue(connection, entry, now)).ToArray();
        var rows = allQueue.Where(entry => entry.Request.SessionId == session).ToArray();
        var active = rows.Where(entry => entry.IsCurrent || entry.State == SessionQueueState.Unknown
            || entry.RunId == runId && entry.IsPending).ToArray();
        var recent = rows.Except(active).OrderByDescending(entry => entry.Position)
            .Take(SessionWorkSnapshot.RecentQueueRecords);
        var states = allQueue.Where(entry => entry.Dependency is not null)
            .Select(entry => entry.Dependency!.Value).Distinct()
            .Select(id => ReadTask(connection, id)).Where(task => task is not null)
            .ToDictionary(task => task!.Request.TaskId, task => task!.State);
        var blocked = HasUnclassifiedWorkOrWait(connection, session);
        using var unknown = connection.CreateCommand();
        unknown.CommandText = "SELECT 1 FROM host_tasks WHERE session_id=$session AND state=7 LIMIT 1;";
        unknown.Parameters.AddWithValue("$session", Id(session));
        var quarantined = unknown.ExecuteScalar() is not null;
        var queueRecords = active.Concat(recent).OrderBy(entry => entry.Position).Select(entry =>
        {
            var projected = ProjectQueue(connection, entry, now);
            return new SessionQueueObservation(projected, SessionQueuePolicy.Eligibility(projected,
                projectedQueue, states, runId, now, admissionRevision, limits, authority.Authority.IsActive, blocked, quarantined),
                entry.ActiveDeadlineAt ?? (entry.DispatchOrder > 0
                    ? SessionQueueLimits.ActiveDeadlineAt(ReadAdmissionTime(connection, entry), SessionQueueLimits.DefaultActiveBudgetMinutes) : null));
        }).ToArray();
        var taskRecords = ReadWorkTasks(connection, session);
        var questions = ReadQuestions(connection, session).Where(question => question.Status == QuestionStatus.Pending)
            .OrderBy(question => question.ExpiresAt).ThenBy(question => question.Key.QuestionId.Value).ToArray();
        using var head = connection.CreateCommand();
        head.CommandText = "SELECT sequence FROM authority_head WHERE singleton=1;";
        var snapshot = new SessionWorkSnapshot(new(authority.Authority, ReadMetadata(connection, session)),
            (long)head.ExecuteScalar()!, now, QueueSnapshot(connection, session, now),
            rows.Count(entry => entry.RunId == runId && entry.IsPending), limits.PendingPerSession, limits.ExecutionSlots,
            [.. queueRecords], Math.Max(0, rows.Length - active.Length - SessionWorkSnapshot.RecentQueueRecords),
            [.. taskRecords.Records.Select(task => Observe(connection, task, authority.Authority))], taskRecords.Omitted,
            [.. questions.Take(SessionWorkSnapshot.MaximumRecords).Select(question => new SessionWorkQuestion(question.Key,
                question.SessionGeneration, question.Status, question.Spec.Kind, question.AnswerChannel, question.ExpiresAt))],
            Math.Max(0, questions.Length - SessionWorkSnapshot.MaximumRecords));
        _ = SessionCommandResult.Serialize(new("observed", SessionWorkSnapshot.Scope) { Work = snapshot });
        token.ThrowIfCancellationRequested();
        database.VerifyFiles();
        var result = observation(snapshot);
        token.ThrowIfCancellationRequested();
        database.VerifyFiles();
        return result;
    }, token));

    private static (List<HostTaskRecord> Records, int Omitted) ReadWorkTasks(SqliteConnection connection, HostId<SessionIdentity> session)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT task_id,COUNT(*) OVER() FROM host_tasks t WHERE session_id=$session
            AND NOT EXISTS (SELECT 1 FROM session_queue q WHERE q.task_id=t.task_id)
            ORDER BY CASE WHEN state IN (0,1,7) THEN 0 ELSE 1 END, task_id LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$session", Id(session));
        command.Parameters.AddWithValue("$limit", SessionWorkSnapshot.MaximumRecords);
        using var reader = command.ExecuteReader();
        var records = new List<HostTaskRecord>();
        var count = 0;
        while (reader.Read())
        {
            var id = new HostId<TaskIdentity>(ParseId(reader.GetString(0)));
            count = reader.GetInt32(1);
            records.Add(ReadTask(connection, id)!);
        }
        return (records, Math.Max(0, count - records.Count));
    }

    private static bool HasUnclassifiedWorkOrWait(SqliteConnection connection, HostId<SessionIdentity> session)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM host_tasks t WHERE session_id=$session AND
            (state=7 OR (state IN (0,1) AND NOT EXISTS
            (SELECT 1 FROM session_queue q WHERE q.task_id=t.task_id))) LIMIT 1;
            """;
        command.Parameters.AddWithValue("$session", Id(session));
        return command.ExecuteScalar() is not null;
    }

    private static DateTimeOffset ReadAdmissionTime(SqliteConnection connection, SessionQueueEntry entry)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT envelope FROM security_audit_events WHERE sequence=$sequence;";
        command.Parameters.AddWithValue("$sequence", entry.DispatchOrder);
        var envelope = command.ExecuteScalar() as string ?? throw new InvalidDataException("The exact queue admission receipt is unavailable.");
        return HostInteractionCodec.Decode<AuthorityAudit>(envelope).CommittedAt;
    }
}
