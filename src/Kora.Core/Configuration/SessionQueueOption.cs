namespace Kora.Core.Configuration;

/// <summary>The two admitted fixed-profile queue options; deadline and automatic dispatch settings are unavailable.</summary>
public enum SessionQueueOption
{
    /// <summary>Future per-session pending admission capacity; never evicts existing entries.</summary>
    PendingPerSession,
    /// <summary>Future global admission slots for synchronous read-only local-version work only.</summary>
    ExecutionSlots,
}
