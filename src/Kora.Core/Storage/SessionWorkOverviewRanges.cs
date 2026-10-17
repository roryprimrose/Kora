using System.Runtime.InteropServices;

namespace Kora.Core.Storage;

/// <summary>Authoritative passive work-metadata query contract, never freshness, completeness or authority.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SessionWorkOverviewRanges(
    SessionWorkMetadataRange AuthorityRevision = default,
    SessionWorkMetadataRange QueueRevision = default,
    SessionWorkMetadataRange PendingCount = default,
    SessionWorkMetadataRange PendingCapacity = default,
    SessionWorkMetadataRange ExecutionSlots = default,
    SessionWorkMetadataRange ObservedCurrentFixedReads = default,
    SessionWorkMetadataRange OmittedQueueRecords = default,
    SessionWorkMetadataRange OmittedTasks = default,
    SessionWorkMetadataRange OmittedQuestions = default)
{
    public void Validate()
    {
        AuthorityRevision.Validate(positive: true);
        QueueRevision.Validate(positive: false);
        PendingCount.Validate(positive: false);
        PendingCapacity.Validate(positive: true);
        ExecutionSlots.Validate(positive: true);
        ObservedCurrentFixedReads.Validate(positive: false);
        OmittedQueueRecords.Validate(positive: false);
        OmittedTasks.Validate(positive: false);
        OmittedQuestions.Validate(positive: false);
    }

    public bool Matches(SessionWorkOverviewRow row)
    {
        Validate();
        if (this == default) { return true; }
        return row.Availability == SessionWorkOverviewAvailability.Observed && row.Work is { } work
            && AuthorityRevision.Contains(work.AuthorityRevision)
            && QueueRevision.Contains(work.QueueRevision)
            && PendingCount.Contains(work.PendingCount)
            && PendingCapacity.Contains(work.PendingCapacity)
            && ExecutionSlots.Contains(work.ExecutionSlots)
            && ObservedCurrentFixedReads.Contains(work.ObservedCurrentFixedReads)
            && OmittedQueueRecords.Contains(work.OmittedQueueRecords)
            && OmittedTasks.Contains(work.OmittedTasks)
            && OmittedQuestions.Contains(work.OmittedQuestions);
    }
}
