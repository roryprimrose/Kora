using Kora.Core.Mcp;

namespace Kora.Application.Mcp;

/// <summary>One complete volatile native observation, not reusable model consent.</summary>
public sealed record McpContentResult(McpCapability Capability, McpTextDocument Document,
    string SourceRevision, DateTimeOffset ObservedAt)
{
    public override string ToString() => nameof(McpContentResult);
}