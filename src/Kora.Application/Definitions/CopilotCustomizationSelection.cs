using Kora.Core.Agents;
using Kora.Core.Artifacts;
using Kora.Core.Customizations;
using Kora.Core.Skills;

using NativeSelection = Kora.Core.Dependencies.CopilotCustomizationSelection;

namespace Kora.Application.Definitions;

internal static class CopilotCustomizationSelection
{
    internal static NativeSelection Parse(CustomizationSnapshot snapshot)
    {
        if (snapshot.Source.UnavailableReason is not null || snapshot.Files.Count != 1
            || snapshot.Files[0].HasBom || snapshot.RecoveryDetails is not null)
        { throw new InvalidDataException("Native hosted selection currently supports one complete UTF-8 primary file without companions, recovery or a BOM."); }
        var source = snapshot.Source;
        var file = snapshot.Files[0];
        if (source.Kind == CustomizationKind.Skill)
        {
            var skill = SharedSkillSnapshot.Parse(Guid.NewGuid(), file.Name, file.CopyBytes(), false);
            if (!skill.IsInstructionCompatible)
            { throw new InvalidDataException("This selected skill is unavailable: " + string.Join(", ", skill.UnavailableReasons)); }
            var document = ArtifactFileDocument.Parse(file.CopyBytes(), ArtifactKind.Skill, "copilot-selection", source.Name);
            return new(true, source.Id, snapshot.Revision, document.Definition.Name, document.Definition.Description, file.Text, document.Body);
        }
        if (source.Kind == CustomizationKind.Agent)
        {
            var document = ArtifactFileDocument.Parse(file.CopyBytes(), ArtifactKind.Agent, "copilot-selection", source.Name);
            var profile = BoundedAgentProfile.Parse(document);
            if (!string.Equals(document.Metadata["kora-agent-format"], "1", StringComparison.Ordinal) || !profile.Tools.IsEmpty)
            { throw new InvalidDataException("Only a reference-free tool-free native v1 agent can be copied to this hosted request."); }
            var guidance = SharedSkillSnapshot.Parse(Guid.NewGuid(), "SKILL.md",
                ArtifactFileDocument.Encode("---\nname: reviewed\nversion: 1.0.0\ndescription: Compatibility check\n---\n" + document.Body, false), false);
            if (!guidance.IsInstructionCompatible)
            { throw new InvalidDataException("Agent guidance contains unsupported active content or references: " + string.Join(", ", guidance.UnavailableReasons)); }
            return new(false, source.Id, snapshot.Revision, document.Definition.Name,
                document.Definition.Description, file.Text, document.Body);
        }
        throw new InvalidDataException("Choose one compatible skill or custom agent, not an instruction, prompt or MCP definition.");
    }
}
