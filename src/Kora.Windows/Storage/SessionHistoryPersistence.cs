using System.Security.Cryptography;
using System.Text;

using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

/// <summary>One projection for live writes, restart validation and exact native retrieval.</summary>
internal static class SessionHistoryPersistence
{
    internal sealed record Source(Guid Id, HostId<SessionIdentity> Session, long Sequence,
        HostRevision Generation, bool Baseline, HostTaskRecord? Task,
        HostInteractionCodec.QuestionData? Question, HostRequest? Request,
        HostInteractionOutcome? Decision, long? Audit);

    internal static void Seed(SqliteConnection connection, SqliteTransaction transaction,
        HostId<SessionIdentity> session, HostRevision generation, bool baseline)
    {
        using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = "SELECT 1 FROM session_history_heads WHERE session_id=$id;";
        check.Parameters.AddWithValue("$id", session.Value.ToString("D"));
        if (check.ExecuteScalar() is not null) { return; }
        check.CommandText = "INSERT INTO session_history_heads VALUES($id,0);";
        check.ExecuteNonQuery();
        // There is deliberately no synthetic chronological reconstruction from audit or legacy tasks.
        Append(connection, transaction, session, generation, baseline, null, null, null, null, null);
    }

    internal static void Task(SqliteConnection connection, SqliteTransaction transaction, HostTaskRecord task, bool baseline = false)
    {
        using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = "SELECT generation,state FROM work_sessions WHERE session_id=$id;";
        check.Parameters.AddWithValue("$id", task.Request.SessionId.Value.ToString("D"));
        using var reader = check.ExecuteReader();
        if (!reader.Read() || reader.GetInt64(1) == 2) { return; }
        var generation = new HostRevision(reader.GetInt64(0));
        reader.Close();
        Append(connection, transaction, task.Request.SessionId, generation, baseline, task, null, null, null, null);
    }

    internal static void Question(SqliteConnection connection, SqliteTransaction transaction,
        HostQuestionRecord question, long audit, bool baseline = false)
    {
        // Drafts are not conversation turns; only the original host prompt and finalized states are projected.
        if (question.Status == QuestionStatus.Pending && question.Key.Revision.Value != 1 && !baseline) { return; }
        Append(connection, transaction, question.Key.Request.SessionId, question.SessionGeneration, baseline,
            null, HostInteractionCodec.QuestionData.From(question), null, null, audit);
    }

    internal static void Decision(SqliteConnection connection, SqliteTransaction transaction,
        HostRequest request, HostRevision generation, HostInteractionOutcome decision, long audit) =>
        Append(connection, transaction, request.SessionId, generation, false, null, null, request, decision, audit);

