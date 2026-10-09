using Kora.Core.Artifacts;

namespace Kora.Application.ViewModels;

public sealed record ArtifactCommandOption(
    string Command,
    string Name,
    string Description,
    ArtifactKind Kind,
    string Source)
{
    internal static ArtifactCommandOption From(ArtifactDefinition artifact) =>
        new($"/{artifact.CommandName}", artifact.Name, artifact.Description, artifact.Kind, artifact.Source);
}
