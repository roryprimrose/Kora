namespace Kora.Core.Artifacts;

/// <summary>One immutable source-preserving owned instruction identity change, never activation.</summary>
public class OwnedInstructionRename
{
    public OwnedInstructionRename(OwnedInstructionSnapshot source, string destinationName)
        : this(source, destinationName, ArtifactKind.Instruction) { }

    private protected OwnedInstructionRename(OwnedInstructionSnapshot source, string destinationName, ArtifactKind kind)
    {
        ArgumentNullException.ThrowIfNull(source);
        OwnedInstructionNames.WriteName(source.Name);
        OwnedInstructionNames.WriteName(destinationName);
        if (OwnedInstructionNames.IsReservedControlName(destinationName))
        { throw new InvalidDataException("A renamed instruction cannot use a reserved control ID."); }
        source.Document.RequireOwnedCopySource();
        if (source.Document.Definition.Kind != kind
            || !string.Equals(source.CanonicalFileName, source.Name + OwnedDefinitionKind.Extension(kind), StringComparison.Ordinal)
            || string.Equals(source.Name, destinationName, StringComparison.OrdinalIgnoreCase))
        { throw new InvalidDataException("Rename requires one canonical native owned source of the exact kind and a distinct new canonical ID."); }
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