    private static void Append(SqliteConnection connection, SqliteTransaction transaction,
        HostId<SessionIdentity> session, HostRevision generation, bool baseline, HostTaskRecord? task,
        HostInteractionCodec.QuestionData? question, HostRequest? request, HostInteractionOutcome? decision, long? audit)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT h.sequence,s.generation,s.state FROM session_history_heads h
            JOIN work_sessions s ON s.session_id=h.session_id WHERE h.session_id=$id;
            """;
        command.Parameters.AddWithValue("$id", session.Value.ToString("D"));
        long previous;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read())
            {
                throw new InvalidDataException("A history append requires an existing exact session history head.");
            }
            previous = reader.GetInt64(0);
            if (generation.Value > reader.GetInt64(1)
                || reader.GetInt64(2) == 2 && (task is not null || question is not null || request is not null))
            {
                throw new InvalidOperationException("History append conflicts with the exact session generation or disposition.");
            }
        }
        var sequence = checked(previous + 1);
        var source = new Source(Guid.NewGuid(), session, sequence, generation, baseline, task, question, request, decision, audit);
        var json = HostInteractionCodec.Encode(source);
        var digest = Digest(json);
        var projection = Project(source, digest);
        command.CommandText = """
            INSERT INTO session_history VALUES($event,$id,$sequence,$source,$digest,$projection);
            UPDATE session_history_heads SET sequence=$sequence WHERE session_id=$id AND sequence=$previous;
            """;
        command.Parameters.AddWithValue("$event", source.Id.ToString("D"));
        command.Parameters.AddWithValue("$sequence", sequence);
        command.Parameters.AddWithValue("$previous", previous);
        command.Parameters.AddWithValue("$source", json);
        command.Parameters.AddWithValue("$digest", digest);
        command.Parameters.AddWithValue("$projection", HostInteractionCodec.Encode(projection));
        if (command.ExecuteNonQuery() != 2) { throw new InvalidDataException("History sequence conflict."); }
    }

    internal static SessionHistoryEvent Project(Source source, string digest)
    {
        if (source.Id == Guid.Empty || source.Sequence <= 0 || source.Generation.Value <= 0
            || source.Audit is <= 0 || source.Decision is { } outcome && !Enum.IsDefined(outcome)
            || new[] { source.Task is not null, source.Question is not null, source.Request is not null }.Count(value => value) > 1
            || (source.Request is null) != (source.Decision is null))
        {
            throw new InvalidDataException("Invalid typed history source.");
        }
        source.Session.Validate();
        HostQuestionRecord? question = source.Question?.ToQuestion();
        var request = question?.Key.Request ?? source.Task?.Request ?? source.Request;
        if (request is not null && request.SessionId != source.Session
            || question is not null && question.SessionGeneration != source.Generation
            || source.Task is { } task && (task.Revision.Value <= 0 || !Enum.IsDefined(task.State)))
        {
            throw new InvalidDataException("History source ownership or revision disagrees.");
        }
        var kind = question is not null
            ? question.Status == QuestionStatus.Answered ? SessionHistoryKind.Answer : SessionHistoryKind.Question
            : source.Task is not null ? SessionHistoryKind.Task
            : source.Decision is not null ? SessionHistoryKind.Decision : SessionHistoryKind.Gap;
        return new(source.Id, source.Session, source.Sequence, source.Generation, kind,
            kind == SessionHistoryKind.Gap ? SessionHistoryAvailability.Gap
            : question is not null ? SessionHistoryAvailability.Available : SessionHistoryAvailability.MetadataOnly,
            request?.RequestId.Value, request?.TaskId.Value, question?.Key.QuestionId.Value ?? source.Task?.Request.TaskId.Value,
            question?.Key.Revision.Value ?? source.Task?.Revision.Value ?? 0, digest, source.Audit, source.Baseline,
            question?.Spec.Text, question?.Spec.Options ?? [],
            question?.Status == QuestionStatus.Answered ? question.Draft?.Text : null,
            question?.Status == QuestionStatus.Answered ? question.Draft?.Choices ?? [] : [],
            source.Task?.State, source.Decision, question?.Status, question?.AnswerChannel);
    }

    internal static SessionHistoryEvent Decode(SqliteDataReader reader, bool disposed)
    {
        var sourceJson = reader.GetString(3);
        var digest = reader.GetString(4);
        var projectionJson = reader.GetString(5);
        var projection = HostInteractionCodec.Decode<SessionHistoryEvent>(projectionJson);
        if (projection.Id == Guid.Empty || projection.Sequence <= 0 || projection.SourceRevision < 0
            || projection.AuditSequence is <= 0
            || !Same(projection.Id.ToString("D"), reader.GetString(0))
            || !Same(projection.SessionId.Value.ToString("D"), reader.GetString(1))
            || projection.Sequence != reader.GetInt64(2) || !Same(projection.ProvenanceDigest, digest)
            || digest.Length != 64 || digest.Any(character => !Uri.IsHexDigit(character))
            || projection.Generation.Value <= 0 || !Enum.IsDefined(projection.Kind) || !Enum.IsDefined(projection.Availability)
            || projection.TaskState is { } state && !Enum.IsDefined(state)
            || projection.Decision is { } decision && !Enum.IsDefined(decision)
            || projection.QuestionStatus is { } questionStatus && !Enum.IsDefined(questionStatus)
            || projection.AnswerChannel is { } channel && channel is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || projection.Options.IsDefault || projection.Choices.IsDefault
            || projection.RequestId == Guid.Empty || projection.TaskId == Guid.Empty || projection.SourceId == Guid.Empty)
        {
            throw new InvalidDataException("History citation identity is invalid.");
        }
        if (projection.Availability == SessionHistoryAvailability.Redacted)
        {
            if (!disposed || !Same(sourceJson, "{}") || projection.Question is not null || projection.Answer is not null
                || !projection.Options.IsEmpty || !projection.Choices.IsEmpty)
            {
                throw new InvalidDataException("History redaction state disagrees with the tombstone.");
            }
        }
        else if (disposed || !Same(Digest(sourceJson), digest)
            || !Same(HostInteractionCodec.Encode(Project(HostInteractionCodec.Decode<Source>(sourceJson), digest)), projectionJson))
        {
            throw new InvalidDataException("History projection disagrees with its immutable typed source.");
        }
        return projection;
    }

    internal static void Redact(SqliteConnection connection, SqliteTransaction transaction, HostId<SessionIdentity> session)
    {
        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT * FROM session_history WHERE session_id=$id ORDER BY sequence;";
        read.Parameters.AddWithValue("$id", session.Value.ToString("D"));
        var rows = new List<SessionHistoryEvent>();
        using (var reader = read.ExecuteReader())
        {
            while (reader.Read()) { rows.Add(HostInteractionCodec.Decode<SessionHistoryEvent>(reader.GetString(5))); }
        }
        foreach (var row in rows)
        {
            using var write = connection.CreateCommand();
            write.Transaction = transaction;
            write.CommandText = "UPDATE session_history SET source='{}',projection=$projection WHERE event_id=$event;";
            write.Parameters.AddWithValue("$event", row.Id.ToString("D"));
            write.Parameters.AddWithValue("$projection", HostInteractionCodec.Encode(row with
            {
                Availability = SessionHistoryAvailability.Redacted, Question = null, Options = [], Answer = null, Choices = [],
            }));
            write.ExecuteNonQuery();
        }
    }

    internal static void Validate(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM work_sessions s LEFT JOIN session_history_heads h ON h.session_id=s.session_id
            WHERE h.sequence IS NULL OR h.sequence<=0 OR h.sequence<>(SELECT count(*) FROM session_history e WHERE e.session_id=s.session_id)
                OR h.sequence<>COALESCE((SELECT max(sequence) FROM session_history e WHERE e.session_id=s.session_id),0)
            LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null) { throw new InvalidDataException("History is missing, truncated or out of sequence."); }
        command.CommandText = """
            SELECT 1 FROM host_questions q WHERE NOT EXISTS (
                SELECT 1 FROM session_history h WHERE h.session_id=q.session_id
                    AND json_extract(h.projection,'$.SourceId')=q.question_id
                    AND json_extract(h.projection,'$.Kind') IN ($question,$answer))
            UNION ALL
            SELECT 1 FROM host_tasks t JOIN work_sessions s ON s.session_id=t.session_id
            WHERE s.state<>2 AND t.state NOT IN (0,1) AND NOT EXISTS (
                SELECT 1 FROM session_history h WHERE h.session_id=t.session_id
                    AND json_extract(h.projection,'$.TaskId')=t.task_id
                    AND json_extract(h.projection,'$.SourceRevision')=t.revision
                    AND json_extract(h.projection,'$.Kind')=$task)
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$question", (int)SessionHistoryKind.Question);
        command.Parameters.AddWithValue("$answer", (int)SessionHistoryKind.Answer);
        command.Parameters.AddWithValue("$task", (int)SessionHistoryKind.Task);
        if (command.ExecuteScalar() is not null) { throw new InvalidDataException("Committed history source coverage is missing."); }
        command.CommandText = """
            SELECT e.*,s.state FROM session_history e JOIN work_sessions s ON s.session_id=e.session_id
            ORDER BY e.session_id,e.sequence;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var value = Decode(reader, reader.GetInt64(6) == 2);
            WindowsSqliteHostInteractionStore.ValidateHistoryProvenance(connection, value,
                value.Availability == SessionHistoryAvailability.Redacted
                    ? null : HostInteractionCodec.Decode<Source>(reader.GetString(3)));
        }
    }

    private static string Digest(string source) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);
}
