using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore : ISavedDraftCatalogueStore
{
    private readonly byte[] draftCursorKey = RandomNumberGenerator.GetBytes(32);

    public ValueTask<SavedDraftPage> ReadSavedDraftPage(SessionListFilter filter, string? cursor,
        long controlRevision, Func<bool> admitted, CancellationToken token, SessionListSearch? query = null,
        SavedDraftMetadataFilter? metadataFilter = null) =>
        new(ReadDraftMetadataOperation(() =>
        {
            if (!Enum.IsDefined(filter) || controlRevision < 0 || query is not null && query.Filter != filter)
            { throw new InvalidDataException("Saved draft metadata query is invalid."); }
            RequireCatalogueAdmission(admitted, token);
            using var lease = database.AcquireReadLease(token);
            var identity = RequireInspectionIdentity();
            using var connection = database.OpenReadOnly(token);
            using var transaction = connection.BeginTransaction(deferred: true);
            var profile = RequireCatalogueProfile(connection);
            var viewer = RequireCatalogueViewer();
            var snapshot = ValidateDraftCatalogueAuthority(connection, identity, token);
            var now = time.GetUtcNow();
            var continuation = cursor is null ? null : DecodeDraftCursor(cursor);
            if (continuation is not null && (continuation.Filter != filter || continuation.ControlRevision != controlRevision
                || continuation.QueryKind != query?.Kind
                || (continuation.MetadataFilter ?? SavedDraftMetadataFilter.Any) != (metadataFilter ?? SavedDraftMetadataFilter.Any)
                || !string.Equals(continuation.QueryDigest, query?.Digest, StringComparison.Ordinal)
                || continuation.Profile != profile || continuation.Viewer != viewer || continuation.Source != runId
                || !Same(continuation.Identity, identity) || continuation.Ceiling != snapshot.AuthorityCeiling
                || !Same(continuation.Digest, snapshot.AuthorityCeilingDigest!) || now >= continuation.ExpiresAt
                || continuation.ExpiresAt.Offset != TimeSpan.Zero || continuation.After == Guid.Empty
                || continuation.ExpiresAt > now.AddMinutes(SessionAttachmentPage.SnapshotMinutes)))
            { throw new InvalidDataException("Saved draft source or continuation changed. Refresh."); }
            var audit = ValidateCatalogueAuditSegment(connection, snapshot, continuation?.Through ?? 0,
                continuation?.Hash ?? EmptyHash, token);
            var scope = new DraftCursor(filter, controlRevision, profile, viewer, runId, identity,
                snapshot.AuthorityCeiling!.Value, snapshot.AuthorityCeilingDigest!, continuation?.After,
                audit.Through, audit.Hash, continuation?.ExpiresAt ?? now.AddMinutes(SessionAttachmentPage.SnapshotMinutes));
            scope = scope with { QueryKind = query?.Kind, QueryDigest = query?.Digest, MetadataFilter = metadataFilter };
            if (audit.Through < scope.Ceiling)
            {
                var pending = new SavedDraftPage(filter, [], EncodeDraftCursor(scope), 0, audit.Scanned, true,
                    query?.Kind, query?.Digest, metadataFilter);
                pending.Validate();
                VerifyCatalogueSource(identity, admitted, token);
                return pending;
            }
            using var command = DraftMetadataCommand(connection, scope.After, query?.ExactId);
            checkpoint?.BeforeConversationDraftMetadataRead(command);
            using var rows = command.ExecuteReader();
            var records = ImmutableArray.CreateBuilder<SavedDraftSummary>();
            var scanned = 0;
            var after = scope.After;
            while (scanned < SavedDraftPage.MaximumScanned && records.Count < SavedDraftPage.MaximumRecords && rows.Read())
            {
                RequireCatalogueAdmission(admitted, token);
                var id = new HostId<SessionIdentity>(ParseId(rows.GetString(0)));
                var summary = DecodeDraftMetadata(connection, rows, scope);
                if (summary is not null && (filter == SessionListFilter.All
                    || summary.Session.IsActive == (filter == SessionListFilter.Active))
                    && (query is null || query.Matches(new(summary.Session, summary.Metadata)))
                    && (metadataFilter is null || metadataFilter.Matches(summary)))
                {
                    var reserved = new SavedDraftPage(filter, records.ToImmutable().Add(summary),
                        EncodeDraftCursor(scope with { After = id.Value }), SavedDraftPage.MaximumScanned, audit.Scanned, false,
                        query?.Kind, query?.Digest, metadataFilter);
                    if (SavedDraftPage.Size(reserved) > SavedDraftPage.MaximumBytes) { break; }
                    records.Add(summary);
                }
                scanned++;
                after = id.Value;
            }
            using var remaining = connection.CreateCommand();
            remaining.CommandText = "SELECT EXISTS(SELECT 1 FROM conversation_drafts WHERE session_id>$after);";
            remaining.Parameters.AddWithValue("$after", after?.ToString("D") ?? string.Empty);
            var more = query?.ExactId is null && (long)remaining.ExecuteScalar()! == 1;
            var result = new SavedDraftPage(filter, records.ToImmutable(),
                more ? EncodeDraftCursor(scope with { After = after }) : null, scanned, audit.Scanned, false,
                query?.Kind, query?.Digest, metadataFilter);
            result.Validate();
            VerifyCatalogueSource(identity, admitted, token);
            return result;
        }, token));

    public ValueTask<SessionWorkspaceEntry> ResolveSavedDraft(SavedDraftSummary exact,
        Func<bool> admitted, CancellationToken token) =>
        new(ReadDraftMetadataOperation(() =>
        {
            exact.Validate();
            RequireCatalogueAdmission(admitted, token);
            _ = RequireCatalogueViewer();
            using var lease = database.AcquireReadLease(token);
            var identity = RequireInspectionIdentity();
            using var connection = database.OpenReadOnly(token);
            using var transaction = connection.BeginTransaction(deferred: true);
            var profile = RequireCatalogueProfile(connection);
            var snapshot = ValidateDraftCatalogueAuthority(connection, identity, token);
            if (exact.Source != runId || exact.Profile != profile || exact.AuthorityCeiling != snapshot.AuthorityCeiling
                || !Same(exact.AuthorityDigest, snapshot.AuthorityCeilingDigest!))
            { throw new InvalidOperationException("The saved draft source changed. Refresh; no newer draft is followed."); }
            using var command = DraftMetadataCommand(connection, null, exact.Session.SessionId.Value);
            checkpoint?.BeforeConversationDraftMetadataRead(command);
            using var row = command.ExecuteReader();
            var scope = new DraftCursor(SessionListFilter.All, exact.ControlRevision, profile, RequireCatalogueViewer(),
                runId, identity, exact.AuthorityCeiling, exact.AuthorityDigest, null, exact.AuthorityCeiling,
                exact.AuthorityDigest, time.GetUtcNow());
            if (!row.Read() || DecodeDraftMetadata(connection, row, scope) != exact)
            { throw new InvalidOperationException("The exact saved draft was cleared, removed or changed. Refresh."); }
            VerifyCatalogueSource(identity, admitted, token);
            return new SessionWorkspaceEntry(exact.Session, exact.Metadata);
        }, token));

    private static async Task<T> ReadDraftMetadataOperation<T>(Func<T> read, CancellationToken token)
    {
        using var activity = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Storage);
        try
        {
            var result = await Task.Run(read, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            activity.Complete(HostOperationOutcome.Completed);
            return result;
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    private EvidenceSnapshot ValidateDraftCatalogueAuthority(SqliteConnection connection, string identity, CancellationToken token)
    {
        var snapshot = ReadAuditSnapshot(connection, identity, null);
        if (Scalar(connection, "SELECT count(*) FROM security_audit_events;") != snapshot.AuthorityCeiling)
        { throw new InvalidDataException("Saved draft metadata has a missing committed audit chain row."); }
        ValidateMetadataProvenance(connection);
        // Existing conversation inventory validation uses SQL identity/sequence metadata only, not the body decoder.
        // These whole-store provenance checks remain a cost of the consolidated authority model.
        ValidateConversation(connection);
        database.RequireEmptyArtifactInventory();
        foreach (var session in ReadRetentionCandidates(connection, token).Take(MaximumRetentionSessions))
        {
            if (!HasRetentionHold(connection, session))
            { throw new InvalidOperationException("Retention is due. Passive catalogue browsing cannot perform maintenance."); }
        }
        return snapshot;
    }

    private static SqliteCommand DraftMetadataCommand(SqliteConnection connection, Guid? after, Guid? exact)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT session_id,revision,audit_sequence,typeof(payload),length(CAST(payload AS BLOB)),
                CASE WHEN json_valid(payload) THEN json_type(payload,'$.Body') END,
                CASE WHEN json_valid(payload) THEN length(CAST(json_extract(payload,'$.Body') AS BLOB)) END,
                CASE WHEN json_valid(payload) THEN length(json_extract(payload,'$.Body')) END,
                CASE WHEN json_valid(payload) THEN json_extract(payload,'$.SessionId.Value') END,
                CASE WHEN json_valid(payload) THEN json_extract(payload,'$.Revision') END,
                CASE WHEN json_valid(payload) THEN json_extract(payload,'$.Version') END,
                CASE WHEN json_valid(payload) THEN json_type(payload,'$.Revision') END,
                CASE WHEN json_valid(payload) THEN json_type(payload,'$.Version') END
            FROM conversation_drafts WHERE session_id>$after AND ($exact IS NULL OR session_id=$exact)
            ORDER BY session_id LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$after", after?.ToString("D") ?? string.Empty);
        command.Parameters.AddWithValue("$exact", exact?.ToString("D") is { } id ? id : DBNull.Value);
        command.Parameters.AddWithValue("$limit", exact is null ? SavedDraftPage.MaximumScanned : 1);
        return command;
    }

    private SavedDraftSummary? DecodeDraftMetadata(SqliteConnection connection, SqliteDataReader row, DraftCursor scope)
    {
        var id = new HostId<SessionIdentity>(ParseId(row.GetString(0)));
        if (row.GetFieldType(1) != typeof(long) || row.GetFieldType(2) != typeof(long)
            || row.GetInt64(1) <= 0 || !Same(row.GetString(3), "text") || row.GetInt64(4) > 6 * ConversationText.MaximumUtf16Characters + 1024
            || row.IsDBNull(5) || row.GetString(5) is not ("text" or "null")
            || row.IsDBNull(8) || !Same(row.GetString(8), Id(id)) || row.IsDBNull(9) || row.GetInt64(9) != row.GetInt64(1)
            || row.IsDBNull(10) || row.GetInt64(10) != 1 || !Same(row.GetString(11), "integer") || !Same(row.GetString(12), "integer"))
        { throw new InvalidDataException("Saved draft metadata has invalid SQLite types, version, identity or revision."); }
        var sequence = row.GetInt64(2);
        var audit = ReadAttachmentAudit(connection, sequence);
        var changes = audit.Changes.Where(change => Same(change.Kind, "conversation-draft") && Same(change.Id, Id(id))
            && change.Revision == row.GetInt64(1)).ToArray();
        if (changes.Length != 1 || audit.Request.SessionId != id
            || audit.Audit.ActionId is not ("conversation.draft.save" or "conversation.draft.clear")
            || audit.Audit.Outcome != Kora.Core.Auditing.SecurityAuditOutcome.Succeeded)
        { throw new InvalidDataException("Saved draft metadata lacks an exact typed native save/clear commit."); }
        if (Same(row.GetString(5), "null") != Same(audit.Audit.ActionId, "conversation.draft.clear"))
        { throw new InvalidDataException("Saved draft presence conflicts with its typed save/clear commit."); }
        var task = ReadTask(connection, audit.Request.TaskId)
            ?? throw new InvalidDataException("Saved draft control task is missing.");
        if (task.Request != audit.Request || task.State != HostTaskState.Succeeded || !audit.Changes.Contains(TaskChange(task)))
        { throw new InvalidDataException("Saved draft control task lineage is unavailable."); }
        ValidateCatalogueLatest(connection, "conversation-draft", id, sequence);
        var owner = ReadCatalogueSession(connection, id);
        var retention = ReadRetention(connection, id);
        var metadata = ReadDraftCatalogueMetadata(connection, id);
        if (owner.State == 2 || retention.Purged)
        { throw new InvalidDataException("Removed session authority cannot retain a saved draft slot."); }
        if (Same(row.GetString(5), "null")) { return null; }
        var bytes = row.GetInt64(6);
        var scalars = row.GetInt64(7);
        if (bytes > ConversationText.MaximumUtf8Bytes || scalars > ConversationText.MaximumUtf16Characters)
        { throw new InvalidDataException("Saved draft length exceeds the authoritative conversation bounds."); }
        if (bytes == 0) { return null; }
        var summary = new SavedDraftSummary(runId, scope.Profile, owner.Authority, metadata, row.GetInt64(1),
            bytes, scalars, scope.ControlRevision, scope.Ceiling, scope.Digest);
        summary.Validate();
        return summary;
    }

    private static SessionMetadata? ReadDraftCatalogueMetadata(SqliteConnection connection, HostId<SessionIdentity> id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT session_id,revision,
                CASE WHEN typeof(name)='text' AND length(CAST(name AS BLOB))<=$bytes THEN name END,
                audit_sequence FROM session_metadata WHERE session_id=$id;
            """;
        command.Parameters.AddWithValue("$id", Id(id));
        command.Parameters.AddWithValue("$bytes", SessionName.MaximumUtf8Bytes);
        using var row = command.ExecuteReader();
        if (!row.Read()) { return null; }
        if (row.IsDBNull(2) || row.GetFieldType(1) != typeof(long) || row.GetFieldType(3) != typeof(long))
        { throw new InvalidDataException("Saved draft name metadata exceeds its validated projection or has invalid types."); }
        var metadata = DecodeMetadata(connection, row);
        ValidateAttachmentRowAuthority(connection, row.GetInt64(3), MetadataChange(metadata));
        ValidateCatalogueLatest(connection, "metadata", id, row.GetInt64(3));
        return metadata;
    }

    private string EncodeDraftCursor(DraftCursor cursor)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(cursor);
        return Convert.ToBase64String(bytes) + "." + Convert.ToHexStringLower(HMACSHA256.HashData(draftCursorKey, bytes));
    }

    private DraftCursor DecodeDraftCursor(string text)
    {
        SessionAttachmentPage.ValidateCursor(text);
        try
        {
            var parts = text.Split('.');
            var bytes = Convert.FromBase64String(parts[0]);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(parts[1]), HMACSHA256.HashData(draftCursorKey, bytes)))
            { throw new InvalidDataException("Saved draft continuation authentication failed."); }
            return HostInteractionCodec.Decode<DraftCursor>(System.Text.Encoding.UTF8.GetString(bytes));
        }
        catch (FormatException exception)
        { throw new InvalidDataException("Saved draft continuation is malformed.", exception); }
    }

    private sealed record DraftCursor(SessionListFilter Filter, long ControlRevision, HostId<DeviceProfileIdentity> Profile,
        HostId<RequestIdentity> Viewer, Guid Source, string Identity, long Ceiling, string Digest, Guid? After,
        long Through, string Hash, DateTimeOffset ExpiresAt,
        SessionListSearchKind? QueryKind = null, string? QueryDigest = null,
        SavedDraftMetadataFilter? MetadataFilter = null);
}
