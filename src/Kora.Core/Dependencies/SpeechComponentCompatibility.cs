namespace Kora.Core.Dependencies;

/// <summary>Read-only compatibility of the exact installed host adapter, independently of model data.</summary>
public enum SpeechComponentCompatibility
{
    /// <summary>The host cannot establish compatibility.</summary>
    Unknown,
    /// <summary>The exact installed adapter and supported environment match.</summary>
    Compatible,
    /// <summary>The observed adapter or environment does not match.</summary>
    Incompatible,
    /// <summary>No authoritative adapter is available for this component.</summary>
    Unavailable,
}
