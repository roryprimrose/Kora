using Kora.Core.Mcp;

namespace Kora.Application.Mcp;

/// <summary>The shipped connector has no qualified account/transport installation. Never attempt anonymous or ambient fallback.</summary>
public sealed class UnavailableMcpConnectionBroker : IMcpConnectionBroker
{
    public Task<IReadOnlyList<McpAccountChoice>> ReadAccountsAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        throw new McpUnavailableException();
    }

    public Task<IMcpConnection> ConnectAsync(McpAccountReference account, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        throw new McpUnavailableException();
    }
}
