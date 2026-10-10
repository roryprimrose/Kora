namespace Kora.Core.Configuration;

/// <summary>Admitted fixed-profile queue options; active deadline and automatic dispatch settings are unavailable.</summary>
public enum SessionQueueOption
{
    /// <summary>Future per-session pending admission capacity; never evicts existing entries.</summary>
    PendingPerSession,
    /// <summary>Future global admission slots for synchronous read-only local-version work only.</summary>
    ExecutionSlots,
    /// <summary>Pending lifetime in integer minutes captured only by newly enqueued admitted fixed reads.</summary>
    PendingLifetimeMinutes,
}
