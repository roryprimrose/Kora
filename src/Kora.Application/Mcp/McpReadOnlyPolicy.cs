using Kora.Core.Mcp;

namespace Kora.Application.Mcp;

/// <summary>Trusted host registration only. Discovery/configuration/annotations cannot populate this catalogue.</summary>
public sealed class McpReadOnlyPolicy
{
    private readonly McpReadOnlyMapping[] mappings;

    public McpReadOnlyPolicy(IEnumerable<McpReadOnlyMapping> mappings) => this.mappings = mappings.ToArray();

    internal McpReadOnlyMapping? Find(McpConnectionDescriptor source, McpDiscoveredTool tool) =>
        mappings.SingleOrDefault(mapping => mapping.Endpoint == source.Endpoint && mapping.Account == source.Account
            && string.Equals(mapping.SourceRevision, source.SourceRevision, StringComparison.Ordinal)
            && string.Equals(mapping.ToolName, tool.Name, StringComparison.Ordinal)
            && string.Equals(mapping.SchemaDigest, tool.Schema.Digest, StringComparison.Ordinal));
}
