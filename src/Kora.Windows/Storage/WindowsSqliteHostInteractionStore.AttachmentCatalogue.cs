using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore : ISessionAttachmentCatalogueStore
{
    private readonly byte[] attachmentCursorKey = RandomNumberGenerator.GetBytes(32);
    private HostId<DeviceProfileIdentity>? attachmentProfile;

    private void InitializeAttachmentCatalogue(SqliteConnection connection)
    {
        var profile = ReadMemoryProfile(connection);
        if (attachmentProfile is { } previous && previous != profile)
        {
            throw new InvalidDataException("The initialized private attachment profile was replaced.");
        }
        attachmentProfile = profile;
    }

    public ValueTask<SessionAttachmentPage> ReadAttachmentPage(SessionAttachmentQuery query, string? cursor,
        Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(admitted);
        query.Validate();
        return new(ReadCatalogue(query, cursor, admitted, token));
    }

    private async Task<SessionAttachmentPage> ReadCatalogue(SessionAttachmentQuery query, string? cursor,
        Func<bool> admitted, CancellationToken token)
    {
        using var activity = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Storage);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(SessionAttachmentPage.RequestSeconds));
        try
        {
            var result = await Task.Run(() => ReadCataloguePage(query, cursor, admitted, deadline.Token), deadline.Token)
                .ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            if (!admitted()) { throw new InvalidOperationException("Private attachment catalogue admission changed."); }
            activity.Complete(HostOperationOutcome.Completed);
            return result;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    private SessionAttachmentPage ReadCataloguePage(SessionAttachmentQuery query, string? cursor,
        Func<bool> admitted, CancellationToken token)
    {
        RequireCatalogueAdmission(admitted, token);
        using var lease = database.AcquireReadLease(token);
        var identity = RequireInspectionIdentity();
        using var connection = database.OpenReadOnly(token);
        using var transaction = connection.BeginTransaction(deferred: true);
        var profile = RequireCatalogueProfile(connection);
        var viewer = RequireCatalogueViewer();
        var now = time.GetUtcNow();
        var continuation = cursor is null ? null : DecodeAttachmentCursor(cursor, query, identity, viewer, now);
        var snapshot = ValidateCatalogueAuthority(connection, identity, token);
        if (continuation is not null && (continuation.Ceiling != snapshot.AuthorityCeiling
            || !Same(continuation.Digest, snapshot.AuthorityCeilingDigest!) || continuation.Profile != profile))
        {
            throw new InvalidDataException("Attachment catalogue authority changed. Refresh; no stale continuation is admitted.");
        }
        var expires = continuation?.ExpiresAt ?? now.AddMinutes(SessionAttachmentPage.SnapshotMinutes);
        var audit = ValidateCatalogueAuditSegment(connection, snapshot, continuation?.AuditedThrough ?? 0,
            continuation?.AuditedHash ?? EmptyHash, token);
        if (audit.Through < snapshot.AuthorityCeiling)
        {
            var pending = new SessionAttachmentPage(query, [], EncodeAttachmentCursor(new(query, identity, expires,
                snapshot.AuthorityCeiling!.Value, snapshot.AuthorityCeilingDigest!, null, audit.Through, audit.Hash, profile, viewer,
                AttachmentMetadataBinding(query))),
                now, snapshot.AuthorityCeiling.Value, snapshot.AuthorityCeilingDigest!, 0, false, audit.Scanned, true);
            pending.Validate();
            VerifyCatalogueSource(identity, admitted, token);
            return pending;
        }
        if (continuation?.After is not null)
        {
            using var predecessor = AttachmentMetadataCommand(connection, null, continuation.After);
            using var row = predecessor.ExecuteReader();
            if (!row.Read()) { throw new InvalidDataException("The attachment continuation slot is missing."); }
            _ = DecodeCatalogueRow(connection, row, admitted, token);
        }
        using var command = AttachmentMetadataCommand(connection, continuation?.After, null);
        checkpoint?.BeforeAttachmentMetadataRead(command);
        using var rows = command.ExecuteReader();
        var records = ImmutableArray.CreateBuilder<SessionAttachmentSummary>();
        var scanned = 0;
        var after = continuation?.After;
        while (scanned < SessionAttachmentPage.MaximumScannedRows && records.Count < SessionAttachmentPage.MaximumRecords
            && rows.Read())
        {
            RequireCatalogueAdmission(admitted, token);
            var summary = DecodeCatalogueRow(connection, rows, admitted, token);
            scanned++;
            after = summary.Session.Value;
            if (query.Matches(summary)) { records.Add(summary); }
        }
        // Lookahead is content-free. It determines only whether a slot remains; it never skips an unvalidated slot.
        using var remaining = connection.CreateCommand();
        remaining.CommandText = "SELECT EXISTS(SELECT 1 FROM session_file WHERE session_id>$after);";
        remaining.Parameters.AddWithValue("$after", after?.ToString("D") ?? string.Empty);
        var more = (long)remaining.ExecuteScalar()! == 1;
        var next = more ? EncodeAttachmentCursor(new(query, identity, expires, snapshot.AuthorityCeiling!.Value,
            snapshot.AuthorityCeilingDigest!, after!.Value, audit.Through, audit.Hash, profile, viewer,
            AttachmentMetadataBinding(query))) : null;
        var page = new SessionAttachmentPage(query, records.ToImmutable(), next, now,
            snapshot.AuthorityCeiling!.Value, snapshot.AuthorityCeilingDigest!, scanned,
            more && scanned == SessionAttachmentPage.MaximumScannedRows, audit.Scanned);
        page.Validate();
        VerifyCatalogueSource(identity, admitted, token);
        return page;
    }

    public ValueTask VerifyAttachmentSummary(SessionAttachmentSummary exact, Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(exact);
        ArgumentNullException.ThrowIfNull(admitted);
        exact.Validate();
        return new(VerifyCatalogueSelection(exact, admitted, token));
    }

    private async Task VerifyCatalogueSelection(SessionAttachmentSummary exact, Func<bool> admitted, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(SessionAttachmentPage.RequestSeconds));
        await Task.Run(() =>
        {
            RequireCatalogueAdmission(admitted, deadline.Token);
            using var lease = database.AcquireReadLease(deadline.Token);
            var identity = RequireInspectionIdentity();
            using var connection = database.OpenReadOnly(deadline.Token);
            using var transaction = connection.BeginTransaction(deferred: true);
            _ = RequireCatalogueProfile(connection);
            _ = ValidateCatalogueAuthority(connection, identity, deadline.Token);
            using var command = AttachmentMetadataCommand(connection, null, exact.Session.Value);
            checkpoint?.BeforeAttachmentMetadataRead(command);
            using var row = command.ExecuteReader();
            if (!row.Read() || DecodeCatalogueRow(connection, row, admitted, deadline.Token) != exact
                || exact.CopyStatus != AttachmentCopyStatus.Confirmed)
            {
                throw new InvalidOperationException("The selected exact attachment is stale, removed or held. Refresh; no newer source was opened.");
            }
            VerifyCatalogueSource(identity, admitted, deadline.Token);
        }, deadline.Token).ConfigureAwait(false);
        RequireCatalogueAdmission(admitted, deadline.Token);
    }

    private static SqliteCommand AttachmentMetadataCommand(SqliteConnection connection, Guid? after, Guid? exact)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT session_id,revision,
                CASE WHEN typeof(descriptor)='text' AND length(CAST(descriptor AS BLOB))<=8192
                    THEN descriptor ELSE NULL END,
                audit_sequence,length(body),typeof(body)
            FROM session_file WHERE session_id>$after AND ($exact IS NULL OR session_id=$exact)
            ORDER BY session_id LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$after", after?.ToString("D") ?? string.Empty);
        command.Parameters.AddWithValue("$exact", exact?.ToString("D") is { } id ? id : DBNull.Value);
        command.Parameters.AddWithValue("$limit", exact is null ? SessionAttachmentPage.MaximumScannedRows : 1);
        return command;
    }

    private SessionAttachmentSummary DecodeCatalogueRow(SqliteConnection connection, SqliteDataReader row,
        Func<bool> admitted, CancellationToken token)
    {
        RequireCatalogueAdmission(admitted, token);
        if (row.GetFieldType(1) != typeof(long) || row.GetFieldType(3) != typeof(long))
        {
            throw new InvalidDataException("Attachment metadata revision or audit index has an invalid SQLite type.");
        }
        var session = new HostId<SessionIdentity>(ParseId(row.GetString(0)));
        var revision = row.GetInt64(1);
        var descriptor = DecodeAttachmentDescriptor(connection, session, revision,
            row.IsDBNull(2) ? null : row.GetString(2), row.GetInt64(3),
            row.IsDBNull(4) ? null : row.GetInt64(4), row.GetString(5));
        ValidateCatalogueLatest(connection, "session-file", session, row.GetInt64(3));
        var owner = ReadCatalogueSession(connection, session);
        var metadata = ReadMetadata(connection, session);
        if (metadata is not null)
        {
            using var sequence = connection.CreateCommand();
            sequence.CommandText = "SELECT audit_sequence FROM session_metadata WHERE session_id=$id;";
            sequence.Parameters.AddWithValue("$id", Id(session));
            var committed = (long)sequence.ExecuteScalar()!;
            ValidateAttachmentRowAuthority(connection, committed, MetadataChange(metadata));
            ValidateCatalogueLatest(connection, "metadata", session, committed);
        }
        var retention = ReadRetention(connection, session);
        if (descriptor.Data.Metadata is not null && (owner.State == 2 || retention.Purged))
        {
            throw new InvalidDataException("Removed session authority retains an active attachment body.");
        }
        var pendingRequest = PendingReplacementRequest(connection, session);
        var pending = pendingRequest is not null || descriptor.CopyVerificationPending;
        if (pendingRequest is not null)
        {
            var pendingTask = ReadTask(connection, pendingRequest.TaskId)
                ?? throw new InvalidDataException("Pending replacement capture task is missing.");
            _ = WindowsSqliteHostTaskStore.ValidateTaskLedger(connection, pendingTask);
            _ = PendingReplacementIntent(connection, session);
        }
        if (pending && descriptor.Data.Metadata is null)
        {
            throw new InvalidDataException("A removed attachment slot cannot retain pending replacement authority.");
        }
        var summary = new SessionAttachmentSummary(runId, descriptor.Data.Profile, session, owner.Authority.Generation,
            descriptor.Data.Generation, new(revision), (AttachmentSessionState)owner.State,
            descriptor.Data.Reference, descriptor.Bytes ?? 0, descriptor.Data.MediaType, descriptor.Data.CapturedAt,
            descriptor.Data.Metadata is null ? AttachmentCopyStatus.Unavailable
                : pending ? AttachmentCopyStatus.Held : AttachmentCopyStatus.Confirmed);
        summary.Validate();
        return summary;
    }

    private static SessionRow ReadCatalogueSession(SqliteConnection connection, HostId<SessionIdentity> session,
        bool requireLatest = true)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT session_id,generation,state,audit_sequence FROM work_sessions WHERE session_id=$id;";
        command.Parameters.AddWithValue("$id", Id(session));
        using var row = command.ExecuteReader();
        if (!row.Read()) { throw new InvalidDataException("Attachment session authority is missing."); }
        if (row.GetFieldType(1) != typeof(long) || row.GetFieldType(2) != typeof(long) || row.GetFieldType(3) != typeof(long))
        {
            throw new InvalidDataException("Attachment current session authority has an invalid SQLite type.");
        }
        var owner = DecodeSession(row);
        if (owner.Authority.Generation.Value <= 0 || owner.State is < 0 or > 2)
        {
            throw new InvalidDataException("Attachment current session lineage is invalid.");
        }
        ValidateAttachmentRowAuthority(connection, row.GetInt64(3), SessionChange(owner.Authority, checked((int)owner.State)));
        if (requireLatest) { ValidateCatalogueLatest(connection, "session", session, row.GetInt64(3)); }
        return owner;
    }

    private static void ValidateCatalogueLatest(SqliteConnection connection, string kind,
        HostId<SessionIdentity> session, long sequence)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT MAX(a.sequence) FROM security_audit_events a,json_each(a.envelope,'$.Changes') c
            WHERE json_extract(c.value,'$.Kind')=$kind AND json_extract(c.value,'$.Id')=$id;
            """;
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$id", Id(session));
        if (command.ExecuteScalar() is not long latest || latest != sequence)
        {
            throw new InvalidDataException("The attachment catalogue lost latest committed authority.");
        }
    }

    private EvidenceSnapshot ValidateCatalogueAuthority(SqliteConnection connection, string identity, CancellationToken token)
    {
        var snapshot = ReadAuditSnapshot(connection, identity, null);
        ValidateMetadataProvenance(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM security_audit_events;";
        if ((long)command.ExecuteScalar()! != snapshot.AuthorityCeiling)
        {
            throw new InvalidDataException("Attachment catalogue committed audit chain has missing rows.");
        }
        ValidateAttachmentProvenance(connection);
        command.CommandText = "SELECT count(*) FROM session_file WHERE body IS NOT NULL;";
        if ((long)command.ExecuteScalar()! > SessionFileAttachment.MaximumRetainedFiles)
        {
            throw new InvalidDataException("The retained profile attachment body inventory exceeds sixteen; no eviction or truncation is admitted.");
        }
        database.RequireEmptyArtifactInventory();
        foreach (var session in ReadRetentionCandidates(connection, token).Take(MaximumRetentionSessions))
        {
            if (!HasRetentionHold(connection, session))
            {
                throw new InvalidOperationException("Ordinary session retention is due. Metadata inspection is held until private background maintenance establishes source availability; browsing performs no cleanup.");
            }
        }
        return snapshot;
    }

    private static (long Through, string Hash, int Scanned) ValidateCatalogueAuditSegment(SqliteConnection connection,
        EvidenceSnapshot snapshot, long sequence, string hash, CancellationToken token)
    {
        if (sequence < 0 || sequence > snapshot.AuthorityCeiling)
        {
            throw new InvalidDataException("The attachment authority continuation is invalid.");
        }
        using var command = connection.CreateCommand();
        if (sequence != 0)
        {
            command.CommandText = "SELECT hash FROM security_audit_events WHERE sequence=$previous;";
            command.Parameters.AddWithValue("$previous", sequence);
            if (!Same(command.ExecuteScalar() as string ?? string.Empty, hash))
            {
                throw new InvalidDataException("The attachment authority continuation predecessor changed.");
            }
        }
        // Validate complete typed chain segments before any attachment metadata is disclosed.
        command.CommandText = """
            SELECT sequence,correlation_id,previous_hash,hash,
                CASE WHEN typeof(envelope)='text' AND length(CAST(envelope AS BLOB))<=131072
                    THEN envelope ELSE NULL END
            FROM security_audit_events WHERE sequence>$after ORDER BY sequence LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", SessionAttachmentPage.MaximumAuditRows);
        command.Parameters.AddWithValue("$after", sequence);
        var scanned = 0;
        using (var rows = command.ExecuteReader())
        {
            while (rows.Read())
            {
                token.ThrowIfCancellationRequested();
                if (rows.GetInt64(0) != sequence + 1 || rows.IsDBNull(4) || !Same(rows.GetString(2), hash))
                {
                    throw new InvalidDataException("Attachment catalogue committed audit chain is missing, oversized or inconsistent.");
                }
                _ = DecodeAuthorityAudit(rows.GetInt64(0), rows.GetString(1), rows.GetString(2), rows.GetString(3), rows.GetString(4));
                sequence++;
                scanned++;
                hash = rows.GetString(3);
            }
        }
        if (sequence == snapshot.AuthorityCeiling && !Same(hash, snapshot.AuthorityCeilingDigest!)
            || sequence < snapshot.AuthorityCeiling && scanned != SessionAttachmentPage.MaximumAuditRows)
        {
            throw new InvalidDataException("Attachment catalogue committed authority ceiling is inconsistent.");
        }
        return (sequence, hash, scanned);
    }

    private void VerifyCatalogueSource(string identity, Func<bool> admitted, CancellationToken token)
    {
        RequireCatalogueAdmission(admitted, token);
        database.VerifyFiles();
        if (!Same(identity, RequireInspectionIdentity())) { throw new InvalidDataException("Private attachment storage changed."); }
    }

    private HostId<DeviceProfileIdentity> RequireCatalogueProfile(SqliteConnection connection)
    {
        var profile = ReadMemoryProfile(connection);
        if (attachmentProfile != profile) { throw new InvalidDataException("Private attachment profile authority is uninitialized or changed."); }
        return profile;
    }

    private static void RequireCatalogueAdmission(Func<bool> admitted, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!admitted()) { throw new InvalidOperationException("Private attachment metadata admission is unavailable."); }
    }

    private static HostId<RequestIdentity> RequireCatalogueViewer()
    {
        var request = HostActivity.RequireCurrent().Request;
        if (request.Origin != RequestOrigin.LocalUi || request.InvocationId is not null)
        {
            throw new InvalidOperationException("Attachment catalogue reads require original native viewer context.");
        }
        return request.RequestId;
    }

    private string EncodeAttachmentCursor(AttachmentCursor cursor)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(cursor);
        return Convert.ToBase64String(bytes) + "." + Convert.ToHexStringLower(HMACSHA256.HashData(attachmentCursorKey, bytes));
    }

    private string? AttachmentMetadataBinding(SessionAttachmentQuery query) => query.MetadataText is null ? null
        : Convert.ToHexStringLower(HMACSHA256.HashData(attachmentCursorKey, System.Text.Encoding.UTF8.GetBytes(query.MetadataText)));

    private AttachmentCursor DecodeAttachmentCursor(string text, SessionAttachmentQuery query, string identity,
        HostId<RequestIdentity> viewer, DateTimeOffset now)
    {
        SessionAttachmentPage.ValidateCursor(text);
        try
        {
            var parts = text.Split('.');
            if (parts.Length != 2) { throw new FormatException("Invalid continuation shape."); }
            var bytes = Convert.FromBase64String(parts[0]);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(parts[1]), HMACSHA256.HashData(attachmentCursorKey, bytes)))
            {
                throw new FormatException("Invalid continuation authentication.");
            }
            var cursor = HostInteractionCodec.Decode<AttachmentCursor>(System.Text.Encoding.UTF8.GetString(bytes));
            if (cursor.Query != new SessionAttachmentQuery(query.IncludeRemoved, query.ExactSessionId, query.Filter)
                || !string.Equals(cursor.MetadataBinding, AttachmentMetadataBinding(query), StringComparison.Ordinal)
                || !Same(cursor.Identity, identity) || cursor.Viewer != viewer || cursor.After == Guid.Empty
                || cursor.ExpiresAt.Offset != TimeSpan.Zero || now >= cursor.ExpiresAt
                || cursor.ExpiresAt > now.AddMinutes(SessionAttachmentPage.SnapshotMinutes))
            {
                throw new FormatException("Expired or mismatched continuation scope.");
            }
            return cursor;
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("Attachment catalogue continuation is invalid. Refresh.", exception);
        }
    }

    private sealed record AttachmentCursor(SessionAttachmentQuery Query, string Identity, DateTimeOffset ExpiresAt,
        long Ceiling, string Digest, Guid? After, long AuditedThrough, string AuditedHash,
        HostId<DeviceProfileIdentity> Profile, HostId<RequestIdentity> Viewer, string? MetadataBinding);
}
