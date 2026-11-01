using Kora.Core.Commands;

namespace Kora.Core.Artifacts;

public static class ArtifactPublicationValidation
{
    public static void RequireUnshadowed(ArtifactDefinition proposed,
        IReadOnlyList<ArtifactDefinition> candidates, BuiltInCommandRouter controls)
    {
        if (proposed.CommandName is "skill" or "skills" or "instruction" or "instructions" or "prompt"
            or "prompts" or "agent" or "agents" or "definitions" or "help" or "exit" or "settings")
        { throw new InvalidDataException("The definition cannot shadow a reserved control."); }
        if (proposed.SpokenNames.Any(name => new[] { "use " + name, "run " + name }.Any(phrase => controls.Match(phrase).IsMatch)))
        { throw new InvalidDataException("The declared definition name would shadow a registered built-in command."); }
        using var validated = new ArtifactCatalogue([.. candidates, proposed]);
    }
}
