using System.Collections.Immutable;

using Kora.Core.Artifacts;

namespace Kora.Core.Agents;

/// <summary>An exact catalogue ID, never a path, search or instruction enablement.</summary>
public sealed record AgentInstructionReference
{
    public const int MaximumCount = 4;
    public const string Prefix = "disk.kora-profile.instruction.";

    public AgentInstructionReference(string id)
    {
        if (string.IsNullOrEmpty(id) || !id.StartsWith(Prefix, StringComparison.Ordinal)
            || id.Length == Prefix.Length)
        { throw new InvalidDataException("Agent references require exact source-qualified Kora-owned instruction IDs."); }
        try { Id = ArtifactDefinition.ValidateId(id); }
        catch (ArgumentException exception)
        { throw new InvalidDataException("The owned instruction reference ID is invalid.", exception); }
    }

    public string Id { get; }

    internal static ImmutableArray<AgentInstructionReference> Parse(string value)
    {
        var ids = value.Split(',').Select(item => item.Trim()).ToArray();
        if (ids.Length > MaximumCount || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
        { throw new InvalidDataException("Declare between one and four distinct owned instruction IDs."); }
        return ids.Select(id => new AgentInstructionReference(id)).ToImmutableArray();
    }
}
