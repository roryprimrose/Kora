using Kora.Core.Hosting;

namespace Kora.Core.Memory;

public sealed record MemoryReviewReceipt(
    HostId<RequestIdentity> Request, HostRevision Revision, DateTimeOffset ReviewedAt, MemoryBoundary Boundary);
