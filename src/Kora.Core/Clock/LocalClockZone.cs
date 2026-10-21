namespace Kora.Core.Clock;

/// <summary>Identifies the explicit display zone for a host clock answer.</summary>
public enum LocalClockZone
{
    /// <summary>Uses the current host-local timezone.</summary>
    HostLocal,

    /// <summary>Uses UTC only when explicitly requested.</summary>
    Utc,
}
