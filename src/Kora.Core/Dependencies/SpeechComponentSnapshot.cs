namespace Kora.Core.Dependencies;

public sealed record SpeechComponentSnapshot(bool Installed, bool Verified,
    SpeechComponentOwnership Ownership, string Stamp);
