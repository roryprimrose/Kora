using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Exact stored expired source and a separate fresh decision; neither is execution authority.</summary>
public sealed record SessionQueueRequeueReview(Guid Id, SessionQueueSnapshot Queue, SessionQueueEntry Original,
    HostTaskRecord OriginalTask, HostRequest FreshRequest, long AdmissionRevision, SessionQueueLimits Limits,
    long ConfigurationRevision, DateTimeOffset EnqueuedAt, DateTimeOffset ExpiresAt);
