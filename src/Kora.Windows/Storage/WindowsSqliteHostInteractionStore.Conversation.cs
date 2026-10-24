using System.Collections.Immutable;

using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore : ISessionConversationStore
{
    public ValueTask<HostTaskRecord> RecordConversationCredentialRejectionAsync(HostRequest request,
        HostRevision generation, Func<bool> admitted, CancellationToken token)
    {
        RequireLive(request);
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            var session = RequireSession(connection, request.SessionId);
            var existing = ReadTask(connection, request.TaskId);
            if (request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice) || request.InvocationId is not null || session.State != 0
                || session.Authority.Generation != generation || !admitted()
                || existing is not null && (existing.Request != request || existing.Revision.Value != 1
                    || existing.State != HostTaskState.IntentRecorded)
                || ReadConversationMessage(connection, request.RequestId) is not null)
            { throw new InvalidOperationException("Credential rejection requires a fresh exact native request, generation and private admission."); }
            var intent = existing ?? new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
            var terminal = intent.Next(HostTaskState.Denied);
            var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
                "conversation.credential-rejected", SecurityAuditOutcome.Denied,
                request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand,
                Id(request.TaskId), reasonCode: "credential-like-content");
            using var activity = HostActivity.BeginAudit(request, audit);
            using var transaction = connection.BeginTransaction();
            if (existing is null)
            {
                WindowsSqliteHostTaskStore.WriteTask(connection, transaction, intent);
                Execute(connection, transaction, "INSERT INTO host_task_runs VALUES($task,$run);",
                    ("$task", Id(request.TaskId)), ("$run", runId.ToString("D")));
            }
            AppendAudit(connection, transaction, intent, session.Authority, audit,
                changes: [TaskChange(terminal)], requestedAudit: ConversationRequestedAudit(audit));
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, terminal);
            Commit(transaction, request, token, admitted);
            activity.Complete(HostOperationOutcome.Completed);
            return terminal;
        }, token));
    }
    private void MigrateConversation(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        ValidateConsolidatedAuthority(connection);
        SessionHistoryPersistence.Validate(connection);
        ValidateQueue(connection);
        ValidateQueueOrders(connection);
        ValidateRetention(connection);
        ValidateMemory(connection);
        ValidateAttachments(connection);
        ValidateProviderProfiles(connection);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1 FROM security_audit_events a,json_each(a.envelope,'$.Changes') c
            WHERE json_extract(c.value,'$.Kind') IN ('conversation-draft','conversation-message') LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null)
        { throw new InvalidDataException("Missing audited conversation authority cannot be replaced by an empty migration."); }
        token.ThrowIfCancellationRequested();
        foreach (var table in HostInteractionSchema.LegacyConversationSchema) { Execute(connection, transaction, table); }
        checkpoint?.BeforeCommit(connection, transaction);
    }

    public ValueTask<ConversationDraftSnapshot> ReadConversationDraftAsync(HostId<SessionIdentity> session, CancellationToken token) =>
        new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            var authority = RequireSession(connection, session);
            if (authority.State == 2) { throw new InvalidOperationException("Removed conversation content is unavailable."); }
            var result = new ConversationDraftSnapshot(authority.Authority, ReadConversationDraft(connection, session));
            database.VerifyFiles();
            token.ThrowIfCancellationRequested();
            return result;
        }, token));

    public ValueTask<ConversationMessage> ReadConversationMessageAsync(HostId<SessionIdentity> session,
        HostId<RequestIdentity> request, CancellationToken token) =>
        new(Task.Run(() =>
        {
            session.Validate();
            request.Validate();
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            using var transaction = connection.BeginTransaction();
            var authority = RequireSession(connection, session);
            if (authority.State == 2) { throw new InvalidOperationException("Removed conversation content is unavailable."); }
            var message = ReadConversationMessage(connection, request)
                ?? throw new InvalidOperationException("The exact retained conversation record is unavailable.");
            if (message.Request.SessionId != session || message.Generation.Value > authority.Authority.Generation.Value)
            { throw new InvalidDataException("Conversation detail ownership does not match the exact retained session."); }
            database.VerifyFiles();
            token.ThrowIfCancellationRequested();
            return message;
        }, token));

    public async ValueTask<ConversationDraftSnapshot> CommitConversationDraftAsync(HostRequest control,
        ConversationDraftSnapshot expected, string? body, Func<bool> admitted, CancellationToken token)
    {
        expected.Draft.Validate();
        if (body is not null)
        {
            try { ConversationText.Validate(body); }
            catch (ConversationCredentialException)
            {
                await RecordConversationCredentialRejectionAsync(control, expected.Session.Generation, admitted, token).ConfigureAwait(false);
                throw;
            }
        }
        var saved = await RunHostMutationAsync(control, body is null ? "conversation.draft.clear" : "conversation.draft.save",
            (connection, transaction, intent, audit) =>
            {
                RequireFreshMetadataIntent(intent);
                var session = RequireSession(connection, control.SessionId);
                var previous = ReadConversationDraft(connection, control.SessionId);
                if (control.Origin != RequestOrigin.LocalUi || control.InvocationId is not null || session.State != 0
                    || session.Authority != expected.Session || previous != expected.Draft
                    || expected.ExpectedTarget is { } target
                        && target != new SessionWorkspaceEntry(session.Authority, ReadMetadata(connection, control.SessionId))
                    || expected.Draft.SessionId != control.SessionId || body is null && previous.Body is null)
                { throw new ConversationDraftConflictException(); }
                database.RequireEmptyArtifactInventory();
                Execute(connection, transaction, "PRAGMA secure_delete=ON; PRAGMA journal_size_limit=0;");
                var draft = new SavedConversationDraft(control.SessionId, checked(previous.Revision + 1), body);
                var completed = intent.Next(HostTaskState.Succeeded);
                var sequence = AppendAudit(connection, transaction, intent, session.Authority, audit,
                    changes: [DraftChange(draft), TaskChange(completed)], requestedAudit: ConversationRequestedAudit(audit));
                Execute(connection, transaction, """
                    INSERT INTO conversation_drafts VALUES($id,$revision,$payload,$audit)
                    ON CONFLICT(session_id) DO UPDATE SET revision=excluded.revision,payload=excluded.payload,audit_sequence=excluded.audit_sequence;
                    """, ("$id", Id(control.SessionId)), ("$revision", draft.Revision),
                    ("$payload", HostInteractionCodec.Encode(draft)), ("$audit", sequence));
                WindowsSqliteHostTaskStore.WriteTask(connection, transaction, completed);
                return new ConversationDraftSnapshot(session.Authority, draft) { ExpectedTarget = expected.ExpectedTarget };
            }, token, admitted, requireIdle: false).ConfigureAwait(false);
        using var lease = database.AcquireReadLease(CancellationToken.None);
        database.ClearCommittedJournal();
        database.RequireEmptyArtifactInventory();
        using var readback = Open(created: false, CancellationToken.None);
        if (ReadConversationDraft(readback, control.SessionId) != saved.Draft)
        { throw new IOException("Saved draft commit readback is unconfirmed; inspect exact revision before another decision."); }
        return saved;
    }

    public async ValueTask<ConversationSubmissionReceipt> RetainConversationMessageAsync(HostRequest request,
        HostRevision generation, long expectedMetadataRevision, string body, Func<bool> admitted, CancellationToken token)
    {
        try { ConversationText.Validate(body, submission: true); }
        catch (ConversationCredentialException)
        {
            await RecordConversationCredentialRejectionAsync(request, generation, admitted, token).ConfigureAwait(false);
            throw;
        }
        RequireLive(request);
        return await Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            var intent = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
            var session = RequireSession(connection, request.SessionId);
            if (request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice) || request.InvocationId is not null || session.State != 0
                || session.Authority.Generation != generation
                || (ReadMetadata(connection, request.SessionId)?.Revision.Value ?? 0) != expectedMetadataRevision || !admitted())
            { throw new InvalidOperationException("Submission requires the exact existing Active session and current private generation."); }
            var duplicate = ReadConversationMessage(connection, request.RequestId);
            if (duplicate is not null)
            {
                if (duplicate.Request != request || duplicate.Generation != generation || !Same(duplicate.Body, body))
                { throw new InvalidOperationException("This submission identity already belongs to different immutable content."); }
                return new ConversationSubmissionReceipt(duplicate, ReadTask(connection, request.TaskId)!);
            }
            RequireDispositionIdle(connection, request.SessionId, request.TaskId);
            if (ReadTask(connection, request.TaskId) is not null)
            { throw new InvalidOperationException("A fresh host task identity is required for this original submission."); }
            var count = Scalar(connection, "SELECT count(*) FROM conversation_messages WHERE session_id=$id;", request.SessionId);
            if (count >= ConversationText.MaximumSessionMessages)
            { throw new ConversationCapacityException(); }
            var message = new ConversationMessage(request, generation, count + 1, 1, time.GetUtcNow().ToUniversalTime(),
                body, null, null);
            var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval, "conversation.submit",
                SecurityAuditOutcome.Succeeded,
                request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand,
                Id(request.TaskId));
            using var activity = HostActivity.BeginAudit(request, audit);
            using var transaction = connection.BeginTransaction();
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, intent);
            Execute(connection, transaction, "INSERT INTO host_task_runs VALUES($task,$run);",
                ("$task", Id(request.TaskId)), ("$run", runId.ToString("D")));
            var sequence = AppendAudit(connection, transaction, intent, session.Authority, audit,
                changes: [MessageChange(message), TaskChange(intent)], requestedAudit: ConversationRequestedAudit(audit));
            WriteConversationMessage(connection, transaction, message, sequence);
            TouchActivity(connection, transaction, request.SessionId);
            Commit(transaction, request, token, admitted);
            database.VerifyFiles();
            if (ReadConversationMessage(connection, request.RequestId) != message)
            { throw new IOException("Original submission readback is unconfirmed; inspect the exact request without replay."); }
            activity.Complete(HostOperationOutcome.Completed);
            return new ConversationSubmissionReceipt(message, intent);
        }, token).ConfigureAwait(false);
    }

    public ValueTask<ConversationSubmissionReceipt> CompleteConversationSubmissionAsync(HostRequest request,
        ModelTurnOutcome outcome, ModelTurnReason reason, Func<bool> admitted, CancellationToken token)
    {
        if (!Enum.IsDefined(outcome) || !Enum.IsDefined(reason) || outcome == ModelTurnOutcome.Succeeded)
        { throw new InvalidOperationException("This conversation increment has no qualified assistant-generation or dispatch route."); }
        return RunHostMutationAsync(request, "conversation.terminal", (connection, transaction, intent, audit) =>
        {
            var previous = ReadConversationMessage(connection, request.RequestId)
                ?? throw new InvalidDataException("The exact retained original submission is missing.");
            var session = RequireSession(connection, request.SessionId);
            if (previous.Request != request || previous.Revision != 1 || session.State != 0
                || session.Authority.Generation != previous.Generation || intent.State != HostTaskState.IntentRecorded)
            { throw new InvalidOperationException("The retained request, task, session or terminal outcome conflicts; inspect without replay."); }
            var message = previous with { Revision = 2, Outcome = outcome, Reason = reason };
            var completed = intent.Next(message.ExpectedTaskState);
            var terminalAudit = new SecurityAuditEvent(audit.CorrelationId, audit.Category, audit.ActionId,
                outcome == ModelTurnOutcome.Cancelled ? SecurityAuditOutcome.Cancelled
                    : outcome == ModelTurnOutcome.Unknown ? SecurityAuditOutcome.Unknown : SecurityAuditOutcome.Denied,
                audit.Initiator, audit.TargetId, reasonCode: reason.ToString().ToLowerInvariant());
            var sequence = AppendAudit(connection, transaction, intent, session.Authority, terminalAudit,
                changes: [MessageChange(message), TaskChange(completed)], requestedAudit: ConversationRequestedAudit(terminalAudit));
            WriteConversationMessage(connection, transaction, message, sequence);
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, completed);
            TouchActivity(connection, transaction, request.SessionId);
            return new ConversationSubmissionReceipt(message, completed);
        }, token, admitted, requireIdle: false);
    }

    public ValueTask<ConversationPage> ReadConversationPageAsync(HostId<SessionIdentity> session,
        ConversationCursor? cursor, int limit, CancellationToken token)
    {
        if (limit is <= 0 or > ConversationText.MaximumPageRecords) { throw new ArgumentOutOfRangeException(nameof(limit)); }
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            using var transaction = connection.BeginTransaction();
            var sessionRow = RequireSession(connection, session);
            if (sessionRow.State == 2) { throw new InvalidOperationException("Removed conversation content is unavailable, not an empty readable history."); }
            var authority = sessionRow.Authority;
            var head = Scalar(connection, "SELECT coalesce(sum(revision),0) FROM conversation_messages WHERE session_id=$id;", session);
            var currentCeiling = Scalar(connection, "SELECT count(*) FROM conversation_messages WHERE session_id=$id;", session);
            if (cursor is not null && (cursor.SessionId != session || cursor.Generation != authority.Generation
                || cursor.Ceiling < 0 || cursor.Ceiling > currentCeiling || cursor.After < 0 || cursor.After >= cursor.Ceiling
                || cursor.HeadRevision <= 0 || cursor.HeadRevision > head))
            { throw new InvalidOperationException("Conversation continuation requires its exact session, generation and original sequence ceiling."); }
            if (cursor is not null)
            {
                using var snapshot = connection.CreateCommand();
                snapshot.Transaction = transaction;
                snapshot.CommandText = "SELECT coalesce(sum(revision),0) FROM conversation_messages WHERE session_id=$id AND sequence<=$ceiling;";
                snapshot.Parameters.AddWithValue("$id", Id(session));
                snapshot.Parameters.AddWithValue("$ceiling", cursor.Ceiling);
                if (snapshot.ExecuteScalar() is not long revision || revision != cursor.HeadRevision)
                { throw new ConversationSnapshotChangedException(); }
            }
            var ceiling = cursor?.Ceiling ?? currentCeiling;
            var after = cursor?.After ?? 0;
            var headRevision = cursor?.HeadRevision ?? head;
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT payload,audit_sequence FROM conversation_messages
                WHERE session_id=$id AND sequence>$after AND sequence<=$ceiling ORDER BY sequence LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$id", Id(session));
            command.Parameters.AddWithValue("$after", after);
            command.Parameters.AddWithValue("$ceiling", ceiling);
            command.Parameters.AddWithValue("$limit", limit);
            var rows = ImmutableArray.CreateBuilder<ConversationMessage>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                var message = DecodeConversationMessage(connection, reader.GetString(0), reader.GetInt64(1));
                var candidate = rows.ToImmutable().Add(message);
                var next = message.Sequence < ceiling ? new ConversationCursor(session, authority.Generation, ceiling, message.Sequence, headRevision) : null;
                if (ConversationPage.Serialize(new(authority, headRevision, ceiling, candidate, next)).Length > ConversationText.MaximumPageUtf8Bytes)
                { break; }
                rows.Add(message);
            }
            var last = rows.Count == 0 ? after : rows[^1].Sequence;
            var continuation = last < ceiling ? new ConversationCursor(session, authority.Generation, ceiling, last, headRevision) : null;
            var page = new ConversationPage(authority, headRevision, ceiling, rows.ToImmutable(), continuation);
            database.VerifyFiles();
            token.ThrowIfCancellationRequested();
            return page;
        }, token));
    }

    private static long Scalar(SqliteConnection connection, string sql, HostId<SessionIdentity>? session = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (session is { } id) { command.Parameters.AddWithValue("$id", Id(id)); }
        return (long)(command.ExecuteScalar() ?? throw new InvalidDataException("Conversation authority count is missing."));
    }

    private static SecurityAuditEvent ConversationRequestedAudit(SecurityAuditEvent terminal) =>
        new(terminal.CorrelationId, terminal.Category, terminal.ActionId, SecurityAuditOutcome.Requested,
            terminal.Initiator, terminal.TargetId);

    private static AuthorityChange DraftChange(SavedConversationDraft draft) =>
        Change("conversation-draft", Id(draft.SessionId), draft.Revision, HostInteractionCodec.Encode(draft));
    private static AuthorityChange MessageChange(ConversationMessage message) =>
        Change("conversation-message", Id(message.Request.RequestId), message.Revision, HostInteractionCodec.Encode(message));

    private static SavedConversationDraft ReadConversationDraft(SqliteConnection connection, HostId<SessionIdentity> session)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT revision,payload,audit_sequence FROM conversation_drafts WHERE session_id=$id;";
        command.Parameters.AddWithValue("$id", Id(session));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) { return new(session, 0, null); }
        var payload = reader.GetString(1);
        var draft = HostInteractionCodec.Decode<SavedConversationDraft>(payload);
        try { draft.Validate(); }
        catch (ConversationCredentialException exception)
        { throw new InvalidDataException("Stored draft has credential-like content; explicit storage recovery is required, not empty replacement.", exception); }
        if (draft.SessionId != session || draft.Revision != reader.GetInt64(0) || draft.Revision == 0)
        { throw new InvalidDataException("Saved draft identity or revision conflicts with its exact database cell."); }
        if (!Same(payload, HostInteractionCodec.Encode(draft)))
        { throw new InvalidDataException("Saved draft source bytes or fields are not canonical."); }
        ValidateRowAuthority(connection, reader.GetInt64(2), DraftChange(draft));
        return draft;
    }

    private static ConversationMessage? ReadConversationMessage(SqliteConnection connection, HostId<RequestIdentity> request)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload,audit_sequence FROM conversation_messages WHERE request_id=$id;";
        command.Parameters.AddWithValue("$id", Id(request));
        using var reader = command.ExecuteReader();
        return reader.Read() ? DecodeConversationMessage(connection, reader.GetString(0), reader.GetInt64(1)) : null;
    }

    private static ConversationMessage DecodeConversationMessage(SqliteConnection connection, string payload, long sequence)
    {
        var message = HostInteractionCodec.Decode<ConversationMessage>(payload);
        try { message.Validate(); }
        catch (ConversationCredentialException exception)
        { throw new InvalidDataException("Stored conversation has credential-like content; explicit storage recovery is required, not empty replacement.", exception); }
        if (!Same(payload, HostInteractionCodec.Encode(message)))
        { throw new InvalidDataException("Conversation source bytes are not canonical."); }
        ValidateRowAuthority(connection, sequence, MessageChange(message));
        var task = ReadTask(connection, message.Request.TaskId);
        if (task is null || task.Request != message.Request)
        { throw new InvalidDataException("Conversation body has no exact original task lineage."); }
        if (message.Outcome is not null)
        {
            if (task.State != message.ExpectedTaskState)
            { throw new InvalidDataException("Conversation terminal host status conflicts with the original task receipt."); }
            ValidateRowAuthority(connection, sequence, TaskChange(task));
        }
        return message;
    }

    private static void WriteConversationMessage(SqliteConnection connection, SqliteTransaction transaction,
        ConversationMessage message, long sequence) =>
        Execute(connection, transaction, """
            INSERT INTO conversation_messages VALUES($request,$session,$sequence,$revision,$payload,$audit)
            ON CONFLICT(request_id) DO UPDATE SET revision=excluded.revision,payload=excluded.payload,audit_sequence=excluded.audit_sequence;
            """, ("$request", Id(message.Request.RequestId)), ("$session", Id(message.Request.SessionId)),
            ("$sequence", message.Sequence), ("$revision", message.Revision),
            ("$payload", HostInteractionCodec.Encode(message)), ("$audit", sequence));

    private void ObserveConversationRecovery(SqliteConnection connection, SqliteTransaction transaction, HostTaskRecord task)
    {
        if (task.State is not (HostTaskState.Interrupted or HostTaskState.Unknown)) { return; }
        var message = ReadConversationMessage(connection, task.Request.RequestId);
        if (message is null || message.Outcome is not null) { return; }
        var recovered = message with
        {
            Revision = checked(message.Revision + 1),
            Outcome = ModelTurnOutcome.Unknown,
            Reason = task.State == HostTaskState.Interrupted ? ModelTurnReason.HostInterrupted : ModelTurnReason.ProviderOutcome,
        };
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval, "conversation.recovery",
            task.State == HostTaskState.Unknown ? SecurityAuditOutcome.Unknown : SecurityAuditOutcome.Denied,
            SecurityAuditInitiator.System, Id(task.Request.TaskId),
            reasonCode: recovered.Reason.ToString()!.ToLowerInvariant());
        using var activity = HostActivity.BeginAudit(task.Request, audit);
        var session = RequireSession(connection, task.Request.SessionId).Authority;
        var sequence = AppendAudit(connection, transaction, task, session, audit,
            changes: [MessageChange(recovered), TaskChange(task)]);
        WriteConversationMessage(connection, transaction, recovered, sequence);
        activity.Complete(HostOperationOutcome.Completed);
    }

    private HostTaskObservation? ObserveConversationTask(SqliteConnection connection, HostTaskRecord task,
        Kora.Core.Authorization.WorkSessionAuthorization session)
    {
        var message = ReadConversationMessage(connection, task.Request.RequestId);
        if (message is null) { return null; }
        if (message.Request != task.Request)
        { throw new InvalidDataException("Conversation task source does not match its immutable retained original request."); }
        using var blockers = connection.CreateCommand();
        blockers.CommandText = """
            SELECT 1 FROM host_tasks WHERE session_id=$session AND task_id<>$task AND state IN (0,1,7) LIMIT 1;
            """;
        blockers.Parameters.AddWithValue("$session", Id(task.Request.SessionId));
        blockers.Parameters.AddWithValue("$task", Id(task.Request.TaskId));
        return new(task, session.Generation, "conversation.user-request.v1",
            session.IsActive && message.Generation == session.Generation && message.Outcome is null
                && task.State == message.ExpectedTaskState
                && task.Revision.Value == message.Revision
                && ReadRun(connection, task.Request.TaskId) == runId && blockers.ExecuteScalar() is null
                && ConversationQuestionsPermitObservation(connection, task, message, session), null);
    }

    private static void RemoveConversation(SqliteConnection connection, SqliteTransaction transaction, HostId<SessionIdentity> session) =>
        Execute(connection, transaction, """
            DELETE FROM conversation_drafts WHERE session_id=$id;
            DELETE FROM conversation_messages WHERE session_id=$id;
            """, ("$id", Id(session)));

    private static void ValidateConversation(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        // Inventory is metadata only: profiles have no session/body-count quota. Decode body/digest only at the exact read.
        command.CommandText = """
            SELECT 1 FROM conversation_messages m LEFT JOIN host_tasks t
                ON t.task_id=json_extract(m.payload,'$.Request.TaskId.Value')
            WHERE json_valid(m.payload)=0 OR json_extract(m.payload,'$.Request.RequestId.Value') IS NOT m.request_id
                OR json_extract(m.payload,'$.Request.SessionId.Value') IS NOT m.session_id
                OR json_extract(m.payload,'$.Sequence') IS NOT m.sequence
                OR json_extract(m.payload,'$.Revision') IS NOT m.revision
                OR t.request_id IS NOT m.request_id OR t.session_id IS NOT m.session_id
                OR json_extract(m.payload,'$.Outcome') IS NOT NULL AND t.state IN (0,1)
            UNION ALL
            SELECT 1 FROM conversation_drafts d
            WHERE json_valid(d.payload)=0 OR json_extract(d.payload,'$.SessionId.Value') IS NOT d.session_id
                OR json_extract(d.payload,'$.Revision') IS NOT d.revision
            UNION ALL
            SELECT 1 FROM conversation_messages GROUP BY session_id HAVING count(*)<>max(sequence)
            LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null)
        { throw new InvalidDataException("Conversation source identity, sequence, revision or task lineage is inconsistent."); }
        command.CommandText = """
            SELECT json_extract(c.value,'$.Kind'),json_extract(c.value,'$.Id'),max(json_extract(c.value,'$.Revision'))
            FROM security_audit_events a,json_each(a.envelope,'$.Changes') c
            WHERE json_extract(c.value,'$.Kind') IN ('conversation-draft','conversation-message')
                AND json_extract(a.envelope,'$.Request.SessionId.Value') IN (SELECT session_id FROM work_sessions WHERE state<>2)
            GROUP BY 1,2;
            """;
        using var commits = command.ExecuteReader();
        while (commits.Read())
        {
            using var row = connection.CreateCommand();
            row.CommandText = Same(commits.GetString(0), "conversation-draft")
                ? "SELECT revision FROM conversation_drafts WHERE session_id=$id;"
                : "SELECT revision FROM conversation_messages WHERE request_id=$id;";
            row.Parameters.AddWithValue("$id", Id(new HostId<SessionIdentity>(ParseId(commits.GetString(1)))));
            var revision = row.ExecuteScalar() as long? ?? 0;
            if (revision != commits.GetInt64(2))
            { throw new InvalidDataException("Audited conversation content is missing or downgraded; explicit storage recovery is required."); }
        }
    }
}
