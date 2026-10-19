using Kora.Core.Configuration;
using Kora.Core.Diagnostics;

namespace Kora.Windows.Storage;

internal sealed class DiagnosticImpactScan(DiagnosticRetentionDays proposed, DateTimeOffset cutoff)
{
    internal DiagnosticRetentionImpact ProtectedPrefix { get; } = new(proposed, cutoff);
    internal DiagnosticRetentionImpact Logs { get; } = new(proposed, cutoff);
    internal DiagnosticRetentionImpact Spans { get; } = new(proposed, cutoff);
    internal DiagnosticRetentionImpact Links { get; } = new(proposed, cutoff);
    internal bool ProtectedPrefixComplete { get; set; } = true;
    internal bool LogsAvailable { get; set; } = true;
    internal bool OwnedAvailable { get; set; } = true;

    internal DiagnosticImpactSnapshot Finish(string stamp)
    {
        if (!ProtectedPrefixComplete)
        {
            return new(Unqualified(DiagnosticImpactSource.OrdinaryLogs, DiagnosticImpactStatus.Incomplete),
                Unqualified(DiagnosticImpactSource.Spans, DiagnosticImpactStatus.Incomplete),
                Unqualified(DiagnosticImpactSource.OwnedLinks, DiagnosticImpactStatus.Incomplete), stamp);
        }
        var logs = LogsAvailable ? Logs.Finish(DiagnosticImpactSource.OrdinaryLogs)
            : Unqualified(DiagnosticImpactSource.OrdinaryLogs, DiagnosticImpactStatus.Unavailable);
        var spans = OwnedAvailable ? Spans.Finish(DiagnosticImpactSource.Spans)
            : Unqualified(DiagnosticImpactSource.Spans, DiagnosticImpactStatus.Unavailable);
        var links = spans.Status == DiagnosticImpactStatus.Complete ? Links.Finish(DiagnosticImpactSource.OwnedLinks)
            : Unqualified(DiagnosticImpactSource.OwnedLinks, spans.Status);
        return new(logs, spans, links, stamp);
    }

    private static DiagnosticImpactResult Unqualified(DiagnosticImpactSource source, DiagnosticImpactStatus status) =>
        new(source, status, null);
}
