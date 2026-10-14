namespace Kora.Core.Diagnostics;

public sealed record AuditImpactAggregate(long AffectedCount, DateTimeOffset? OldestCommittedUtc,
    DateTimeOffset? NewestCommittedUtc, DateTimeOffset? EarliestDueUtc, DateTimeOffset? LatestDueUtc);
