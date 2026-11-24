using Kora.Core.Mcp;

namespace Kora.Application.Mcp;

/// <summary>One reviewed implementation's read-only effect mapping; no default or server-asserted effect is accepted.</summary>
public sealed record McpReadOnlyMapping(McpConnectionIdentity Identity,
    string SourceRevision, string ToolName, string SchemaDigest)
{
    public McpReadOnlyMapping(McpEndpoint endpoint, McpAccountReference account,
        string sourceRevision, string toolName, string schemaDigest)
        : this(new McpConnectionIdentity(account), sourceRevision, toolName, schemaDigest)
    {
        if (endpoint != account.Destination) { throw new InvalidDataException("MCP mapping destination mismatch."); }
    }
}
