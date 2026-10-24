namespace Kora.Core.Mcp;

/// <summary>Untrusted discovery metadata, never an effect mapping or invocation grant.</summary>
public sealed record McpDiscoveredTool(string Name, McpToolSchema Schema);
