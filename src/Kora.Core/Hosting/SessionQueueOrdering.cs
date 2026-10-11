namespace Kora.Core.Hosting;

/// <summary>One pending scheduling rule for admission and all passive work projections.</summary>
public static class SessionQueueOrdering
{
    private const string ExpiredReason = "An expired pair cannot move. Explicitly remove/requeue; original deadlines are never extended.";
    public static SessionQueueEntry? Head(IReadOnlyList<SessionQueueEntry> entries,
        HostId<SessionIdentity> session, IReadOnlyList<SessionQueueOrder>? orders = null)
    {
        var pending = Pending(entries, session, orders);
        return pending.Count == 0 ? null : pending[0];
    }

    public static IReadOnlyList<SessionQueueEntry> Pending(IReadOnlyList<SessionQueueEntry> entries,
        HostId<SessionIdentity> session, IReadOnlyList<SessionQueueOrder>? orders = null)
    {
        var pending = entries.Where(entry => entry.Request.SessionId == session && entry.IsPending).ToArray();
        return pending.OrderBy(entry => Key(entry, orders)).ThenBy(entry => entry.Position).ToArray();
    }

    public static long Key(SessionQueueEntry entry, IReadOnlyList<SessionQueueOrder>? orders = null)
    {
        var order = orders?.SingleOrDefault(order => order.SessionId == entry.Request.SessionId
            && order.RunId == entry.RunId && order.Generation == entry.Generation);
        // Original arrival slots remain immutable. A reviewed permutation only selects which pending identity fills a slot.
        return order is not null && order.Tasks.Contains(entry.Request.TaskId)
            ? order.Tasks.IndexOf(entry.Request.TaskId) + 1L : entry.Position;
    }

    public static string? Refusal(IReadOnlyList<SessionQueueEntry> pending, SessionQueueEntry selected,
        SessionQueueEntry neighbor, bool up, Guid runId, HostRevision generation, long admissionRevision, DateTimeOffset now)
    {
        if (selected.State == SessionQueueState.Expired || neighbor.State == SessionQueueState.Expired)
        {
            return ExpiredReason;
        }
        if (!selected.IsPending || !neighbor.IsPending || selected.RunId != runId || neighbor.RunId != runId
            || selected.Generation != generation || neighbor.Generation != generation
            || selected.Request.SessionId != neighbor.Request.SessionId || selected.Request.TaskId == neighbor.Request.TaskId)
        {
            return "Only two exact current-run pending entries in this active session generation can move.";
        }
        if (selected.ExpiresAt <= now || neighbor.ExpiresAt <= now)
        {
            return ExpiredReason;
        }
        if (selected.AdmissionRevision != admissionRevision || neighbor.AdmissionRevision != admissionRevision)
        {
            return "Private admission changed. Remove/requeue retired work rather than renewing it through reordering.";
        }
        var index = pending.ToList().IndexOf(selected);
        var adjacent = up ? index - 1 : index + 1;
        if (index < 0 || adjacent < 0 || adjacent >= pending.Count || pending[adjacent] != neighbor)
        {
            return "The exact displayed adjacent pair changed. Refresh and review again; no different neighbor is substituted.";
        }
        if (up ? selected.Dependency == neighbor.Request.TaskId : neighbor.Dependency == selected.Request.TaskId)
        {
            return "This move would place a pending dependent before its required pending prerequisite and strand this queue.";
        }
        return null;
    }
}
