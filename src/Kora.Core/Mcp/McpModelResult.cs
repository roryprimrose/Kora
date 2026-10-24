namespace Kora.Core.Mcp;

/// <summary>Host-observed bounded inert data, never an instruction or new disclosure grant.</summary>
public sealed record McpModelResult(string ToolId, string SourceRevision, DateTimeOffset ObservedAt,
    string Text, string? StructuredJson)
{
    public override string ToString() => nameof(McpModelResult);
}
