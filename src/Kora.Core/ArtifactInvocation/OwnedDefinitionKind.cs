namespace Kora.Core.Artifacts;

/// <summary>Closed host-owned single-file layouts; never caller-configurable filesystem authority.</summary>
public static class OwnedDefinitionKind
{
    public static string DirectoryName(ArtifactKind kind) => kind switch
    {
        ArtifactKind.Instruction => "Instructions",
        ArtifactKind.Prompt => "Prompts",
        _ => throw new InvalidDataException("Only owned instructions and prompts support native editing."),
    };

    public static string Extension(ArtifactKind kind) => kind switch
    {
        ArtifactKind.Instruction => ".instructions.md",
        ArtifactKind.Prompt => ".prompt.md",
        _ => throw new InvalidDataException("Only owned instructions and prompts support native editing."),
    };

    public static int MaximumFiles(ArtifactKind kind) => kind switch
    {
        ArtifactKind.Instruction => 128,
        ArtifactKind.Prompt => 128,
        _ => throw new InvalidDataException("Only owned instructions and prompts support native editing."),
    };
}
