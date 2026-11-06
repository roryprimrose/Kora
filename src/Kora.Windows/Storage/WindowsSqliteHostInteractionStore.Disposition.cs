using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Kora.Core.Authorization;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    public ValueTask<SessionDispositionPreview> PreviewStopAndDeleteAsync(SessionWorkspaceEntry session,
        ConversationCancellationTarget running, CancellationToken cancellationToken) =>
        new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(cancellationToken);
            using var connection = Open(created: false, cancellationToken);
            var target = RequireDispositionTarget(connection, session.Authority.SessionId,
                session.Authority.Generation, session.Metadata?.Revision.Value ?? 0);
            if (target != session || !target.Authority.IsActive || running.Request.SessionId != target.Authority.SessionId
                || running.Generation != target.Authority.Generation)
            { throw new InvalidOperationException("Review requires the exact Active conversation and running task."); }
            var message = ReadConversationMessage(connection, running.Request.RequestId)
                ?? throw new InvalidDataException("The running original conversation is unavailable.");
            var task = ReadTask(connection, running.Request.TaskId);
            var dispatched = message.Revision == 2 && message.Outcome is null
                && task?.State == HostTaskState.DispatchRecorded && task.Revision == running.TaskRevision;
            var stopped = message.Revision == 3 && message.Outcome == ModelTurnOutcome.Cancelled
                && message.Reason == ModelTurnReason.CallerCancelled && message.AssistantResponse is null
                && task?.State == HostTaskState.Unknown && task.Revision.Value == running.TaskRevision.Value + 1;
            if (message.Request != running.Request || message.Generation != running.Generation
                || message.Execution is null || task?.Request != running.Request
                || !(dispatched || stopped) || ReadRun(connection, running.Request.TaskId) != runId)
            { throw new InvalidOperationException("The reviewed running task is no longer the exact current execution."); }
            RequireDispositionIdle(connection, target.Authority.SessionId, null, running.Request.TaskId);
            var original = message with { Revision = 2, Outcome = null, Reason = null };
            var preview = DescribeDisposition(connection, target, null, Guid.NewGuid(), new(running, original));
            database.VerifyFiles();
            cancellationToken.ThrowIfCancellationRequested();
            return preview;
        }, cancellationToken));

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

    public ValueTask<SessionDispositionReceipt> DisposeSessionAsync(HostRequest request, SessionDispositionPreview preview,
        Func<bool> canControl, CancellationToken cancellationToken) =>
        DisposeSessionAsync(request, preview, canControl, null, cancellationToken);

    private async ValueTask<SessionDispositionReceipt> DisposeSessionAsync(HostRequest request, SessionDispositionPreview preview,
        Func<bool> canControl, SessionRetentionApplyReview? retentionReview, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(canControl);
        var receipt = await RunHostMutationAsync(request, "session.disposition", (connection, transaction, intent, audit) =>
        {
            RequireFreshMetadataIntent(intent);
            if (retentionReview is not null)
            { ApplyReviewedRetentionPolicy(connection, transaction, intent, audit, retentionReview); }
            if (request.SessionId != preview.Session.Authority.SessionId)
            {
                throw new InvalidOperationException("Disposition requires the exact previewed session ID.");
            }
            var target = RequireDispositionTarget(connection, request.SessionId, preview.Session.Authority.Generation,
                preview.Session.Metadata?.Revision.Value ?? 0);
            if (preview.StoppedWork is { } stopped)
            {
                var message = ReadConversationMessage(connection, stopped.Target.Request.RequestId);
                var task = ReadTask(connection, stopped.Target.Request.TaskId);
                var expected = stopped.Message with
                { Revision = 3, Outcome = ModelTurnOutcome.Cancelled, Reason = ModelTurnReason.CallerCancelled };
                if (message != expected || task?.Request != stopped.Target.Request
                    || task.Revision.Value != stopped.Target.TaskRevision.Value + 1
                    || task.State != expected.ExpectedTaskState || ReadRun(connection, task.Request.TaskId) != runId)
                { throw new InvalidOperationException("Only the reviewed, physically joined cancellation may be disposed. Review again."); }
            }
            RequireDispositionIdle(connection, request.SessionId, request.TaskId, preview.StoppedWork?.Target.Request.TaskId);
            if (preview.ConfirmationId == Guid.Empty
                || retentionReview is null
                    && preview != DescribeDisposition(connection, target, request.TaskId, preview.ConfirmationId, preview.StoppedWork))
            {
                throw new InvalidOperationException("Session records changed after preview. Obtain a fresh preview and confirmation.");
            }
            var removed = new WorkSessionAuthorization(request.SessionId,
                new(checked(target.Authority.Generation.Value + 1)), false);
            var completed = intent.Next(HostTaskState.Succeeded);
            var memories = MemoryInvalidations(connection, request.SessionId);
            var attachments = AttachmentInvalidation(connection, request.SessionId);
            database.RequireEmptyArtifactInventory();
            Execute(connection, transaction, "PRAGMA secure_delete=ON; PRAGMA journal_size_limit=0;");
            var sequence = AppendAudit(connection, transaction, intent, removed, audit,
                changes: [SessionChange(removed, 2), TaskChange(completed), .. memories.Select(MemoryChange), .. attachments]);
            RevokeAttachment(connection, transaction, request.SessionId, sequence);
            WriteSession(connection, transaction, removed, 2, sequence);
            foreach (var memory in memories) { WriteMemory(connection, transaction, memory, sequence); }
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, completed);
            if (retentionReview is not null)
            { CompleteRetentionApplication(connection, transaction, intent, removed, audit); }
            SessionHistoryPersistence.Redact(connection, transaction, request.SessionId);
            RemoveConversation(connection, transaction, request.SessionId);
            Execute(connection, transaction, """
                DELETE FROM host_task_waits WHERE task_id IN (SELECT task_id FROM host_tasks WHERE session_id=$id);
                DELETE FROM host_observations WHERE session_id=$id;
                DELETE FROM host_questions WHERE session_id=$id;
                DELETE FROM scoped_grants WHERE session_id=$id;
                DELETE FROM session_metadata WHERE session_id=$id;
                DELETE FROM session_provider_profile WHERE session_id=$id;
                """, ("$id", Id(request.SessionId)));
            return new SessionDispositionReceipt(request.SessionId, removed.Generation, preview);
        }, cancellationToken, canControl, requireIdle: preview.StoppedWork is null).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                using var lease = database.AcquireReadLease(CancellationToken.None);
                database.ClearCommittedJournal();
                database.RequireEmptyArtifactInventory();
                using (var connection = Open(created: false, CancellationToken.None))
                {
                    RebuildRetiredContent(connection);
                }
                database.ClearCommittedJournal();
                database.RequireEmptyArtifactInventory();
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or SqliteException)
        {
            throw new IOException("Session disposition committed, but owned-copy removal acceptance is unconfirmed. "
                + "Inspect durable authority and storage recovery before any fresh review; do not infer rollback or retry automatically.", exception);
        }
        return receipt;
    }

    private static void RebuildRetiredContent(SqliteConnection connection)
    {
        // secure_delete cannot retroactively clear earlier free space. Open keeps the rebuild in memory.
        // Revocation has committed; caller cancellation cannot imply rollback or interrupt copy acceptance.
        RestrictedSqliteDatabase.ResetProgressBudget(connection, CancellationToken.None);
        using var cleanup = connection.CreateCommand();
        cleanup.CommandText = "PRAGMA secure_delete=ON; PRAGMA journal_size_limit=0; VACUUM;";
        cleanup.ExecuteNonQuery();
    }

    private static SessionDispositionPreview DescribeDisposition(SqliteConnection connection,
        SessionWorkspaceEntry target, HostId<TaskIdentity>? control, Guid confirmation, SessionStoppedWorkReview? stopped = null)
        => DescribeDispositionInventory(connection, target, confirmation,
            DispositionRevision(connection, target.Authority.SessionId, control, stopped)) with { StoppedWork = stopped };

    private static SessionDispositionPreview DescribeDispositionInventory(SqliteConnection connection,
        SessionWorkspaceEntry target, Guid confirmation, string revision)
    {
        var session = target.Authority.SessionId;
        return new(confirmation, target, revision,
            CountDispositionRows(connection, "host_questions", session), CountDispositionRows(connection, "scoped_grants", session),
            CountDispositionRows(connection, "host_observations", session), CountDispositionRows(connection, "host_task_waits", session))
        {
            Attachments = ReadAttachmentRow(connection, session)?.Attachment is null ? 0 : 1,
            ProviderProfiles = CountDispositionRows(connection, "session_provider_profile", session),
            ConversationMessages = CountDispositionRows(connection, "conversation_messages", session),
            SavedConversationDrafts = CountDispositionRows(connection, "conversation_drafts", session),
        };
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
        HostId<TaskIdentity>? control, HostId<TaskIdentity>? reviewedReplacementIntent = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM host_tasks WHERE session_id=$id AND state IN (0,1,7) AND task_id<>$control
                AND task_id<>$replacement
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", Id(session));
        command.Parameters.AddWithValue("$control", control is { } task ? Id(task) : string.Empty);
        command.Parameters.AddWithValue("$replacement", reviewedReplacementIntent is { } replacement ? Id(replacement) : string.Empty);
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
        HostId<TaskIdentity>? control, SessionStoppedWorkReview? stopped = null)
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
            "SELECT * FROM session_permission WHERE session_id=$id ORDER BY session_id;",
            "SELECT * FROM session_admissions WHERE session_id=$id ORDER BY admission_id;",
            "SELECT * FROM reviewed_memory WHERE session_id=$id ORDER BY memory_id;",
            "SELECT * FROM session_file WHERE session_id=$id ORDER BY session_id;",
            "SELECT * FROM session_provider_profile WHERE session_id=$id ORDER BY session_id;",
            "SELECT * FROM conversation_drafts WHERE session_id=$id ORDER BY session_id;",
            "SELECT * FROM conversation_answer_reviews WHERE session_id=$id ORDER BY request_id;",
            "SELECT * FROM conversation_messages WHERE session_id=$id AND request_id<>$stoppedRequest ORDER BY sequence;",
            "SELECT * FROM session_queue WHERE session_id=$id ORDER BY task_id;",
            "SELECT * FROM session_queue_order WHERE session_id=$id ORDER BY order_id;",
            "SELECT * FROM host_tasks WHERE session_id=$id AND task_id<>$control AND task_id<>$stoppedTask ORDER BY task_id;",
            """
            SELECT * FROM session_history WHERE session_id=$id
                AND (json_extract(projection,'$.TaskId') IS NULL OR json_extract(projection,'$.TaskId')<>$control)
                AND NOT (coalesce(json_extract(projection,'$.TaskId'),'')=$stoppedTask AND $stoppedTask<>''
                    AND json_extract(projection,'$.Kind')=$taskKind
                    AND json_extract(projection,'$.SourceRevision')=$stoppedRevision
                    AND json_extract(projection,'$.TaskState')=$unknownState)
                ORDER BY sequence;
            """,
            "SELECT w.* FROM host_task_waits w JOIN host_tasks t ON t.task_id=w.task_id WHERE t.session_id=$id ORDER BY w.task_id;",
        })
        {
            Append(query);
            using var command = connection.CreateCommand();
            command.CommandText = query;
            command.Parameters.AddWithValue("$id", Id(session));
            command.Parameters.AddWithValue("$control", control is { } task ? Id(task) : string.Empty);
            command.Parameters.AddWithValue("$stoppedTask", stopped is null ? string.Empty : Id(stopped.Target.Request.TaskId));
            command.Parameters.AddWithValue("$stoppedRequest", stopped is null ? string.Empty : Id(stopped.Target.Request.RequestId));
            command.Parameters.AddWithValue("$taskKind", (int)SessionHistoryKind.Task);
            command.Parameters.AddWithValue("$stoppedRevision", stopped is null ? 0 : stopped.Target.TaskRevision.Value + 1);
            command.Parameters.AddWithValue("$unknownState", (int)HostTaskState.Unknown);
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
