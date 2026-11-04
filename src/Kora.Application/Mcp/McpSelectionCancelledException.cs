namespace Kora.Application.Mcp;

internal sealed class McpSelectionCancelledException(CancellationToken token)
    : OperationCanceledException("MCP tool selection was not submitted.", token);
