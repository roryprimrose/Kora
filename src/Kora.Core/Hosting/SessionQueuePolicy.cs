namespace Kora.Core.Hosting;

public static class SessionQueuePolicy
{
    public static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan ActiveDeadline = TimeSpan.FromMinutes(5);

    /// <summary>Round-robin by last successful admission, FIFO within each session; blocked heads never bypass.</summary>
    public static SessionQueueEntry? SelectReady(IReadOnlyList<SessionQueueEntry> entries,
        IReadOnlyDictionary<HostId<TaskIdentity>, HostTaskState> tasks, Guid runId,
        DateTimeOffset now, long admissionRevision, SessionQueueLimits limits)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(limits);
        if (entries.Count(entry => entry.IsCurrent && entry.RunId == runId) >= limits.ExecutionSlots)
        {
            return null;
        }
        return entries.GroupBy(entry => entry.Request.SessionId)
            .Where(group => !group.Any(entry => entry.IsCurrent || entry.State == SessionQueueState.Unknown))
            .Select(group => new
            {
                Head = group.Where(entry => entry.IsPending).OrderBy(entry => entry.Position).FirstOrDefault(),
                Last = group.Max(entry => entry.DispatchOrder),
            })
            .Where(candidate => candidate.Head is { } head && head.RunId == runId
                && head.AdmissionRevision == admissionRevision && head.ExpiresAt > now
                && (head.Dependency is null || tasks.TryGetValue(head.Dependency.Value, out var state)
                    && state == HostTaskState.Succeeded))
            .OrderBy(candidate => candidate.Last)
            .ThenBy(candidate => candidate.Head!.Position)
            .ThenBy(candidate => candidate.Head!.Request.SessionId.Value)
            .Select(candidate => candidate.Head).FirstOrDefault();
    }
}
