using Kora.Core.Mcp;

namespace Kora.Application.Mcp;

/// <summary>One reviewed implementation's read-only effect mapping; no default or server-asserted effect is accepted.</summary>
public sealed record McpReadOnlyMapping(McpEndpoint Endpoint, McpAccountReference Account,
    string SourceRevision, string ToolName, string SchemaDigest);
