namespace Kora.Application.Mcp;

public sealed class McpUnavailableException : InvalidOperationException
{
    public McpUnavailableException() : base("The exact delegated-account HTTPS MCP connector is not qualified. No connection was attempted.") { }
}
