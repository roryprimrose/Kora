using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    public ValueTask<SessionDispositionPreview> PreviewDispositionAsync(HostId<SessionIdentity> session,
        HostRevision expectedGeneration, long expectedMetadataRevision, CancellationToken cancellationToken) =>
        new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(cancellationToken);
            using var connection = Open(created: false, cancellationToken);
            var target = RequireDispositionTarget(connection, session, expectedGeneration, expectedMetadataRevision);
            RequireDispositionIdle(connection, session, null);
            var preview = DescribeDisposition(connection, target, null, Guid.NewGuid());
            database.VerifyFiles();
            cancellationToken.ThrowIfCancellationRequested();
            return preview;
        }, cancellationToken));

    public async ValueTask<SessionDispositionReceipt> DisposeSessionAsync(HostRequest request, SessionDispositionPreview preview,
        Func<bool> canControl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(canControl);
        var receipt = await RunHostMutationAsync(request, "session.disposition", (connection, transaction, intent, audit) =>
        {
            RequireFreshMetadataIntent(intent);
            if (request.SessionId != preview.Session.Authority.SessionId)
            {
                throw new InvalidOperationException("Disposition requires the exact previewed session ID.");
            }
            var target = RequireDispositionTarget(connection, request.SessionId, preview.Session.Authority.Generation,
                preview.Session.Metadata?.Revision.Value ?? 0);
            RequireDispositionIdle(connection, request.SessionId, request.TaskId);
            if (preview.ConfirmationId == Guid.Empty
                || preview != DescribeDisposition(connection, target, request.TaskId, preview.ConfirmationId))
            {
                throw new InvalidOperationException("Session records changed after preview. Obtain a fresh preview and confirmation.");
            }
            var removed = new WorkSessionAuthorization(request.SessionId,
                new(checked(target.Authority.Generation.Value + 1)), false);
            var completed = intent.Next(HostTaskState.Succeeded);
            var memories = MemoryInvalidations(connection, request.SessionId);
            var attachments = AttachmentInvalidation(connection, request.SessionId);
            if (attachments.Length != 0)
            {
                database.RequireEmptyArtifactInventory();
                Execute(connection, transaction, "PRAGMA secure_delete=ON; PRAGMA journal_size_limit=0;");
            }
            var sequence = AppendAudit(connection, transaction, intent, removed, audit,
                changes: [SessionChange(removed, 2), TaskChange(completed), .. memories.Select(MemoryChange), .. attachments]);
            RevokeAttachment(connection, transaction, request.SessionId, sequence);
            WriteSession(connection, transaction, removed, 2, sequence);
            foreach (var memory in memories) { WriteMemory(connection, transaction, memory, sequence); }
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, completed);
            SessionHistoryPersistence.Redact(connection, transaction, request.SessionId);
            Execute(connection, transaction, """
                DELETE FROM host_task_waits WHERE task_id IN (SELECT task_id FROM host_tasks WHERE session_id=$id);
                DELETE FROM host_observations WHERE session_id=$id;
                DELETE FROM host_questions WHERE session_id=$id;
                DELETE FROM scoped_grants WHERE session_id=$id;
                DELETE FROM session_metadata WHERE session_id=$id;
                """, ("$id", Id(request.SessionId)));
            return new SessionDispositionReceipt(request.SessionId, removed.Generation, preview);
        }, cancellationToken, canControl).ConfigureAwait(false);
        if (receipt.Removed.Attachments != 0)
        {
            using var lease = database.AcquireReadLease(CancellationToken.None);
            database.ClearCommittedJournal();
            database.RequireEmptyArtifactInventory();
        }
        return receipt;
    }

    private static SessionDispositionPreview DescribeDisposition(SqliteConnection connection,
        SessionWorkspaceEntry target, HostId<TaskIdentity>? control, Guid confirmation)
    {
        var session = target.Authority.SessionId;
        return new(confirmation, target, DispositionRevision(connection, session, control),
            CountDispositionRows(connection, "host_questions", session), CountDispositionRows(connection, "scoped_grants", session),
            CountDispositionRows(connection, "host_observations", session), CountDispositionRows(connection, "host_task_waits", session))
        { Attachments = ReadAttachmentRow(connection, session)?.Attachment is null ? 0 : 1 };
    }

    private static SessionWorkspaceEntry RequireDispositionTarget(SqliteConnection connection,
        HostId<SessionIdentity> session, HostRevision generation, long metadataRevision)
    {
        var row = RequireSession(connection, session);
        var metadata = ReadMetadata(connection, session);
        if (row.State == 2 || row.Authority.Generation != generation || (metadata?.Revision.Value ?? 0) != metadataRevision)
        {
            throw new InvalidOperationException("The exact session generation or metadata revision conflicts. Refresh before previewing.");
        }
        return new(row.Authority, metadata);
    }

    private static void RequireDispositionIdle(SqliteConnection connection, HostId<SessionIdentity> session,
        HostId<TaskIdentity>? control)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM host_tasks WHERE session_id=$id AND state IN (0,1,7) AND task_id<>$control
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", Id(session));
        command.Parameters.AddWithValue("$control", control is { } task ? Id(task) : string.Empty);
        if (command.ExecuteScalar() is not null
            || ReadQuestions(connection, session).Any(q => q.Status == QuestionStatus.Pending))
        {
            throw new InvalidOperationException("Disposition is blocked by live, unresolved or Unknown work/questions. No work was abandoned.");
        }
    }

    private static long CountDispositionRows(SqliteConnection connection, string table, HostId<SessionIdentity> session)
    {
        using var command = connection.CreateCommand();
        command.CommandText = string.Equals(table, "host_task_waits", StringComparison.Ordinal)
            ? "SELECT count(*) FROM host_task_waits WHERE task_id IN (SELECT task_id FROM host_tasks WHERE session_id=$id);"
            : "SELECT count(*) FROM " + table + " WHERE session_id=$id;";
        command.Parameters.AddWithValue("$id", Id(session));
        return (long)(command.ExecuteScalar() ?? throw new InvalidDataException("Disposition row count is unavailable."));
    }

    private static string DispositionRevision(SqliteConnection connection, HostId<SessionIdentity> session,
        HostId<TaskIdentity>? control)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        // Length framing covers exact live records and all task transitions, not a UI page or global audit head.
        foreach (var query in new[]
        {
            "SELECT * FROM work_sessions WHERE session_id=$id ORDER BY session_id;",
            "SELECT * FROM session_metadata WHERE session_id=$id ORDER BY session_id;",
            "SELECT * FROM host_observations WHERE session_id=$id ORDER BY request_id;",
            "SELECT * FROM host_questions WHERE session_id=$id ORDER BY question_id;",
            "SELECT * FROM scoped_grants WHERE session_id=$id ORDER BY approval_id;",
            "SELECT * FROM reviewed_memory WHERE session_id=$id ORDER BY memory_id;",
            "SELECT * FROM session_file WHERE session_id=$id ORDER BY session_id;",
            "SELECT * FROM host_tasks WHERE session_id=$id AND task_id<>$control ORDER BY task_id;",
            "SELECT * FROM session_history WHERE session_id=$id AND (json_extract(projection,'$.TaskId') IS NULL OR json_extract(projection,'$.TaskId')<>$control) ORDER BY sequence;",
            "SELECT w.* FROM host_task_waits w JOIN host_tasks t ON t.task_id=w.task_id WHERE t.session_id=$id ORDER BY w.task_id;",
        })
        {
            Append(query);
            using var command = connection.CreateCommand();
            command.CommandText = query;
            command.Parameters.AddWithValue("$id", Id(session));
            command.Parameters.AddWithValue("$control", control is { } task ? Id(task) : string.Empty);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                Append("row");
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    Append(reader.IsDBNull(index) ? "null" : "value");
                    Append(Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture) ?? string.Empty);
                }
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());

        void Append(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            hash.AppendData(Encoding.ASCII.GetBytes(bytes.Length.ToString(CultureInfo.InvariantCulture) + ":"));
            hash.AppendData(bytes);
        }
    }
}
