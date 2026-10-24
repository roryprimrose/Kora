using System.Collections.Immutable;

using Kora.Core.Artifacts;

namespace Kora.Core.Agents;

/// <summary>Immutable exact source bytes; this value alone conveys no admission or consent.</summary>
public sealed class AgentInstructionBundle
{
    public const int MaximumSourceBytes = 64 * 1024;

    public AgentInstructionBundle(BoundedAgentProfile profile, IEnumerable<OwnedInstructionSnapshot> instructions)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(instructions);
        Profile = profile;
        Instructions = instructions.ToImmutableArray();
        if (Instructions.Length != profile.References.Length || Instructions.Any(item => item is null)
            || Instructions.Where((item, index) => item.Document.Definition.Kind != ArtifactKind.Instruction
                || item.Document.Definition.Source is not "kora-profile"
                || !string.Equals(item.Document.Definition.Id, profile.References[index].Id, StringComparison.Ordinal)).Any())
        { throw new InvalidDataException("The bundle must contain exactly the declared owned instruction sources in order."); }
        if ((long)profile.Document.ByteCount + Instructions.Sum(item => (long)item.Document.ByteCount) > MaximumSourceBytes)
        { throw new InvalidDataException("The complete profile and instruction source bundle exceeds 64 KiB."); }
    }

    public BoundedAgentProfile Profile { get; }
    public ImmutableArray<OwnedInstructionSnapshot> Instructions { get; }
}
