namespace Kora.Core.Dependencies;

/// <summary>Exact host-owned asset mutation admission, not runtime selection or microphone authority.</summary>
public enum SpeechComponentMutationState
{
    /// <summary>All consumers are joined and new consumers excluded by the returned lease.</summary>
    Acquired,
    /// <summary>No authoritative mutation adapter is available.</summary>
    Unavailable,
    /// <summary>A known consumer is still using the data.</summary>
    Busy,
    /// <summary>The host cannot establish exclusive ownership or quiescence.</summary>
    UnknownOwnership,
    /// <summary>The data belongs to shared consumers or another owner.</summary>
    SharedOrUserOwned,
    /// <summary>Current host policy denies mutation.</summary>
    Denied,
}
