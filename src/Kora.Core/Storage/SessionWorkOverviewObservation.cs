using System.Collections.Immutable;

namespace Kora.Core.Storage;

/// <summary>Content-free work provenance and exact observed records. Stored lifecycle is on the containing session, not inferred from work.</summary>
public sealed record SessionWorkOverviewObservation(
    long AuthorityRevision, DateTimeOffset ObservedAt, long QueueRevision,
    int PendingCount, int PendingCapacity, int ExecutionSlots, int ObservedCurrentFixedReads,
    ImmutableArray<SessionQueueObservation> QueueRecords, int OmittedQueueRecords,
    ImmutableArray<SessionWorkOverviewTask> Tasks, int OmittedTasks,
    ImmutableArray<SessionWorkQuestion> PendingQuestions, int OmittedQuestions)
{
    public SessionQueueDispatchControl? DispatchControl { get; init; }
}
