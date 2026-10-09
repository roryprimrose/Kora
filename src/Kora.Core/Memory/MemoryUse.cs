using Kora.Core.Hosting;

namespace Kora.Core.Memory;

public sealed record MemoryUse(
    HostId<MemoryIdentity> Id, HostRevision Revision, MemoryScope Scope, MemoryLineage Lineage,
    MemoryCandidate Candidate, MemoryReviewReceipt Receipt, HostRequest UsedBy);
