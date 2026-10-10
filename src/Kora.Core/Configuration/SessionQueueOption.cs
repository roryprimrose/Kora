namespace Kora.Core.Configuration;

/// <summary>Admitted future-only fixed-profile queue options; automatic dispatch remains unavailable.</summary>
public enum SessionQueueOption
{
    /// <summary>Future per-session pending admission capacity; never evicts existing entries.</summary>
    PendingPerSession,
    /// <summary>Future global admission slots for synchronous read-only local-version work only.</summary>
    ExecutionSlots,
    /// <summary>Pending lifetime in integer minutes captured only by newly enqueued admitted fixed reads.</summary>
    PendingLifetimeMinutes,
    /// <summary>Active budget in integer minutes captured only at future fixed read admissions.</summary>
    ActiveBudgetMinutes,
}
