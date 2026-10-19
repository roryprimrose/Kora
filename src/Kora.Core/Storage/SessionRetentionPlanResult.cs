namespace Kora.Core.Storage;

public sealed record SessionRetentionPlanResult(SessionRetentionPlanStatus Status,
    SessionRetentionPlanAggregate? Aggregate, string? Snapshot);
