namespace Kora.Core.Diagnostics;

/// <summary>The opaque snapshot is host-private currentness evidence, never presentation content.</summary>
public sealed record DiagnosticImpactSnapshot(DiagnosticImpactResult Logs, DiagnosticImpactResult Spans,
    DiagnosticImpactResult Links, string? Snapshot)
{
    public static DiagnosticImpactSnapshot Unavailable { get; } = new(
        new(DiagnosticImpactSource.OrdinaryLogs, DiagnosticImpactStatus.Unavailable, null),
        new(DiagnosticImpactSource.Spans, DiagnosticImpactStatus.Unavailable, null),
        new(DiagnosticImpactSource.OwnedLinks, DiagnosticImpactStatus.Unavailable, null), null);
}
