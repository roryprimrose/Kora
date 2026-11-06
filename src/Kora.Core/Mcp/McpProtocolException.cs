namespace Kora.Core.Mcp;

/// <summary>A correlated server error without retaining or disclosing its untrusted message or data.</summary>
public sealed class McpProtocolException(int code) : InvalidOperationException("The MCP server reported an error. Review before a new request.")
{
    public int Code { get; } = code;
}