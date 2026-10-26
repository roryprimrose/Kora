namespace Kora.Core.Communication;

/// <summary>Health of a bounded, content-free platform observation.</summary>
public enum CommunicationActivityHealth
{
    /// <summary>The source is reconciling its current scope.</summary>
    Starting,
    /// <summary>The declared scope is currently observed.</summary>
    Healthy,
    /// <summary>The exact source profile is unavailable.</summary>
    Unavailable,
    /// <summary>The source or resource release failed.</summary>
    Failed,
    /// <summary>The observed device changed or disappeared.</summary>
    DeviceLost,
    /// <summary>The observed Windows session or privacy eligibility changed.</summary>
    SessionLost,
}