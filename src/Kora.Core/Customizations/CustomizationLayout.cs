using Kora.Core.Artifacts;

namespace Kora.Core.Customizations;

public static class CustomizationLayout
{
    public static string PrimaryFile(CustomizationKind kind, string name) => kind switch
    {
        CustomizationKind.Skill => "SKILL.md",
        CustomizationKind.Instruction => name + ".instructions.md",
        CustomizationKind.Agent => name + ".agent.md",
        CustomizationKind.Prompt => name + ".prompt.md",
        CustomizationKind.McpServer => "server.json",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static ArtifactKind ArtifactKind(CustomizationKind kind) => kind switch
    {
        CustomizationKind.Skill => Artifacts.ArtifactKind.Skill,
        CustomizationKind.Instruction => Artifacts.ArtifactKind.Instruction,
        CustomizationKind.Agent => Artifacts.ArtifactKind.Agent,
        CustomizationKind.Prompt => Artifacts.ArtifactKind.Prompt,
        _ => throw new InvalidDataException("An MCP server is not an instruction artifact."),
    };

    public static string Template(CustomizationKind kind, string name) => kind switch
    {
        CustomizationKind.McpServer => "{\n  \"type\": \"http\",\n  \"url\": \"https://example.invalid/mcp\"\n}\n",
        CustomizationKind.Instruction =>
            $"---\nkora-format: 1\nname: {name}\ndescription: Describe when to use this {kind.ToString().ToLowerInvariant()}.\n---\n\nWrite the guidance here.\n",
        CustomizationKind.Prompt =>
            $"---\nkora-format: 2\nname: {name}\ndescription: Describe when to use this prompt.\n---\n\nWrite the saved user request here.\n",
        _ => $"---\nname: {name}\ndescription: Describe when to use this {kind.ToString().ToLowerInvariant()}.\n---\n\nWrite the guidance here.\n",
    };
}
