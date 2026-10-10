using System.Security.Cryptography;
using System.Text;

using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Memory;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    private void MigrateAttachments(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        ValidateConsolidatedAuthority(connection);
        SessionHistoryPersistence.Validate(connection);
        ValidateQueue(connection);
        ValidateRetention(connection);
        ValidateMemory(connection);
        ValidateAttachmentMigrationAdmission(connection);
        token.ThrowIfCancellationRequested();
        Execute(connection, transaction, HostInteractionSchema.AttachmentTable);
        checkpoint?.BeforeCommit(connection, transaction);
    }

    private static void ValidateAttachmentMigrationAdmission(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        if (command.ExecuteScalar() is long version && version == HostInteractionSchema.Version) { return; }
        command.CommandText = """
            SELECT 1 FROM security_audit_events a, json_each(a.envelope,'$.Changes') c
            WHERE json_extract(c.value,'$.Kind')='session-file' LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null)
        {
            throw new InvalidDataException("A downgraded attachment schema cannot replace committed snapshots or removal provenance.");
        }
    }

    public ValueTask<SessionFileAttachment?> ReadAttachment(HostId<SessionIdentity> session, CancellationToken token)
    {
        session.Validate();
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            var identity = RequireInspectionIdentity();
            using var connection = database.OpenReadOnly(token);
            ValidateAuthority(connection);
            RequireAttachmentReadable(connection, session);
            var row = ReadAttachmentRow(connection, session);
            token.ThrowIfCancellationRequested();
            database.VerifyFiles();
            if (!Same(identity, RequireInspectionIdentity())) { throw new InvalidDataException("Attachment storage changed during inspection."); }
            return row?.Attachment;
        }, token));
    }

    public async ValueTask<SessionFileAttachment> Attach(HostRequest request, HostRevision generation, LocalFileRevision revision,
        ReadOnlyMemory<byte> originalBytes, Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(admitted);
        RequireLive(request);
        await VerifyPriorAttachmentCopies(request, generation, admitted, token).ConfigureAwait(false);
        return await RunHostMutationAsync(request, "session.file.attach", (connection, transaction, intent, audit) =>
        {
            RequireFreshMetadataIntent(intent);
            _ = RequireInspectionIdentity();
            var owner = RequireSession(connection, request.SessionId);
            RequireAttachmentReadable(connection, request.SessionId);
            if (request.Origin != RequestOrigin.LocalUi || request.InvocationId is not null
                || revision.Review.Request.Origin != RequestOrigin.LocalUi || revision.Review.Request.InvocationId is not null
                || revision.Review.Request.SessionId != request.SessionId || revision.Review.Request.TaskId != request.TaskId
                || !owner.Authority.IsActive || owner.Authority.Generation != generation || !admitted())
            {
                throw new InvalidOperationException("Attachment admission requires the exact current native session and original capture lineage.");
            }
            var validated = LocalFileRevision.Restore(revision.Review, originalBytes.Span, revision.AdmittedAt, revision.Reference);
            if (!string.Equals(validated.Text, revision.Text, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The stored projection must match the authoritative capture.");
            }
            var previous = ReadAttachmentRow(connection, request.SessionId);
            if (previous?.Attachment is not null)
            {
                throw new InvalidOperationException("Remove this session's existing attachment and Kora copies before selecting another file.");
            }
            if (previous is not null) { database.RequireEmptyJournal(); }
            database.RequireEmptyArtifactInventory();
            using var count = connection.CreateCommand();
            count.Transaction = transaction;
            count.CommandText = "SELECT count(*) FROM session_file WHERE body IS NOT NULL;";
            if ((long)count.ExecuteScalar()! >= SessionFileAttachment.MaximumRetainedFiles)
            {
                throw new InvalidOperationException("The private profile already retains sixteen attachments. No file was evicted.");
            }
            var storageRevision = checked((previous?.Revision ?? 0) + 1);
            Execute(connection, transaction, "PRAGMA secure_delete=ON; PRAGMA journal_size_limit=0;");
            var descriptor = new AttachmentData(1, ReadMemoryProfile(connection), request.SessionId, generation,
                revision.Reference, revision.Review.ReviewId, revision.Review.Request, revision.Review.Metadata,
                revision.AdmittedAt, SessionFileAttachment.Projection, AttachmentMediaType(revision.Review.Metadata.CanonicalPath));
            var completed = intent.Next(HostTaskState.Succeeded);
            var sequence = AppendAudit(connection, transaction, intent, owner.Authority, audit,
                changes: [AttachmentChange(descriptor, storageRevision), TaskChange(completed)]);
            byte[] bytes = originalBytes.ToArray();
            try
            {
                WriteAttachment(connection, transaction, descriptor, storageRevision, bytes, sequence);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, completed);
            TouchActivity(connection, transaction, request.SessionId);
            return new SessionFileAttachment(request.SessionId, generation, new(storageRevision), validated);
        }, token, admitted).ConfigureAwait(false);
    }

    private Task VerifyPriorAttachmentCopies(HostRequest request, HostRevision generation, Func<bool> admitted,
        CancellationToken token) => Task.Run(() =>
    {
        using var lease = database.AcquireReadLease(token);
        _ = RequireInspectionIdentity();
        bool previousRemoval;
        using (var connection = database.OpenReadOnly(token))
        {
            ValidateAuthority(connection);
            var owner = RequireSession(connection, request.SessionId);
            if (request.Origin != RequestOrigin.LocalUi || request.InvocationId is not null
                || !owner.Authority.IsActive || owner.Authority.Generation != generation || !admitted())
            {
                throw new InvalidOperationException("Attachment copy verification requires current original native session admission.");
            }
            var row = ReadAttachmentRow(connection, request.SessionId);
            previousRemoval = row is { Attachment: null };
        }
        if (!previousRemoval) { return; }
        database.RequireEmptyArtifactInventory();
        token.ThrowIfCancellationRequested();
        if (!admitted()) { throw new InvalidOperationException("Attachment admission changed before prior-copy verification."); }
        // Fresh control intents can grow the shared committed journal after an earlier successful removal.
        // Only a committed tombstone permits this inventoried cleanup; an active body is never replaced.
        database.ClearCommittedJournal();
    }, token);

    public ValueTask<SessionFileRemoval> PreviewRemoval(HostId<SessionIdentity> session, CancellationToken token) =>
        new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            _ = RequireInspectionIdentity();
            using var connection = database.OpenReadOnly(token);
            ValidateAuthority(connection);
            RequireAttachmentReadable(connection, session);
            RequireDispositionIdle(connection, session, null);
            database.RequireEmptyArtifactInventory();
            var row = ReadAttachmentRow(connection, session)
                ?? throw new InvalidOperationException("This exact session has no attachment.");
            var generation = RequireSession(connection, session).Authority.Generation;
            return new SessionFileRemoval(Guid.NewGuid(), session, generation, new(row.Revision),
                row.Data.Reference, DispositionRevision(connection, session, null))
            { BodyRetained = row.Attachment is not null };
        }, token));

    public async ValueTask Remove(HostRequest request, SessionFileRemoval review, Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(review);
        await RunHostMutationAsync(request, "session.file.remove", (connection, transaction, intent, audit) =>
        {
            RequireFreshMetadataIntent(intent);
            _ = RequireInspectionIdentity();
            var owner = RequireSession(connection, request.SessionId);
            RequireDispositionIdle(connection, request.SessionId, request.TaskId);
            database.RequireEmptyArtifactInventory();
            var row = ReadAttachmentRow(connection, request.SessionId);
            if (request.Origin != RequestOrigin.LocalUi || request.InvocationId is not null
                || review.ConfirmationId == Guid.Empty || request.SessionId != review.Session
                || owner.State == 2 || owner.Authority.Generation != review.Generation
                || row is null || row.Revision != review.StorageRevision.Value || row.Data.Reference != review.File
                || review.BodyRetained != (row.Attachment is not null)
                || !Same(review.InventoryRevision, DispositionRevision(connection, request.SessionId, request.TaskId)))
            {
                throw new InvalidOperationException("The exact attachment, session, revision or owned-copy inventory changed. Review again.");
            }
            Execute(connection, transaction, "PRAGMA secure_delete=ON; PRAGMA journal_size_limit=0;");
            var tombstone = row.Data with { Metadata = null };
            var revision = checked(row.Revision + 1);
            var completed = intent.Next(HostTaskState.Succeeded);
            var sequence = AppendAudit(connection, transaction, intent, owner.Authority, audit,
                changes: [AttachmentChange(tombstone, revision), TaskChange(completed)]);
            WriteAttachment(connection, transaction, tombstone, revision, null, sequence);
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, completed);
            return true;
        }, token, admitted).ConfigureAwait(false);
        // Revocation is committed. Cancellation cannot restore body; a failed journal verification holds completion.
        using var lease = database.AcquireReadLease(CancellationToken.None);
        database.ClearCommittedJournal();
        database.RequireEmptyArtifactInventory();
    }

    private void RequireAttachmentReadable(SqliteConnection connection, HostId<SessionIdentity> session)
    {
        if (RequireSession(connection, session).State == 2 || ReadRetention(connection, session).Purged)
        {
            throw new InvalidOperationException("Removed session snapshots are unavailable.");
        }
    }

    private static AuthorityChange AttachmentChange(AttachmentData data, long revision) =>
        Change("session-file", Id(data.Session), revision, HostInteractionCodec.Encode(data));

    private static void WriteAttachment(SqliteConnection connection, SqliteTransaction transaction,
        AttachmentData data, long revision, byte[]? bytes, long sequence) =>
        Execute(connection, transaction, """
            INSERT INTO session_file VALUES($session,$revision,$descriptor,$body,$audit)
            ON CONFLICT(session_id) DO UPDATE SET revision=excluded.revision,descriptor=excluded.descriptor,
                body=excluded.body,audit_sequence=excluded.audit_sequence;
            """, ("$session", Id(data.Session)), ("$revision", revision),
            ("$descriptor", HostInteractionCodec.Encode(data)), ("$body", bytes is null ? DBNull.Value : bytes),
            ("$audit", sequence));

    private static AttachmentRow? ReadAttachmentRow(SqliteConnection connection, HostId<SessionIdentity> session)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT revision,descriptor,body,audit_sequence,length(body),typeof(body) FROM session_file WHERE session_id=$session;";
        command.Parameters.AddWithValue("$session", Id(session));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) { return null; }
        var revision = reader.GetInt64(0);
        var data = HostInteractionCodec.Decode<AttachmentData>(reader.GetString(1));
        if (data.Request is null || data.Reference is null || data.Reference.Digest is null
            || data.Format != 1 || data.Profile != ReadMemoryProfile(connection) || data.Session != session
            || data.Request.SessionId != session || data.Request.Origin != RequestOrigin.LocalUi || data.Request.InvocationId is not null
            || data.Generation.Value <= 0 || data.ReviewId == Guid.Empty
            || data.Reference.SourceId == Guid.Empty || data.Reference.RevisionId == Guid.Empty || data.Reference.ItemId == Guid.Empty
            || !IsHex(data.Reference.Digest, 64) || data.CapturedAt.Offset != TimeSpan.Zero
            || !Same(data.Projection, SessionFileAttachment.Projection) || data.MediaType is not ("text/plain" or "text/markdown") || revision <= 0
            || data.Generation.Value > RequireSession(connection, session).Authority.Generation.Value
            || reader.IsDBNull(2) != (data.Metadata is null)
            || !reader.IsDBNull(2) && (reader.GetInt64(4) > LocalFilePolicy.MaximumBytes || !Same(reader.GetString(5), "blob")))
        {
            throw new InvalidDataException("The retained snapshot format, profile, scope, projection or original lineage is invalid.");
        }
        ValidateRowAuthority(connection, reader.GetInt64(3), AttachmentChange(data, revision));
        LocalFileRevision? file = null;
        if (data.Metadata is { } metadata)
        {
            var task = ReadTask(connection, data.Request.TaskId);
            if (metadata.CanonicalPath is null
                || !Same(data.MediaType, AttachmentMediaType(metadata.CanonicalPath))
                || task is null || task.Request.SessionId != session || task.Request.Origin != RequestOrigin.LocalUi
                || task.Request.InvocationId is not null || task.State != HostTaskState.Succeeded)
            {
                throw new InvalidDataException("The retained attachment lost its original committed capture task or exact type/metadata provenance.");
            }
            var bytes = reader.GetFieldValue<byte[]>(2);
            try
            {
                file = LocalFileRevision.Restore(new(data.ReviewId, data.Reference.SourceId, data.Request, metadata),
                    bytes, data.CapturedAt, data.Reference);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("The retained snapshot has an invalid strict-UTF-8 projection.", exception);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
            if (RequireSession(connection, session).State == 2)
            {
                throw new InvalidDataException("Removed session authority retains an unrevoked body.");
            }
        }
        return new(data, revision, file is null ? null : new(session, data.Generation, new(revision), file));
    }

    private static void ValidateAttachments(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT session_id FROM session_file ORDER BY session_id;";
        var retained = 0;
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                if (ReadAttachmentRow(connection, new(ParseId(reader.GetString(0))))!.Attachment is not null
                    && ++retained > SessionFileAttachment.MaximumRetainedFiles)
                {
                    throw new InvalidDataException("The retained profile attachment inventory exceeds its approved bound.");
                }
            }
        }
        command.CommandText = """
            SELECT 1 FROM (
                SELECT json_extract(c.value,'$.Id') AS id, MAX(a.sequence) AS sequence
                FROM security_audit_events a, json_each(a.envelope,'$.Changes') c
                WHERE json_extract(c.value,'$.Kind')='session-file' GROUP BY json_extract(c.value,'$.Id')
            ) latest LEFT JOIN session_file f ON f.session_id=latest.id
            WHERE f.audit_sequence IS NULL OR f.audit_sequence<>latest.sequence LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null)
        {
            throw new InvalidDataException("Committed attachment or removal provenance is missing or stale.");
        }
    }

    private static void RevokeAttachment(SqliteConnection connection, SqliteTransaction transaction,
        HostId<SessionIdentity> session, long sequence)
    {
        var row = ReadAttachmentRow(connection, session);
        if (row?.Attachment is not null)
        {
            WriteAttachment(connection, transaction, row.Data with { Metadata = null }, checked(row.Revision + 1), null, sequence);
        }
    }

    private static AuthorityChange[] AttachmentInvalidation(SqliteConnection connection, HostId<SessionIdentity> session)
    {
        var row = ReadAttachmentRow(connection, session);
        return row?.Attachment is null ? [] : [AttachmentChange(row.Data with { Metadata = null }, checked(row.Revision + 1))];
    }

    private sealed record AttachmentRow(AttachmentData Data, long Revision, SessionFileAttachment? Attachment);
    private static string AttachmentMediaType(string path) =>
        path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? "text/plain" : "text/markdown";
    private sealed record AttachmentData(int Format, HostId<DeviceProfileIdentity> Profile, HostId<SessionIdentity> Session,
        HostRevision Generation, LocalFileReference Reference, Guid ReviewId, HostRequest Request, LocalFileMetadata? Metadata,
        DateTimeOffset CapturedAt, string Projection, string MediaType);
}
