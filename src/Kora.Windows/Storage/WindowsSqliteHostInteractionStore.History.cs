using System.Collections.Immutable;
using System.Text;

using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    internal static void ValidateHistoryProvenance(SqliteConnection connection, SessionHistoryEvent value,
        SessionHistoryPersistence.Source? source)
    {
        if (value.Kind == SessionHistoryKind.Task)
        {
            using var task = connection.CreateCommand();
            task.CommandText = """
                SELECT t.request_id,t.session_id,e.state FROM host_tasks t JOIN host_task_events e ON e.task_id=t.task_id
                WHERE t.task_id=$id AND e.revision=$revision;
                """;
            task.Parameters.AddWithValue("$id", value.TaskId?.ToString("D") ?? string.Empty);
            task.Parameters.AddWithValue("$revision", value.SourceRevision);
            using var row = task.ExecuteReader();
            if (!row.Read() || !Same(row.GetString(0), value.RequestId?.ToString("D") ?? string.Empty)
                || !Same(row.GetString(1), Id(value.SessionId)) || row.GetInt64(2) != (long?)value.TaskState)
            {
                throw new InvalidDataException("History task provenance is missing or crosses session ownership.");
            }
        }
        if (value.AuditSequence is not { } sequence) { return; }
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT envelope FROM security_audit_events WHERE sequence=$sequence;";
        command.Parameters.AddWithValue("$sequence", sequence);
        var json = command.ExecuteScalar() as string
            ?? throw new InvalidDataException("History required typed provenance is missing.");
        var audit = HostInteractionCodec.Decode<AuthorityAudit>(json);
        if (audit.Request.SessionId != value.SessionId
            || value.Kind == SessionHistoryKind.Decision
                && (audit.Request.RequestId.Value != value.RequestId || audit.Request.TaskId.Value != value.TaskId))
        {
            throw new InvalidDataException("History provenance cannot cross host request/session identity.");
        }
        if (value.Kind == SessionHistoryKind.Decision && audit.Outcome != value.Decision)
        {
            throw new InvalidDataException("History decision disagrees with its host-committed outcome.");
        }
        if (value.Kind is SessionHistoryKind.Question or SessionHistoryKind.Answer)
        {
            if (!audit.Changes.Any(change => Same(change.Kind, "question")
                && Same(change.Id, value.SourceId?.ToString("D") ?? string.Empty) && change.Revision == value.SourceRevision))
            {
                throw new InvalidDataException("History question provenance is missing.");
            }
            if (source?.Question is { } question)
            {
                ValidateRowAuthority(connection, sequence, Change("question", Id(question.Key.QuestionId),
                    question.Key.Revision.Value, HostInteractionCodec.Encode(question)));
            }
        }
    }

    private void MigrateHistory(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        ValidateConsolidatedAuthority(connection);
        Execute(connection, transaction, string.Join(';', HostInteractionSchema.HistorySchema));
        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT session_id,generation,state FROM work_sessions ORDER BY session_id;";
        var sessions = new List<SessionRow>();
        using (var reader = read.ExecuteReader()) { while (reader.Read()) { sessions.Add(DecodeSession(reader)); } }
        foreach (var session in sessions)
        {
            token.ThrowIfCancellationRequested();
            SessionHistoryPersistence.Seed(connection, transaction, session.Authority.SessionId, session.Authority.Generation, true);
            if (session.State == 2)
            {
                SessionHistoryPersistence.Redact(connection, transaction, session.Authority.SessionId);
            }
            else
            {
                using var taskRead = connection.CreateCommand();
                taskRead.Transaction = transaction;
                taskRead.CommandText = "SELECT * FROM host_tasks WHERE session_id=$id ORDER BY task_id;";
                taskRead.Parameters.AddWithValue("$id", Id(session.Authority.SessionId));
                var tasksAtMigration = new List<HostTaskRecord>();
                using (var reader = taskRead.ExecuteReader())
                {
                    while (reader.Read()) { tasksAtMigration.Add(WindowsSqliteHostTaskStore.Decode(reader)); }
                }
                foreach (var task in tasksAtMigration)
                {
                    token.ThrowIfCancellationRequested();
                    SessionHistoryPersistence.Task(connection, transaction, task, baseline: true);
                }
                foreach (var question in ReadQuestions(connection, session.Authority.SessionId))
                {
                    SessionHistoryPersistence.Question(connection, transaction, question,
                        QuestionAudit(connection, question.Key.QuestionId.Value), baseline: true);
                }
            }
        }
        checkpoint?.BeforeCommit(connection, transaction);
    }

    private static long QuestionAudit(SqliteConnection connection, Guid question)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT audit_sequence FROM host_questions WHERE question_id=$id;";
        command.Parameters.AddWithValue("$id", question.ToString("D"));
        return (long)(command.ExecuteScalar() ?? throw new InvalidDataException("Question provenance is missing."));
    }

    public ValueTask<SessionHistoryPage> ReadHistoryAsync(HostId<SessionIdentity> session,
        SessionHistoryCursor? cursor, int limit, CancellationToken cancellationToken)
    {
        session.Validate();
        SessionHistoryPage.ValidateLimit(limit);
        cursor?.Validate(session);
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(cancellationToken);
            var identity = RequireInspectionIdentity();
            using var connection = database.OpenReadOnly(cancellationToken);
            ValidateAuthority(connection);
            var row = RequireSession(connection, session);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT sequence FROM session_history_heads WHERE session_id=$id;";
            command.Parameters.AddWithValue("$id", Id(session));
            var head = (long)(command.ExecuteScalar() ?? throw new InvalidDataException("History head is unavailable."));
            if (cursor is not null && (cursor.Generation != row.Authority.Generation || cursor.Snapshot > head))
            {
                throw new InvalidOperationException("History lifecycle or snapshot changed. Refresh the exact session.");
            }
            var snapshot = cursor?.Snapshot ?? head;
            var after = cursor?.After ?? 0;
            command.CommandText = """
                SELECT * FROM session_history WHERE session_id=$id AND sequence>$after AND sequence<=$snapshot
                ORDER BY sequence LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$after", after);
            command.Parameters.AddWithValue("$snapshot", snapshot);
            command.Parameters.AddWithValue("$limit", limit + 1);
            var rows = ImmutableArray.CreateBuilder<SessionHistoryEvent>();
            var bytes = 0;
            var more = false;
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var value = BoundHistoryEvent(SessionHistoryPersistence.Decode(reader, row.State == 2));
                    var size = Encoding.UTF8.GetByteCount(HostInteractionCodec.Encode(value));
                    if (rows.Count >= limit || bytes + size > SessionHistoryPage.MaximumBytes - 2048)
                    {
                        more = true;
                        break;
                    }
                    rows.Add(value);
                    bytes += size;
                }
            }
            database.VerifyFiles();
            if (!Same(identity, RequireInspectionIdentity())) { throw new InvalidDataException("History storage changed during inspection."); }
            cancellationToken.ThrowIfCancellationRequested();
            return new SessionHistoryPage(session, row.Authority.Generation, row.State == 2, snapshot, rows.ToImmutable(),
                more ? new(session, row.Authority.Generation, snapshot, rows[^1].Sequence) : null);
        }, cancellationToken));
    }

    public ValueTask<SessionHistoryEvent?> ReadHistoryEventAsync(HostId<SessionIdentity> session,
        Guid eventId, CancellationToken cancellationToken)
    {
        session.Validate();
        if (eventId == Guid.Empty) { throw new ArgumentException("An exact history event ID is required.", nameof(eventId)); }
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(cancellationToken);
            var identity = RequireInspectionIdentity();
            using var connection = database.OpenReadOnly(cancellationToken);
            ValidateAuthority(connection);
            var row = RequireSession(connection, session);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM session_history WHERE session_id=$id AND event_id=$event;";
            command.Parameters.AddWithValue("$id", Id(session));
            command.Parameters.AddWithValue("$event", eventId.ToString("D"));
            using var reader = command.ExecuteReader();
            var value = reader.Read() ? BoundHistoryEvent(SessionHistoryPersistence.Decode(reader, row.State == 2)) : null;
            database.VerifyFiles();
            if (!Same(identity, RequireInspectionIdentity())) { throw new InvalidDataException("History storage changed during inspection."); }
            cancellationToken.ThrowIfCancellationRequested();
            return value;
        }, cancellationToken));
    }

    private static SessionHistoryEvent BoundHistoryEvent(SessionHistoryEvent value) =>
        Encoding.UTF8.GetByteCount(HostInteractionCodec.Encode(value)) <= SessionHistoryPage.MaximumBytes - 2048
            ? value : value with { Availability = SessionHistoryAvailability.Unavailable,
                Question = null, Options = [], Answer = null, Choices = [] };
}
