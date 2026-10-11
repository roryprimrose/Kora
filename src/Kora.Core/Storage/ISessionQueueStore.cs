using Kora.Core.Hosting;
using Kora.Core.Tools;

namespace Kora.Core.Storage;

/// <summary>Host-owned fixed no-argument reads only. No arbitrary action, resource or provider descriptor.</summary>
public interface ISessionQueueStore
{
    ValueTask<SessionQueueSnapshot> ReadQueueAsync(HostId<SessionIdentity> session, CancellationToken token);
    ValueTask<SessionQueueEntry?> ReadQueueEntryAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task, CancellationToken token);
    /// <summary>Change exact current-run session dispatch admission only; committed work is unaffected.</summary>
    ValueTask<SessionQueueSnapshot> SetDispatchPausedAsync(HostRequest control, HostRevision generation,
        long expectedQueueRevision, Guid expectedRunId, long expectedControlRevision, long admissionRevision,
        bool paused, Func<bool> eligible, CancellationToken token);
    /// <summary>Hold only a matching committed run receipt whose delivery failed; never release or overwrite newer control.</summary>
    ValueTask HoldDispatchControlAsync(SessionQueueSnapshot expected, CancellationToken token);
    /// <summary>Swap only the exact reviewed adjacent pending pair under the authoritative admission fence.</summary>
    ValueTask<SessionQueueSnapshot> MovePendingAsync(HostRequest control, SessionQueueMoveReview review,
        Func<bool> eligible, CancellationToken token);
    ValueTask<SessionQueueSnapshot> EnqueueAsync(HostRequest control, HostRequest work, HostRevision generation,
        long expectedRevision, long admissionRevision, HostId<TaskIdentity>? dependency,
        SessionQueueLimits limits, Func<bool> eligible, CancellationToken token,
        SessionQueueReadProfile profile = SessionQueueReadProfile.Version);
    ValueTask<SessionQueueSnapshot> RemovePendingAsync(HostRequest control, HostRevision generation, long expectedRevision,
        HostId<TaskIdentity>? task, HostRevision? entryRevision, SessionQueueState outcome,
        Func<bool> eligible, CancellationToken token);
    ValueTask<SessionQueueEntry?> FindReadyAsync(long admissionRevision, SessionQueueLimits limits, CancellationToken token);
    ValueTask<SessionQueueAdmissionReceipt> AdmitAsync(SessionQueueEntry expected, long admissionRevision,
        SessionQueueLimits limits, Func<bool> eligible, CancellationToken token);
    ValueTask<SessionQueueEntry> CompleteAsync(SessionQueueEntry expected, SessionQueueState outcome,
        Func<bool> eligible, CancellationToken token, CapabilityReply? result = null);
}
