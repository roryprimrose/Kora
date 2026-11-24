namespace Kora.Windows.Mcp;

/// <summary>One already-started held root and its physical pipes. Implementations never enumerate or terminate descendants.</summary>
internal interface IWindowsMcpProcessLease : IAsyncDisposable
{
    Stream Input { get; }
    Stream Output { get; }
    Stream Error { get; }
    bool IsOwned { get; }
    bool HasExited { get; }
    Task WaitForExitAsync(CancellationToken token);
    Task CloseInputAsync();
    Task TerminateRootAsync();
/// <summary>Content-free terminal failure classes for the owned local process.</summary>
internal enum McpProcessFailure { StderrLimit, ProtocolOutput, ShutdownUnconfirmed }

internal sealed class McpProcessException(McpProcessFailure failure)
    : IOException("The owned MCP process failed: " + failure)
{
    internal McpProcessFailure Failure { get; } = failure;
}
}
