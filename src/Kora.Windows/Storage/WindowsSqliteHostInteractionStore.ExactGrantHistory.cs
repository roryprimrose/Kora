using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    private readonly byte[] exactHistoryCursorKey = RandomNumberGenerator.GetBytes(32);

    public async ValueTask<ExactGrantHistoryPage> ReadExactGrantHistoryAsync(ExactGrantInspection inspected,
        HostRequest viewer, string? cursor, Func<bool> admitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inspected);
        ArgumentNullException.ThrowIfNull(admitted);
        inspected.Grant.Id.Validate();
        RequireLive(viewer);
        if (viewer.Origin != RequestOrigin.LocalUi || !admitted())
        {
            throw new InvalidOperationException("Exact history requires current original native-user read admission.");
        }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Evidence);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(ExactGrantHistoryPage.RequestSeconds));
        try
        {
            var result = await Task.Run(() => ReadExactHistory(inspected, viewer, cursor, admitted, deadline.Token),
                deadline.Token).ConfigureAwait(false);
            RequireLive(viewer);
            if (!admitted()) { throw new InvalidOperationException("No late private exact history is admitted."); }
            deadline.Token.ThrowIfCancellationRequested();
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

    private ExactGrantHistoryPage ReadExactHistory(ExactGrantInspection inspected, HostRequest viewer,
        string? cursor, Func<bool> admitted, CancellationToken token)
    {
        using var lease = database.AcquireReadLease(token);
        var identity = RequireInspectionIdentity();
        if (!Same(inspected.StoreIdentity, identity) || !admitted())
        {
            throw new InvalidOperationException("The inspected private exact-grant source or read admission changed.");
        }
        using var connection = database.OpenReadOnly(token);
        using var transaction = connection.BeginTransaction(deferred: true);
        var now = time.GetUtcNow();
        var continuation = cursor is null ? null : DecodeExactHistoryCursor(cursor, inspected.Grant.Id, viewer.SessionId, identity, now);
        var snapshot = ReadAuditSnapshot(connection, identity, continuation?.Snapshot);
        var current = ReadExactGrant(connection, inspected.Grant.Id, identity);
        if (current is null)
        {
            if (continuation is not null) { throw new InvalidDataException("The exact retained continuation record is unavailable."); }
            VerifyExactHistoryRead(identity, admitted, token);
            return new(null, [], null, now, snapshot.AuthorityCeiling!.Value, snapshot.AuthorityCeilingDigest!, 0, 0, false);
        }
        if (current.Grant.Revision.Value < inspected.Grant.Revision.Value
            || current.Grant.ApprovedProposal != inspected.Grant.ApprovedProposal)
        {
            throw new InvalidDataException("The exact retained grant was replaced by older or different authority.");
        }
        var after = continuation?.After;
        if (after is not null)
        {
            using var predecessor = connection.CreateCommand();
            predecessor.CommandText = """
                SELECT a.correlation_id,a.hash,a.previous_hash,
                    CASE WHEN length(CAST(a.envelope AS BLOB))<=131072 THEN a.envelope ELSE NULL END,p.hash
                FROM security_audit_events a LEFT JOIN security_audit_events p ON p.sequence=a.sequence-1
                WHERE a.sequence=$sequence;
                """;
            predecessor.Parameters.AddWithValue("$sequence", after.CommittedTicks);
            using var row = predecessor.ExecuteReader();
            if (!row.Read() || row.IsDBNull(3) || !Same(row.GetString(0), after.Id) || !Same(row.GetString(1), after.CommitDigest!)
                || !Same(row.GetString(2), after.CommittedTicks == 1 ? EmptyHash : row.IsDBNull(4) ? string.Empty : row.GetString(4)))
            {
                throw new InvalidDataException("The exact history continuation predecessor is missing or changed.");
            }
            _ = DecodeAuthorityAudit(after.CommittedTicks, row.GetString(0), row.GetString(2), row.GetString(1), row.GetString(3));
        }
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.sequence,a.correlation_id,a.previous_hash,a.hash,
                CASE WHEN length(CAST(a.envelope AS BLOB))<=131072 THEN a.envelope ELSE NULL END,p.hash
            FROM security_audit_events a LEFT JOIN security_audit_events p ON p.sequence=a.sequence-1
            WHERE a.sequence>$after AND a.sequence<=$ceiling ORDER BY a.sequence LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$after", after?.CommittedTicks ?? 0);
        command.Parameters.AddWithValue("$ceiling", snapshot.AuthorityCeiling!.Value);
        command.Parameters.AddWithValue("$limit", ExactGrantHistoryPage.MaximumScannedRows);
        using var rows = command.ExecuteReader();
        var records = ImmutableArray.CreateBuilder<ExactGrantHistoryEntry>();
        var scannedThrough = after;
        var scanned = 0;
        var outputBound = false;
        var expires = continuation?.ExpiresAt ?? now.AddMinutes(ExactGrantHistoryPage.SnapshotMinutes);
        ExactGrantHistoryPage Page(bool more, bool limited) => new(current, records.ToImmutable(),
            more ? EncodeExactHistoryCursor(new(inspected.Grant.Id, viewer.SessionId, identity, expires, snapshot, scannedThrough!)) : null,
            now, snapshot.AuthorityCeiling.Value, snapshot.AuthorityCeilingDigest!, scannedThrough?.CommittedTicks ?? 0, scanned, limited);
        while (rows.Read())
        {
            token.ThrowIfCancellationRequested();
            if (!admitted()) { throw new InvalidOperationException("Exact history read admission changed during scanning."); }
            var sequence = rows.GetInt64(0);
            if (sequence != (scannedThrough?.CommittedTicks ?? 0) + 1 || rows.IsDBNull(4)
                || !Same(rows.GetString(2), sequence == 1 ? EmptyHash : rows.IsDBNull(5) ? string.Empty : rows.GetString(5)))
            {
                throw new InvalidDataException("The exact history prefix is oversized, missing or has an inconsistent predecessor.");
            }
            var audit = DecodeAuthorityAudit(sequence, rows.GetString(1), rows.GetString(2), rows.GetString(3), rows.GetString(4));
            scanned++;
            var position = new EvidencePosition(sequence, EvidenceSource.AuthorityAudit, rows.GetString(1), -1, rows.GetString(3));
            var entry = ExactHistoryEntry(audit, inspected.Grant.Id, sequence, rows.GetString(3), now);
            if (entry?.Provenance.Changes.Any(change => change.Revision > current.Grant.Revision.Value
                || change.Revision == current.Grant.Revision.Value && !Same(change.Digest, GrantChange(current.Grant).Digest)) == true)
            {
                throw new InvalidDataException("The retained exact grant disagrees with its committed history.");
            }
            if (entry is not null)
            {
                records.Add(entry);
                var previousPosition = scannedThrough;
                scannedThrough = position;
                if (records.Count > ExactGrantHistoryPage.MaximumRecords
                    || ExactGrantHistoryPage.Serialize(Page(sequence < snapshot.AuthorityCeiling, false)).Length > ExactGrantHistoryPage.MaximumBytes)
                {
                    records.RemoveAt(records.Count - 1);
                    scannedThrough = previousPosition;
                    if (records.Count == 0) { throw new InvalidDataException("Exact history metadata exceeds the complete serialized page budget."); }
                    outputBound = true;
                    break;
                }
            }
            scannedThrough = position;
            if (records.Count == ExactGrantHistoryPage.MaximumRecords) { outputBound = true; break; }
        }
        var more = (scannedThrough?.CommittedTicks ?? 0) < snapshot.AuthorityCeiling;
        var limited = scanned == ExactGrantHistoryPage.MaximumScannedRows && more;
        if (more && scanned < ExactGrantHistoryPage.MaximumScannedRows && !outputBound)
        {
            throw new InvalidDataException("The exact history snapshot contains a missing suffix.");
        }
        var page = Page(more, limited);
        if (ExactGrantHistoryPage.Serialize(page).Length > ExactGrantHistoryPage.MaximumBytes)
        {
            throw new InvalidDataException("Exact history exceeds the complete serialized output budget.");
        }
        VerifyExactHistoryRead(identity, admitted, token);
        if (ReadExactGrant(connection, inspected.Grant.Id, identity) != current)
        {
            throw new InvalidDataException("The retained exact grant changed during its history read.");
        }
        return page;
    }

    private void VerifyExactHistoryRead(string identity, Func<bool> admitted, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        database.VerifyFiles();
        if (!Same(identity, RequireInspectionIdentity()) || !admitted())
        {
            throw new InvalidOperationException("The private source or native exact history read admission changed.");
        }
    }

    private static ExactGrantHistoryEntry? ExactHistoryEntry(AuthorityAudit audit, HostId<ApprovalIdentity> id,
        long sequence, string digest, DateTimeOffset now)
    {
        var provenance = AuditProvenance(audit, sequence, digest);
        var changes = provenance.Changes.Where(change => Same(change.Kind, "grant") && Same(change.Id, Id(id))).ToArray();
        var requested = provenance.RequestedGrants?.Where(change => Same(change.Kind, "grant") && Same(change.Id, Id(id))).ToArray();
        // ApprovalId on a generic approval audit can be a QUESTION identity. Never treat that alone as grant membership.
        if (changes.Length == 0 && requested is not { Length: > 0 } && provenance.GrantId != id
            && !(Same(audit.Audit.ActionId, "approval.revoke-exact") && audit.RequestedAudit?.ApprovalId == id.Value))
        {
            return null;
        }
        return new(audit.Request, audit.CommittedAt, audit.DueAt,
            audit.DueAt <= now ? EvidenceSegmentStatus.ExpiredButPresent : EvidenceSegmentStatus.Present, audit.Audit,
            provenance with { Changes = changes, RequestedGrants = requested },
            provenance.Changes.Count - changes.Length, (provenance.RequestedGrants?.Count ?? 0) - (requested?.Length ?? 0));
    }

    private string EncodeExactHistoryCursor(ExactHistoryContinuation value)
    {
        var bytes = Encoding.UTF8.GetBytes(HostInteractionCodec.Encode(value));
        return Convert.ToBase64String(bytes) + "." + Convert.ToBase64String(HMACSHA256.HashData(exactHistoryCursorKey, bytes));
    }

    private ExactHistoryContinuation DecodeExactHistoryCursor(string cursor, HostId<ApprovalIdentity> id,
        HostId<SessionIdentity> viewer, string identity, DateTimeOffset now)
    {
        try
        {
            if (cursor.Length > 2048) { throw new InvalidDataException("The exact history cursor exceeds its bound."); }
            var parts = cursor.Split('.');
            if (parts.Length != 2) { throw new InvalidDataException("The exact history cursor is malformed."); }
            var bytes = Convert.FromBase64String(parts[0]);
            if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(exactHistoryCursorKey, bytes), Convert.FromBase64String(parts[1])))
            {
                throw new InvalidDataException("The exact history cursor is not issued by this host store lifetime.");
            }
            var value = HostInteractionCodec.Decode<ExactHistoryContinuation>(Encoding.UTF8.GetString(bytes));
            if (value.GrantId != id || value.Viewer != viewer || !Same(value.StoreIdentity, identity) || value.ExpiresAt <= now
                || value.After.Source != EvidenceSource.AuthorityAudit || value.After.Ordinal != -1
                || value.After.CommittedTicks <= 0 || value.After.CommittedTicks > value.Snapshot.AuthorityCeiling
                || !IsHex(value.After.CommitDigest ?? string.Empty, 64))
            {
                throw new InvalidDataException("The exact history cursor is foreign, stale or has an invalid position.");
            }
            return value;
        }
        catch (FormatException exception) { throw new InvalidDataException("The exact history cursor is malformed.", exception); }
    }

    private sealed record ExactHistoryContinuation(HostId<ApprovalIdentity> GrantId, HostId<SessionIdentity> Viewer,
        string StoreIdentity, DateTimeOffset ExpiresAt, EvidenceSnapshot Snapshot, EvidencePosition After);
}
