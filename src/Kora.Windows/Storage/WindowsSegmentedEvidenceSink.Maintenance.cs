using System.Security.Cryptography;
using System.Text;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSegmentedEvidenceSink
{
    private int retentionPosition;

    internal DiagnosticRetentionBatch PruneOrdinaryDiagnostics(DateTimeOffset cutoff, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (gate)
        {
            RequireAvailable();
            VerifyInventory();
            var segment = segments[retentionPosition];
            var sealedSegment = segment.Entry.Sealed;
            var beforeAudits = segment.Entry.AuditCount;
            // Once a sealed file is made writable, any incomplete maintenance holds admission.
            // Cancellation after a committed prune cannot relabel its receipt as a rollback.
            try
            {
                if (sealedSegment) { segment.UnsealForMaintenance(); }
                var result = segment.Sink.PruneOrdinaryDiagnostics(cutoff, token);
                if (sealedSegment)
                {
                    segment.AdmitAndSeal(cancellationToken: CancellationToken.None);
                    if (segment.Entry.AuditCount != beforeAudits)
                    {
                        throw new InvalidDataException("Diagnostic maintenance changed retained audit continuity.");
                    }
                }
                inventory = inventory! with
                {
                    Generation = checked(inventory.Generation + 1),
                    Segments = segments.Select(item => item.Entry).ToArray(),
                };
                PublishInventory(first: false, cancellationToken: CancellationToken.None);
                if (!result.HasMore) { retentionPosition = (retentionPosition + 1) % segments.Count; }
                return result with { HasMore = result.HasMore || retentionPosition != 0 };
            }
            catch (Exception exception)
            {
                failure = exception;
                throw;
            }
        }
    }

    internal string AuditImpactStamp()
    {
        lock (gate)
        {
            RequireAvailable();
            VerifyInventory();
            var stamps = new StringBuilder(inventory!.StoreIdentity);
            stamps.Append(':').Append(inventory.Generation);
            foreach (var segment in segments)
            {
                stamps.Append(':').Append(segment.Entry.Id).Append(':').Append(segment.Sink.AuditImpactStamp());
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamps.ToString())));
        }
    }

    internal AuditImpactResult ReadAuditImpact(AuditRetentionDays proposed, DateTimeOffset cutoff, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (gate)
        {
            RequireAvailable();
            var stamp = AuditImpactStamp();
            var impact = new AuditRetentionImpact(proposed, cutoff);
            foreach (var segment in segments)
            {
                token.ThrowIfCancellationRequested();
                if (segment.Entry.Sealed) { segment.VerifySeal(); }
                if (!segment.Sink.AccumulateAuditImpact(impact, token)) { break; }
            }
            if (!string.Equals(stamp, AuditImpactStamp(), StringComparison.Ordinal))
            {
                throw new InvalidDataException("The segmented evidence projection changed during preview.");
            }
            return impact.Finish(AuditImpactSource.DiagnosticProjection, stamp);
        }
    }
}
