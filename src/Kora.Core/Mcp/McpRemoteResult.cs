namespace Kora.Core.Mcp;

/// <summary>Bounded inert server data; provenance and local disclosure remain host-owned.</summary>
public sealed record McpRemoteResult(bool IsError, string Text, string? StructuredJson)
{
    public override string ToString() => nameof(McpRemoteResult);
}
