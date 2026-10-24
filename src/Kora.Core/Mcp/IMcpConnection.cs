using System.Collections.Immutable;
using System.Net;

namespace Kora.Core.Mcp;

/// <summary>A single owned authenticated transport. Identity reports are observations, not grants.</summary>
public interface IMcpConnection : IAsyncDisposable
{
    McpAccountReference Account { get; }
    McpEndpoint Endpoint { get; }
    IReadOnlyList<IPAddress> RemoteAddresses { get; }
    Task<bool> IsCurrentAsync(CancellationToken token);
    Task<ImmutableArray<byte>> ExchangeAsync(ImmutableArray<byte> request, int maximumResponseBytes, CancellationToken token);
    Task NotifyAsync(ImmutableArray<byte> notification, CancellationToken token);
}
