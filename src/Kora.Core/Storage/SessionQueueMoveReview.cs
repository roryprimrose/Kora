using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Exact host-observed pair; a review is not dispatch, action approval or a retargetable position.</summary>
public sealed record SessionQueueMoveReview(Guid Id, SessionQueueSnapshot Queue, SessionQueueEntry Selected,
    SessionQueueEntry Neighbor, bool Up, long AdmissionRevision, DateTimeOffset ObservedAt);
