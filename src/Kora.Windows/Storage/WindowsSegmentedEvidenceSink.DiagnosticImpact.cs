using Kora.Core.Configuration;
using Kora.Core.Diagnostics;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSegmentedEvidenceSink
{
    internal string DiagnosticImpactStamp() => AuditImpactStamp();

    internal DiagnosticImpactSnapshot ReadDiagnosticImpact(DiagnosticRetentionDays proposed, DateTimeOffset cutoff,
        CancellationToken token) => ReadSegments(set =>
    {
        var stamp = DiagnosticImpactStamp();
        // Budgets belong to the whole retained inventory, not to each independently rotated partition.
        var scan = new DiagnosticImpactScan(proposed, cutoff);
        foreach (var segment in set.Segments)
        {
            token.ThrowIfCancellationRequested();
            segment.Sink.AccumulateDiagnosticImpact(scan, token);
            if (!scan.ProtectedPrefixComplete) { break; }
        }
        token.ThrowIfCancellationRequested();
        if (!string.Equals(stamp, DiagnosticImpactStamp(), StringComparison.Ordinal))
        {
            throw new InvalidDataException("The segmented diagnostic store changed during preview.");
        }
        return scan.Finish(stamp);
    }, token);
}
