using System.Security.Cryptography;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    public ValueTask<SessionFileAttachment> Replace(HostRequest request, SessionFileRemoval previous,
        LocalFileRevision revision, ReadOnlyMemory<byte> originalBytes, Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(admitted);
        RequireLive(request);
        return new(Task.Run(() =>
        {
            var requested = ReplacementAudit(request, SecurityAuditOutcome.Requested);
            using var operation = HostActivity.BeginAudit(request, requested);
            try
            {
                var result = tasks.WithCommittedIdleIntent(request, (connection, intent) =>
                {
                    ValidateAuthority(connection);
                    RequireFreshMetadataIntent(intent);
                    var old = RequireReplacementTarget(connection, request, previous, revision, admitted);
                    var validated = LocalFileRevision.Restore(revision.Review, originalBytes.Span, revision.AdmittedAt, revision.Reference);
                    if (!Same(validated.Text, revision.Text))
                    {
                        throw new InvalidDataException("The replacement projection must match authoritative original bytes.");
                    }
                    // A content-free requested receipt is durable before attempting the swap. If
                    // COMMIT is uncertain, even a still-old row is held for exact native inspection.
                    using (var preflight = connection.BeginTransaction())
                    {
                        AppendAudit(connection, preflight, intent, RequireSession(connection, request.SessionId).Authority, requested);
                        Commit(preflight, request, token, admitted);
                    }
                    var storageRevision = checked(old.Revision + 1);
                    var descriptor = new AttachmentData(1, ReadMemoryProfile(connection), request.SessionId, previous.Generation,
                        revision.Reference, revision.Review.ReviewId, revision.Review.Request, revision.Review.Metadata,
                        revision.AdmittedAt, SessionFileAttachment.Projection, AttachmentMediaType(revision.Review.Metadata.CanonicalPath));
                    try
                    {
                        var swapping = ReplacementAudit(request, SecurityAuditOutcome.Requested);
                        using var swap = HostActivity.BeginAudit(request, swapping);
                        using var transaction = connection.BeginTransaction();
                        _ = RequireReplacementTarget(connection, request, previous, revision, admitted, ownPending: true);
                        Execute(connection, transaction, "PRAGMA secure_delete=ON; PRAGMA journal_size_limit=0;");
                        var sequence = AppendAudit(connection, transaction, intent,
                            RequireSession(connection, request.SessionId).Authority, swapping,
                            changes: [AttachmentChange(descriptor, storageRevision)]);
                        var bytes = originalBytes.ToArray();
                        try { WriteAttachment(connection, transaction, descriptor, storageRevision, bytes, sequence); }
                        finally { CryptographicOperations.ZeroMemory(bytes); }
                        Commit(transaction, request, token, admitted);
                        swap.Complete(HostOperationOutcome.Completed);
                    }
                    catch (Exception exception) when (exception is not (InteractionCommitUncertainException or SqliteException))
                    {
                        // The transaction has rolled back. This terminal receipt certifies only
                        // that the old durable record remains old, never that it was refreshed.
                        var failed = ReplacementAudit(request, SecurityAuditOutcome.Failed);
                        using var failure = HostActivity.BeginAudit(request, failed);
                        using var transaction = connection.BeginTransaction();
                        AppendAudit(connection, transaction, intent, RequireSession(connection, request.SessionId).Authority, failed);
                        Commit(transaction, request, CancellationToken.None);
                        failure.Complete(HostOperationOutcome.Failed);
                        throw;
                    }
                    try
                    {
                        // Keep the task/database lease through connection release and both phases.
                        connection.Close();
                        checkpoint?.BeforeAttachmentCopyVerification();
                        database.ClearCommittedJournal();
                        database.RequireEmptyArtifactInventory();
                        using var certified = database.Open(created: false, token);
                        ValidateAuthority(certified);
                        var row = ReadAttachmentRow(certified, request.SessionId)
                            ?? throw new InvalidDataException("The committed replacement lost its exact row.");
                        if (!row.CopyVerificationPending || row.Data.Reference != revision.Reference)
                        {
                            throw new InvalidDataException("Copy certification does not own the captured revision.");
                        }
                        var terminal = ReplacementAudit(request, SecurityAuditOutcome.Succeeded);
                        using var certification = HostActivity.BeginAudit(request, terminal);
                        using var transaction = certified.BeginTransaction();
                        var completed = intent.Next(HostTaskState.Succeeded);
                        var sequence = AppendAudit(certified, transaction, intent,
                            RequireSession(certified, request.SessionId).Authority, terminal,
                            changes: [AttachmentChange(row.Data, row.Revision), TaskChange(completed)]);
                        Execute(certified, transaction, "UPDATE session_file SET audit_sequence=$audit WHERE session_id=$id;",
                            ("$audit", sequence), ("$id", Id(request.SessionId)));
                        WindowsSqliteHostTaskStore.WriteTask(certified, transaction, completed);
                        TouchActivity(certified, transaction, request.SessionId);
                        Commit(transaction, request, token, admitted);
                        certification.Complete(HostOperationOutcome.Completed);
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
                    {
                        // Generic control bookkeeping must not mark a committed, held swap Denied.
                        throw new IOException("The swap committed but certification is held. Inspect exact copy recovery.", exception);
                    }
                    return new SessionFileAttachment(request.SessionId, previous.Generation, new(storageRevision), validated);
                }, token);
                operation.Complete(HostOperationOutcome.Completed);
                return result;
            }
            catch (OperationCanceledException)
            {
                operation.Complete(HostOperationOutcome.Cancelled);
                throw;
            }
            catch
            {
                operation.Complete(HostOperationOutcome.Failed);
                throw;
            }
        }, token));
    }

    private AttachmentRow RequireReplacementTarget(SqliteConnection connection, HostRequest request,
        SessionFileRemoval previous, LocalFileRevision revision, Func<bool> admitted, bool ownPending = false)
    {
        _ = RequireInspectionIdentity();
        var owner = RequireSession(connection, request.SessionId);
        RequireAttachmentReadable(connection, request.SessionId);
        RequireDispositionIdle(connection, request.SessionId, request.TaskId);
        database.RequireEmptyArtifactInventory();
        var old = ReadAttachmentRow(connection, request.SessionId);
        if (request.Origin != RequestOrigin.LocalUi || request.InvocationId is not null
            || previous.ConfirmationId == Guid.Empty || previous.Session != request.SessionId
            || !previous.BodyRetained || previous.ReplacementCopyVerificationPending || previous.ReplacementSwapUnconfirmed
            || !owner.Authority.IsActive || owner.Authority.Generation != previous.Generation
            || old?.Attachment is null || old.CopyVerificationPending
            || !ownPending && PendingReplacementRequest(connection, request.SessionId) is not null
            || old.Revision != previous.StorageRevision.Value || old.Data.Reference != previous.File
            || !Same(previous.InventoryRevision, DispositionRevision(connection, request.SessionId, request.TaskId))
            || revision.Review.Request.Origin != RequestOrigin.LocalUi || revision.Review.Request.InvocationId is not null
            || revision.Review.Request.SessionId != request.SessionId || revision.Review.Request.TaskId != request.TaskId
            || revision.Reference.SourceId == old.Data.Reference.SourceId
            || revision.Reference.RevisionId == old.Data.Reference.RevisionId
            || revision.Reference.ItemId == old.Data.Reference.ItemId || !admitted())
        {
            throw new InvalidOperationException("The exact old attachment, inventory, active generation or new native capture changed. Review again.");
        }
        return old;
    }

    private static SecurityAuditEvent ReplacementAudit(HostRequest request, SecurityAuditOutcome outcome) =>
        new(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval, "session.file.replace", outcome,
            SecurityAuditInitiator.LocalUser, Id(request.TaskId));

    private static bool AttachmentCopyVerificationPending(SqliteConnection connection, long sequence, AttachmentData data)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT envelope FROM security_audit_events WHERE sequence=$sequence;";
        command.Parameters.AddWithValue("$sequence", sequence);
        var envelope = HostInteractionCodec.Decode<AuthorityAudit>((string)(command.ExecuteScalar()
            ?? throw new InvalidDataException("Attachment copy authority is missing.")));
        var pending = envelope.Audit.Outcome == SecurityAuditOutcome.Requested;
        if (pending && (!Same(envelope.Audit.ActionId, "session.file.replace")
            || envelope.Request.SessionId != data.Request.SessionId || envelope.Request.TaskId != data.Request.TaskId
            || envelope.Request.Origin != RequestOrigin.LocalUi || envelope.Request.InvocationId is not null))
        {
            throw new InvalidDataException("Pending replacement lost its exact native copy-verification authority.");
        }
        return pending;
    }

    private static HostRequest? PendingReplacementRequest(SqliteConnection connection, HostId<SessionIdentity> session)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT envelope FROM security_audit_events
            WHERE sequence IN (
                SELECT MAX(sequence) FROM security_audit_events
                WHERE json_extract(envelope,'$.Audit.ActionId')='session.file.replace'
                    AND json_extract(envelope,'$.Request.SessionId.Value')=$id
                GROUP BY json_extract(envelope,'$.Request.TaskId.Value'))
                AND json_extract(envelope,'$.Audit.Outcome')=$requested
                AND sequence>=COALESCE((
                    SELECT MAX(sequence) FROM security_audit_events
                    WHERE json_extract(envelope,'$.Request.SessionId.Value')=$id
                        AND json_extract(envelope,'$.Audit.ActionId')='session.file.remove'),0) LIMIT 2;
            """;
        command.Parameters.AddWithValue("$id", Id(session));
        command.Parameters.AddWithValue("$requested", (int)SecurityAuditOutcome.Requested);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) { return null; }
        var envelope = HostInteractionCodec.Decode<AuthorityAudit>(reader.GetString(0));
        if (reader.Read() || envelope.Request.Origin != RequestOrigin.LocalUi || envelope.Request.InvocationId is not null)
        {
            throw new InvalidDataException("Pending replacement has conflicting or non-native authority.");
        }
        return envelope.Request;
    }

    private static HostId<TaskIdentity>? PendingReplacementIntent(SqliteConnection connection, HostId<SessionIdentity> session)
    {
        var pending = PendingReplacementRequest(connection, session);
        if (pending is null) { return null; }
        var task = ReadTask(connection, pending.TaskId)
            ?? throw new InvalidDataException("The pending replacement capture intent is missing.");
        if (task.State == HostTaskState.Interrupted) { return null; }
        if (task.State != HostTaskState.IntentRecorded || task.Revision.Value != 1)
        {
            throw new InvalidOperationException("Live or Unknown replacement work holds copy recovery.");
        }
        return task.Request.TaskId;
    }
}
