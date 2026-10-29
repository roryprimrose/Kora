using System.Collections.ObjectModel;

using Kora.Core.Artifacts;

namespace Kora.Core.Configuration;

public sealed class ArtifactDirectorySettings
{
    public const int MaximumPathsPerKind = 16;
    public const int MaximumConfiguredDirectoriesPerKind = 32;
    public const int MaximumIncludedMcpServers = 128;
    public static ArtifactDirectorySettings Empty { get; } = new([], [], [], [], [], []);

    public ArtifactDirectorySettings(
        IReadOnlyList<string> skillDirectories,
        IReadOnlyList<string> instructionDirectories,
        IReadOnlyList<string> promptDirectories,
        IReadOnlyList<string> agentDirectories,
        IReadOnlyList<string> mcpConfigurationFiles,
        IReadOnlyList<string> includedMcpServerIds,
        bool useDefaultDirectories = true)
    {
        UseDefaultDirectories = useDefaultDirectories;
        var directoryLimit = useDefaultDirectories ? MaximumPathsPerKind : MaximumConfiguredDirectoriesPerKind;
        SkillDirectories = ValidatePaths(skillDirectories, nameof(skillDirectories), directoryLimit);
        InstructionDirectories = ValidatePaths(instructionDirectories, nameof(instructionDirectories), directoryLimit);
        PromptDirectories = ValidatePaths(promptDirectories, nameof(promptDirectories), directoryLimit);
        AgentDirectories = ValidatePaths(agentDirectories, nameof(agentDirectories), directoryLimit);
        McpConfigurationFiles = ValidatePaths(mcpConfigurationFiles, nameof(mcpConfigurationFiles));
        IncludedMcpServerIds = ValidateIdentifiers(includedMcpServerIds, nameof(includedMcpServerIds));
    }

    public ReadOnlyCollection<string> SkillDirectories { get; }
    public ReadOnlyCollection<string> InstructionDirectories { get; }
    public ReadOnlyCollection<string> PromptDirectories { get; }
    public ReadOnlyCollection<string> AgentDirectories { get; }
    public ReadOnlyCollection<string> McpConfigurationFiles { get; }
    public ReadOnlyCollection<string> IncludedMcpServerIds { get; }
    public bool UseDefaultDirectories { get; }

    public static ArtifactDirectorySettings DefaultDirectories(string koraRoot, string roamingRoot, string userProfile)
    {
        string[] EditorDirectories() =>
        [
            Path.Combine(roamingRoot, "Code", "User", "prompts"),
            Path.Combine(roamingRoot, "Code", "User", "profiles"),
            Path.Combine(roamingRoot, "Code - Insiders", "User", "prompts"),
            Path.Combine(roamingRoot, "Code - Insiders", "User", "profiles"),
        ];
        return new(
            [Path.Combine(koraRoot, "Skills"), Path.Combine(userProfile, ".copilot", "skills"), Path.Combine(userProfile, ".agents", "skills")],
            [Path.Combine(koraRoot, "Instructions"), .. EditorDirectories()],
            [Path.Combine(koraRoot, "Prompts"), .. EditorDirectories()],
            [Path.Combine(koraRoot, "Agents"), .. EditorDirectories()],
            [], [], useDefaultDirectories: false);
    }

    public ArtifactDirectorySettings WithDefaults(ArtifactDirectorySettings defaults)
    {
        if (!UseDefaultDirectories) { return this; }
        static string[] Merge(IReadOnlyList<string> standard, IReadOnlyList<string> additional) =>
            standard.Concat(additional).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new(
            Merge(defaults.SkillDirectories, SkillDirectories),
            Merge(defaults.InstructionDirectories, InstructionDirectories),
            Merge(defaults.PromptDirectories, PromptDirectories),
            Merge(defaults.AgentDirectories, AgentDirectories),
            McpConfigurationFiles, IncludedMcpServerIds, useDefaultDirectories: false);
    }

    public IReadOnlyList<string> GetDirectories(ArtifactKind kind) => kind switch
    {
        ArtifactKind.Skill => SkillDirectories,
        ArtifactKind.Instruction => InstructionDirectories,
        ArtifactKind.Prompt => PromptDirectories,
        ArtifactKind.Agent => AgentDirectories,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public IReadOnlyList<string> GetPaths(ArtifactSourceKind kind) => kind switch
    {
        ArtifactSourceKind.Skill => SkillDirectories,
        ArtifactSourceKind.Instruction => InstructionDirectories,
        ArtifactSourceKind.Prompt => PromptDirectories,
        ArtifactSourceKind.Agent => AgentDirectories,
        ArtifactSourceKind.McpServerConfiguration => McpConfigurationFiles,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static ReadOnlyCollection<string> ValidatePaths(
        IReadOnlyList<string> paths,
        string parameterName,
        int maximumPaths = MaximumPathsPerKind)
    {
        ArgumentNullException.ThrowIfNull(paths, parameterName);
        if (paths.Count > maximumPaths
            || paths.Any(path => string.IsNullOrWhiteSpace(path)
                || path.Length > 1024
                || path.Contains('\0')))
        {
            throw new ArgumentException("Artifact source paths must be nonempty and bounded.", parameterName);
        }
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Count)
        {
            throw new ArgumentException("Artifact source paths must be unique within each kind.", parameterName);
        }
        return new ReadOnlyCollection<string>(paths.ToArray());
    }

    private static ReadOnlyCollection<string> ValidateIdentifiers(
        IReadOnlyList<string> identifiers,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(identifiers, parameterName);
        if (identifiers.Count > MaximumIncludedMcpServers
            || identifiers.Any(identifier => identifier.Length != 64
                || identifier.Any(character => character is not (>= 'a' and <= 'f' or >= '0' and <= '9')))
            || identifiers.Distinct(StringComparer.Ordinal).Count() != identifiers.Count)
        {
            throw new ArgumentException("Included MCP server identifiers must be unique SHA-256 values.", parameterName);
        }
        return new ReadOnlyCollection<string>(identifiers.ToArray());
    }
}
