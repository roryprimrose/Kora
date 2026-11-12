namespace Kora.Core.Artifacts;

/// <summary>One source-preserving native v1/v2 Prompt rename, without use or authority transfer.</summary>
public sealed class OwnedPromptRename(OwnedInstructionSnapshot source, string destinationName)
    : OwnedInstructionRename(source, destinationName, ArtifactKind.Prompt);
