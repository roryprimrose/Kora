using System.Collections.ObjectModel;

using Kora.Core.Commands;

namespace Kora.Core.Artifacts;

public sealed class ArtifactCatalogue
{
    public ArtifactCatalogue(IReadOnlyList<ArtifactDefinition> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        if (artifacts.Count is 0 or > 128 || artifacts.Any(artifact => artifact is null))
        {
            throw new ArgumentException("The artifact catalogue must be nonempty and bounded.", nameof(artifacts));
        }
        if (artifacts.Select(artifact => artifact.Id).Distinct(StringComparer.Ordinal).Count() != artifacts.Count
            || artifacts.Select(artifact => artifact.CommandName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != artifacts.Count)
        {
            throw new ArgumentException("Artifact IDs and slash commands must be unique.", nameof(artifacts));
        }
        var duplicateSpokenName = artifacts
            .SelectMany(artifact => artifact.SpokenNames.Select(name => (
                Name: BuiltInCommandRouter.Normalize(name),
                Artifact: artifact)))
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Select(item => item.Artifact.Id).Distinct(StringComparer.Ordinal).Skip(1).Any());
        if (duplicateSpokenName is not null)
        {
            throw new ArgumentException(
                $"The normalized spoken artifact name “{duplicateSpokenName.Key}” is ambiguous.",
                nameof(artifacts));
        }
        Artifacts = new ReadOnlyCollection<ArtifactDefinition>(artifacts.ToArray());
    }

    public ReadOnlyCollection<ArtifactDefinition> Artifacts { get; }
}
