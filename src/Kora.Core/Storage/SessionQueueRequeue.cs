using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Shared stored-state expiry boundary; projected Expired is never admission authority.</summary>
public static class SessionQueueRequeue
{
    public static void RequireExpired(SessionQueueEntry entry, HostTaskRecord task, Guid run,
        HostRevision generation, long admissionRevision, DateTimeOffset now)
    {
        entry.Validate();
        if (!entry.IsPending || entry.DispatchOrder != 0 || entry.RunId != run || entry.Generation != generation
            || entry.AdmissionRevision != admissionRevision || entry.EnqueuedAt.Offset != TimeSpan.Zero
            || entry.ExpiresAt.Offset != TimeSpan.Zero || entry.ExpiresAt > now
            || task.Request != entry.Request || task.State != HostTaskState.IntentRecorded)
        {
            throw new InvalidOperationException("Only exact expired stored pending work from this current run and unchanged private admission can be replaced.");
        }
    }
}
