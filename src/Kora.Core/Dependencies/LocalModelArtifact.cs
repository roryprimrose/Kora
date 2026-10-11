using Kora.Core.Artifacts;

namespace Kora.Core.Dependencies;

public sealed record LocalModelArtifact(
    string Id,
    ArtifactKind Kind,
    string Name,
    string Source,
    string Version,
    string Digest,
    string Instructions)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsLiteralPrompt { get; init; }
}
