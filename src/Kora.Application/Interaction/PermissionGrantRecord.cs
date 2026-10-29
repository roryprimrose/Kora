using Kora.Core.Authorization;
using Kora.Core.Customizations;

namespace Kora.Application.Interaction;

public abstract record PermissionGrantRecord
{
    public abstract string Name { get; }
    public abstract string Summary { get; }
    public abstract string Detail { get; }
    public abstract PermissionGrantTarget? Target { get; }

    public string ArtifactType => Target?.Kind switch
    {
        null => "Unknown",
        PermissionGrantArtifactKind.McpServer => "MCP server",
        PermissionGrantArtifactKind.Uri => "URI pattern",
        PermissionGrantArtifactKind kind => kind.ToString(),
    };

    public string ArtifactScope => Target?.Scope switch
    {
        null => "Unknown",
        CustomizationScope.BuiltIn => "Built-in",
        CustomizationScope scope => scope.ToString(),
    };
}
