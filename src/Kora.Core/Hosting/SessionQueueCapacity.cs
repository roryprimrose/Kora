namespace Kora.Core.Hosting;

/// <summary>Expired projected pending rows still occupy capacity until explicitly removed.</summary>
public static class SessionQueueCapacity
{
    public static int PendingCount(IEnumerable<SessionQueueEntry> entries, Guid runId) =>
        entries.Count(entry => entry.RunId == runId
            && entry.State is SessionQueueState.Pending or SessionQueueState.Expired);
}
