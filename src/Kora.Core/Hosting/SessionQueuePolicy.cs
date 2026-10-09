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
            .Where(candidate => candidate.Head is { } head && Eligibility(head, entries, tasks,
                runId, now, admissionRevision, limits) == SessionQueueEligibility.Ready)
            .OrderBy(candidate => candidate.Last)
            .ThenBy(candidate => candidate.Head!.Position)
            .ThenBy(candidate => candidate.Head!.Request.SessionId.Value)
            .Select(candidate => candidate.Head).FirstOrDefault();
    }

    public static SessionQueueEligibility Eligibility(SessionQueueEntry entry,
        IReadOnlyList<SessionQueueEntry> entries, IReadOnlyDictionary<HostId<TaskIdentity>, HostTaskState> tasks,
        Guid runId, DateTimeOffset now, long admissionRevision, SessionQueueLimits limits,
        bool sessionActive = true, bool unclassifiedWorkOrWait = false, bool unknownWork = false)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(limits);
        entry.Validate();
        if (entry.State == SessionQueueState.Unknown) { return SessionQueueEligibility.UnknownQuarantine; }
        if (entry.State == SessionQueueState.Interrupted || entry.RunId != runId && entry.IsPending)
        {
            return SessionQueueEligibility.InterruptedNoReplay;
        }
        if (entry.IsCurrent) { return entry.RunId == runId ? SessionQueueEligibility.Current : SessionQueueEligibility.UnknownQuarantine; }
        if (entry.State == SessionQueueState.Expired || entry.IsPending && entry.ExpiresAt <= now) { return SessionQueueEligibility.Expired; }
        if (!entry.IsPending) { return SessionQueueEligibility.Completed; }
        if (!sessionActive) { return SessionQueueEligibility.SessionDone; }
        if (unknownWork || entries.Any(other => other.Request.SessionId == entry.Request.SessionId && other.State == SessionQueueState.Unknown))
        {
            return SessionQueueEligibility.UnknownQuarantine;
        }
        if (unclassifiedWorkOrWait) { return SessionQueueEligibility.UnclassifiedWorkOrWait; }
        if (entry.AdmissionRevision != admissionRevision) { return SessionQueueEligibility.AdmissionChanged; }
        if (entries.Any(other => other.Request.SessionId == entry.Request.SessionId && other.IsCurrent))
        {
            return SessionQueueEligibility.SessionCurrent;
        }
        if (entries.Any(other => other.Request.SessionId == entry.Request.SessionId && other.IsPending && other.Position < entry.Position))
        {
            return SessionQueueEligibility.EarlierPendingEntry;
        }
        if (entry.Dependency is { } dependency && (!tasks.TryGetValue(dependency, out var state) || state != HostTaskState.Succeeded))
        {
            return SessionQueueEligibility.DependencyNotSucceeded;
        }
        return entries.Count(other => other.IsCurrent && other.RunId == runId) >= limits.ExecutionSlots
            ? SessionQueueEligibility.GlobalCapacity : SessionQueueEligibility.Ready;
    }
}
