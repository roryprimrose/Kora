using System.Collections.Immutable;

using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Authoritative bounded pending subset validation shared by review and physical commit.</summary>
public static class SessionQueueCancellation
{
    public static ImmutableArray<SessionQueueEntry> Select(SessionQueueSnapshot snapshot,
        ImmutableArray<SessionQueueSelection> selection)
    {
        if (selection.IsDefaultOrEmpty || selection.Length > SessionQueueLimits.MaximumPendingPerSession
            || selection.Any(member => member is null || member.TaskId.Value == Guid.Empty || member.Revision.Value <= 0)
            || selection.Select(member => member.TaskId).Distinct().Count() != selection.Length
            || snapshot.DispatchControl is not { } control)
        {
            throw new InvalidOperationException("Select one to ten unique exact displayed pending IDs and revisions under exact current-run control.");
        }
        var selected = ImmutableArray.CreateBuilder<SessionQueueEntry>(selection.Length);
        var next = 0;
        foreach (var entry in snapshot.Entries)
        {
            if (next == selection.Length || entry.Request.TaskId != selection[next].TaskId) { continue; }
            if (entry.Request.SessionId != snapshot.SessionId || entry.Generation != snapshot.Generation
                || entry.RunId != control.RunId || entry.Revision != selection[next].Revision
                || entry.State is not (SessionQueueState.Pending or SessionQueueState.Expired))
            {
                throw new InvalidOperationException("One selected member is stale or not current-run pending; no subset is admitted.");
            }
            // Expiry is an observation of the original pending row, not renewed eligibility.
            selected.Add(entry with { State = SessionQueueState.Pending });
            next++;
        }
        if (next != selection.Length)
        {
            throw new InvalidOperationException("The complete selected subset must match the displayed current pending order. Refresh and review again.");
        }
        return selected.MoveToImmutable();
    }
}
