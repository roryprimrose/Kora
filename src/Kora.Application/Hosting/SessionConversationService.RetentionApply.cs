using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    internal async Task<T> WithIdleRetentionAsync<T>(HostId<SessionIdentity> session,
        Func<Task<T>> operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (cancellationGate)
        {
            RequireConversationDispatchOpen(session);
            if (pendingClosed || lifecycleUnconfirmed || runningConversations.ContainsKey(session)
                || lifecycleSubmissions.Any(item => item.Target.Authority.SessionId == session)
                || conversationLanes.TryGetValue(session, out var lane) && lane.Entries.Count != 0)
            { throw new InvalidOperationException("Apply-now requires host-confirmed idle, joined resources. Live, queued or unconfirmed work cannot be confirmed."); }
            stopAndDeleteHolds.Add(session);
        }
        try { return await operation().ConfigureAwait(false); }
        finally { lock (cancellationGate) { stopAndDeleteHolds.Remove(session); } }
    }
}
