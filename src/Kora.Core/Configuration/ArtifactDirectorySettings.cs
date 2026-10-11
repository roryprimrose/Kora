using System.Collections.ObjectModel;

using Kora.Core.Artifacts;

namespace Kora.Core.Configuration;

public sealed class ArtifactDirectorySettings
{
    public const int MaximumPathsPerKind = 16;
    public const int MaximumIncludedMcpServers = 128;
    public static ArtifactDirectorySettings Empty { get; } = new([], [], [], [], [], []);

    public ArtifactDirectorySettings(
        IReadOnlyList<string> skillDirectories,
        IReadOnlyList<string> instructionDirectories,
        IReadOnlyList<string> promptDirectories,
        IReadOnlyList<string> agentDirectories,
        IReadOnlyList<string> mcpConfigurationFiles,
        IReadOnlyList<string> includedMcpServerIds)
    {
        SkillDirectories = ValidatePaths(skillDirectories, nameof(skillDirectories));
        InstructionDirectories = ValidatePaths(instructionDirectories, nameof(instructionDirectories));
        PromptDirectories = ValidatePaths(promptDirectories, nameof(promptDirectories));
        AgentDirectories = ValidatePaths(agentDirectories, nameof(agentDirectories));
        McpConfigurationFiles = ValidatePaths(mcpConfigurationFiles, nameof(mcpConfigurationFiles));
        IncludedMcpServerIds = ValidateIdentifiers(includedMcpServerIds, nameof(includedMcpServerIds));
    }

    public ReadOnlyCollection<string> SkillDirectories { get; }
    public ReadOnlyCollection<string> InstructionDirectories { get; }
    public ReadOnlyCollection<string> PromptDirectories { get; }
    public ReadOnlyCollection<string> AgentDirectories { get; }
    public ReadOnlyCollection<string> McpConfigurationFiles { get; }
    public ReadOnlyCollection<string> IncludedMcpServerIds { get; }

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
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(paths, parameterName);
        if (paths.Count > MaximumPathsPerKind
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
