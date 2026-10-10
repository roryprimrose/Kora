namespace Kora.Core.Storage;

/// <summary>A committed swap is not a disclosed source until old owned copies are certified.</summary>
public sealed class SessionFileReplacementHeldException()
    : InvalidOperationException("Replacement copy verification is held. Review removal of the held snapshot; no old source is restored.");
