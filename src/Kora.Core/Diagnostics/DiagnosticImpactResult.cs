namespace Kora.Core.Diagnostics;

public sealed record DiagnosticImpactResult(DiagnosticImpactSource Source, DiagnosticImpactStatus Status,
    DiagnosticImpactAggregate? Aggregate);
