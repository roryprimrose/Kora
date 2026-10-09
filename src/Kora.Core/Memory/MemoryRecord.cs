using Kora.Core.Hosting;

namespace Kora.Core.Memory;

public sealed record MemoryRecord(
    HostId<MemoryIdentity> Id, HostRevision Revision, MemoryScope Scope, MemoryLineage Lineage,
    MemoryCandidate? Candidate, DateTimeOffset CreatedAt, MemoryReviewState Review,
    MemoryRetentionState Retention, MemoryReviewReceipt? Receipt);
