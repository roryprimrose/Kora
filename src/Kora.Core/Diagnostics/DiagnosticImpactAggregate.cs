namespace Kora.Core.Diagnostics;

public sealed record DiagnosticImpactAggregate(int NewlyDueCount, DateTimeOffset? OldestCommittedUtc,
    DateTimeOffset? NewestCommittedUtc, DateTimeOffset? EarliestProposedDueUtc, DateTimeOffset? LatestProposedDueUtc);
