namespace Kora.Core.Dependencies;

/// <summary>Existing authoritative functional evidence, never implied by data presence or inspection.</summary>
public enum SpeechComponentFunctionalEvidence
{
    /// <summary>No functional verification has been performed.</summary>
    NotChecked,
    /// <summary>The exact host adapter established functional verification.</summary>
    Verified,
    /// <summary>An explicit verification failed.</summary>
    Failed,
    /// <summary>The host cannot establish the functional evidence state.</summary>
    Unknown,
    /// <summary>No authoritative functional verifier is available.</summary>
    Unavailable,
}
