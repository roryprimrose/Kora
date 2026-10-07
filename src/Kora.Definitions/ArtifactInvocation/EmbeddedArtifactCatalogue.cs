using Kora.Core.Artifacts;
using Kora.Definitions.Skills;

namespace Kora.Definitions.Artifacts;

public static class EmbeddedArtifactCatalogue
{
    private static readonly Dictionary<string, (string Command, string[] SpokenNames)> Invocations =
        new Dictionary<string, (string Command, string[] SpokenNames)>(StringComparer.Ordinal)
        {
            ["kora.session.lock"] = ("lock", ["lock", "lock machine", "lock the machine"]),
            ["kora.computer.restart"] = ("restart", ["restart", "restart machine", "restart the machine"]),
            ["kora.computer.shutdown"] = ("shutdown", ["shutdown", "shutdown machine", "shut down the machine"]),
        };

    public static ArtifactCatalogue Load()
    {
        var skills = EmbeddedSkillCatalogue.Load();
        var artifacts = skills.Packages.Select(package =>
        {
            if (!Invocations.TryGetValue(package.Manifest.Id, out var invocation))
            {
                throw new InvalidDataException("An embedded skill has no explicit artifact invocation registration.");
            }
            var declaration = package.Manifest.Files.Single(file => file.Kind is "instructions");
            var instructions = package.Files.Single(file =>
                string.Equals(file.ResourceId, declaration.ResourceId, StringComparison.Ordinal));
            return new ArtifactDefinition(
                package.Manifest.Id,
                ArtifactKind.Skill,
                package.Manifest.Name,
                package.Manifest.Description,
                invocation.Command,
                invocation.SpokenNames,
                "bundled",
                package.Manifest.Version,
                package.DefinitionDigest,
                instructions.Text);
        }).ToArray();
        return new ArtifactCatalogue(artifacts);
    }
}
