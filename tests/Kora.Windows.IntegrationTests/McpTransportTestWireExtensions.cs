using System.Collections.Immutable;
using Kora.Core.Mcp;
using Kora.Windows.Mcp;

namespace Kora.Windows.IntegrationTests;

// These characterize the physical host gate independently of SDK protocol orchestration.
internal static class McpTransportTestWireExtensions
{
    internal static Task<ImmutableArray<byte>> ExchangeAsync(this IMcpConnection connection, ImmutableArray<byte> wire,
        int limit, CancellationToken token) => ((McpHttpConnection)connection).ExchangeAsync(wire, limit, token);

    internal static Task NotifyAsync(this IMcpConnection connection, ImmutableArray<byte> wire, CancellationToken token) =>
        ((McpHttpConnection)connection).NotifyAsync(wire, token);
}
