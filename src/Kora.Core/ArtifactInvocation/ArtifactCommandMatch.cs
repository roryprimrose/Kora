namespace Kora.Core.Artifacts;

public sealed record ArtifactCommandMatch(
    bool IsArtifactCommand,
    ArtifactInvocation? Invocation,
    string? Error)
{
    public static ArtifactCommandMatch NotMatched { get; } = new(false, null, null);
}
