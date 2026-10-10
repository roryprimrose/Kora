using Kora.Core.Memory;

namespace Kora.Core.Commands;

public sealed record MemorySummary(Guid Id, long Revision, MemoryScope Scope, MemoryReviewState Review,
    MemoryRetentionState Retention, DateTimeOffset CreatedAt);
