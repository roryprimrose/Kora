namespace Kora.Core.Diagnostics;

public sealed record AuditImpactResult(AuditImpactSource Source, AuditImpactStatus Status,
    AuditImpactAggregate? Aggregate, string? Snapshot);
