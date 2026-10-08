using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Host-owned fixed local-version queue only. No arbitrary action, resource or provider descriptor.</summary>
public interface ISessionQueueStore
{
    ValueTask<SessionQueueSnapshot> ReadQueueAsync(HostId<SessionIdentity> session, CancellationToken token);
    ValueTask<SessionQueueEntry?> ReadQueueEntryAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task, CancellationToken token);
    ValueTask<SessionQueueSnapshot> EnqueueAsync(HostRequest control, HostRequest work, HostRevision generation,
        long expectedRevision, long admissionRevision, HostId<TaskIdentity>? dependency,
        SessionQueueLimits limits, Func<bool> eligible, CancellationToken token);
    ValueTask<SessionQueueSnapshot> RemovePendingAsync(HostRequest control, HostRevision generation, long expectedRevision,
        HostId<TaskIdentity>? task, HostRevision? entryRevision, SessionQueueState outcome,
        Func<bool> eligible, CancellationToken token);
    ValueTask<SessionQueueEntry?> FindReadyAsync(long admissionRevision, SessionQueueLimits limits, CancellationToken token);
    ValueTask<SessionQueueEntry> AdmitAsync(SessionQueueEntry expected, long admissionRevision,
        SessionQueueLimits limits, Func<bool> eligible, CancellationToken token);
    ValueTask<SessionQueueEntry> CompleteAsync(SessionQueueEntry expected, SessionQueueState outcome,
        Func<bool> eligible, CancellationToken token);
}
