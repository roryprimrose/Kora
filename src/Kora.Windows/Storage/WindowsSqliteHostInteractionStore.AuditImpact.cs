using System.Text;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    internal string AuditImpactStamp()
    {
        _ = RequireInspectionIdentity();
        return database.ReadPreviewStamp();
    }

    internal AuditImpactResult ReadAuditImpact(AuditRetentionDays proposed, DateTimeOffset cutoff,
        CancellationToken token)
    {
        using var lease = database.AcquireReadLease(token);
        var identity = RequireInspectionIdentity();
        var stamp = AuditImpactStamp();
        using var connection = database.OpenReadOnly(token);
        using var transaction = connection.BeginTransaction(deferred: true);
        var snapshot = ReadAuditSnapshot(connection, identity, null);
        var impact = new AuditRetentionImpact(proposed, cutoff);
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT sequence,correlation_id,previous_hash,hash,envelope
            FROM security_audit_events ORDER BY sequence LIMIT {AuditRetentionImpact.MaximumRows + 1};
            """;
        using var rows = command.ExecuteReader();
        long sequence = 0;
        var hash = EmptyHash;
        var complete = true;
        while (rows.Read())
        {
            token.ThrowIfCancellationRequested();
            var payload = rows.GetString(4);
            var audit = DecodeAuthorityAudit(rows.GetInt64(0), rows.GetString(1), rows.GetString(2),
                rows.GetString(3), payload);
            if (rows.GetInt64(0) != ++sequence || !Same(hash, rows.GetString(2)))
            {
                throw new InvalidDataException("The committed audit prefix is inconsistent.");
            }
            hash = rows.GetString(3);
            if (!impact.Add(audit.CommittedAt, audit.DueAt, Encoding.UTF8.GetByteCount(payload)))
            {
                complete = false;
                break;
            }
        }
        if (complete && (sequence != snapshot.AuthorityCeiling || !Same(hash, snapshot.AuthorityCeilingDigest!)))
        {
            throw new InvalidDataException("The committed audit suffix is inconsistent.");
        }
        token.ThrowIfCancellationRequested();
        if (!Same(stamp, AuditImpactStamp())) { throw new InvalidDataException("The audit store changed during preview."); }
        return impact.Finish(AuditImpactSource.RequiredAuthority, stamp);
    }
}
