using System.Collections.Immutable;
using System.Net;
using Kora.Core.Mcp;

namespace Kora.Application.UnitTests.Dependencies;

internal abstract class McpWireTestConnection : IMcpConnection
{
    private int nextId = 2;
    public abstract McpAccountReference Account { get; }
    public abstract McpEndpoint Endpoint { get; }
    public abstract IReadOnlyList<IPAddress> RemoteAddresses { get; }
    public virtual CancellationToken AdmissionWithdrawn => new(canceled: true);
    public abstract Task<bool> IsCurrentAsync(CancellationToken token);
    public abstract Task<ImmutableArray<byte>> ExchangeAsync(ImmutableArray<byte> wire, int maximumResponseBytes, CancellationToken token);
    public abstract Task NotifyAsync(ImmutableArray<byte> wire, CancellationToken token);
    public abstract ValueTask DisposeAsync();

    public async Task<McpConnectionReply<McpServerCapabilities>> InitializeAsync(CancellationToken token)
    {
        nextId = 2;
        var result = await ReadAsync(McpProtocol.InitializeRequest(), McpProtocol.MaximumDiscoveryUtf8Bytes,
            bytes => McpCapabilityProtocol.ReadInitialization(bytes), token);
        await NotifyAsync(ImmutableArray.CreateRange(McpProtocol.InitializedNotification()), token);
        return result;
    }

    public Task<McpConnectionReply<McpCapabilityPage>> ListAsync(McpCapabilityKind kind, string? cursor, CancellationToken token)
    {
        var id = nextId++;
        return ReadAsync(McpCapabilityProtocol.ListRequest(kind, id, cursor), McpProtocol.MaximumDiscoveryUtf8Bytes,
            bytes => McpCapabilityProtocol.ReadPage(bytes, kind, id), token);
    }

    public Task<McpConnectionReply<McpRemoteResult>> InvokeAsync(string name, McpToolSchema schema, string arguments, CancellationToken token)
    {
        var id = nextId++;
        return ReadAsync(McpProtocol.InvocationRequest(name, schema, arguments, id), McpProtocol.MaximumResultUtf8Bytes,
            bytes => McpProtocol.ReadInvocationResult(bytes, id), token);
    }

    public Task<McpConnectionReply<McpTextDocument>> ReadResourceAsync(string uri, CancellationToken token)
    {
        var id = nextId++;
        return ReadAsync(McpCapabilityProtocol.ResourceRequest(uri, id), McpCapabilityProtocol.MaximumContentBytes,
            bytes => McpCapabilityProtocol.ReadResource(bytes, id, uri), token);
    }

    public Task<McpConnectionReply<McpTextDocument>> ReadPromptAsync(McpCapability prompt, string arguments, CancellationToken token)
    {
        var id = nextId++;
        return ReadAsync(McpCapabilityProtocol.PromptRequest(prompt, arguments, id), McpCapabilityProtocol.MaximumContentBytes,
            bytes => McpCapabilityProtocol.ReadPrompt(bytes, id), token);
    }

    private async Task<McpConnectionReply<T>> ReadAsync<T>(byte[] wire, int limit, Func<byte[], T> parse, CancellationToken token)
    {
        var bytes = await ExchangeAsync(ImmutableArray.CreateRange(wire), limit, token);
        return new(bytes.IsDefaultOrEmpty || bytes.Length > limit ? default! : parse(bytes.ToArray()), bytes.Length);
    }
}
