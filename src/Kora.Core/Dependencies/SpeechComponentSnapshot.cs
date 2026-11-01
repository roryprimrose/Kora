namespace Kora.Core.Dependencies;

public sealed record SpeechComponentSnapshot(bool Installed, bool Verified,
    SpeechComponentOwnership Ownership, string Stamp)
{
    public long? ExactBytes { get; init; }
    public string? Inventory { get; init; }
    public string? DirectoryIdentity { get; init; }
    public string? FileInventoryStamp { get; init; }
}