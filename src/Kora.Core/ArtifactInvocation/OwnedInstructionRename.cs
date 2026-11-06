namespace Kora.Core.Artifacts;

/// <summary>One immutable source-preserving owned instruction identity change, never activation.</summary>
public sealed class OwnedInstructionRename
{
    public OwnedInstructionRename(OwnedInstructionSnapshot source, string destinationName)
    {
        ArgumentNullException.ThrowIfNull(source);
        OwnedInstructionNames.WriteName(source.Name);
        OwnedInstructionNames.WriteName(destinationName);
        if (OwnedInstructionNames.IsReservedControlName(destinationName))
        { throw new InvalidDataException("A renamed instruction cannot use a reserved control ID."); }
        source.Document.RequireOwnedCopySource();
        if (source.Document.Definition.Kind != ArtifactKind.Instruction
            || !string.Equals(source.CanonicalFileName, source.Name + ".instructions.md", StringComparison.Ordinal)
            || string.Equals(source.Name, destinationName, StringComparison.OrdinalIgnoreCase))
        { throw new InvalidDataException("Rename requires one canonical native owned Instruction and a distinct new canonical ID."); }
        Source = source;
        DestinationName = destinationName;
        Proposed = source.Document.WithOwnedCopyName(destinationName);
        Proposed.RequireOwnedCopySource();
        if (string.Equals(source.Document.Definition.Id, Proposed.Definition.Id, StringComparison.Ordinal))
        { throw new InvalidDataException("The new instruction must have a distinct invocation identity."); }
    }

    public OwnedInstructionSnapshot Source { get; }
    public string DestinationName { get; }
    public ArtifactFileDocument Proposed { get; }
}
