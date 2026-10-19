using System.Text;
using System.Text.Json;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteEvidenceSink
{
    internal string AuditImpactStamp() => database.ReadPreviewStamp();

    internal AuditImpactResult ReadAuditImpact(AuditRetentionDays proposed, DateTimeOffset cutoff,
        CancellationToken token)
    {
        var stamp = AuditImpactStamp();
        var impact = new AuditRetentionImpact(proposed, cutoff);
        AccumulateAuditImpact(impact, token);
        token.ThrowIfCancellationRequested();
        if (!string.Equals(stamp, AuditImpactStamp(), StringComparison.Ordinal)) { throw new InvalidDataException("The projection store changed during preview."); }
        return impact.Finish(AuditImpactSource.DiagnosticProjection, stamp);
    }

    internal bool AccumulateAuditImpact(AuditRetentionImpact impact, CancellationToken token)
    {
        using var lease = database.AcquireReadLease(token);
        // This path validates only the independently typed audit projection, not diagnostic lookalikes.
        using var connection = database.OpenReadOnly(token);
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT * FROM security_audit_events ORDER BY audit_sequence
            LIMIT {AuditRetentionImpact.MaximumRows + 1};
            """;
        using var rows = command.ExecuteReader();
        long sequence = 0;
        var complete = true;
        while (rows.Read())
        {
            token.ThrowIfCancellationRequested();
            var payload = rows.GetString(rows.GetOrdinal("envelope"));
            using (var document = JsonDocument.Parse(payload)) { RequireUnique(document.RootElement); }
            var projection = DecodeProjection("security_audit_events", payload);
            foreach (var pair in projection.Projection)
            {
                if (!Equals(rows.GetValue(rows.GetOrdinal(pair.Key)), pair.Value))
                {
                    throw new InvalidDataException("The typed audit projection disagrees with its committed columns.");
                }
            }
            if (rows.GetInt64(rows.GetOrdinal("audit_sequence")) != ++sequence)
            {
                throw new InvalidDataException("The audit projection sequence is discontinuous.");
            }
            if (!impact.Add(ReadTimestamp(rows, "committed_utc"), ReadTimestamp(rows, "due_utc"),
                Encoding.UTF8.GetByteCount(payload)))
            {
                complete = false;
                break;
            }
        }
        rows.Close();
        if (complete) { ValidateAuditSequence(connection); }
        token.ThrowIfCancellationRequested();
        return complete;
    }
}
