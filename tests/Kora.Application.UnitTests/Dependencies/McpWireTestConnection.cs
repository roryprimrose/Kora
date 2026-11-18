using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
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
        var result = await ReadAsync(Request("initialize", 1, new { }), McpProtocol.MaximumDiscoveryUtf8Bytes,
            bytes => McpCapabilityProtocol.ReadInitialization(bytes), token);
        await NotifyAsync(ImmutableArray.CreateRange(
            """{"jsonrpc":"2.0","method":"notifications/initialized"}"""u8.ToArray()), token);
        return result;
    }

    public Task<McpConnectionReply<McpCapabilityPage>> ListAsync(McpCapabilityKind kind, string? cursor, CancellationToken token)
    {
        var id = nextId++;
        return ReadAsync(Request(McpCapabilityProtocol.ListMethod(kind), id,
            cursor is null ? new Dictionary<string, string>(StringComparer.Ordinal) : new(StringComparer.Ordinal) { ["cursor"] = cursor }),
            McpProtocol.MaximumDiscoveryUtf8Bytes,
            bytes => McpCapabilityProtocol.ReadPage(bytes, kind, id), token);
    }

    public Task<McpConnectionReply<McpRemoteResult>> InvokeAsync(string name, McpToolSchema schema, string arguments, CancellationToken token)
    {
        var id = nextId++;
        schema.ValidateArguments(arguments);
        // Raw argument spelling is part of the existing approval/host fixture contract.
        var wire = System.Text.Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":"
            + id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",\"method\":\"tools/call\",\"params\":{\"name\":" + JsonSerializer.Serialize(name) + ",\"arguments\":" + arguments + "}}");
        return ReadAsync(wire,
            McpProtocol.MaximumResultUtf8Bytes,
            bytes => McpProtocol.ReadInvocationResult(bytes, id), token);
    }

    public Task<McpConnectionReply<McpTextDocument>> ReadResourceAsync(string uri, CancellationToken token)
    {
        var id = nextId++;
        return ReadAsync(Request("resources/read", id, new { uri }), McpCapabilityProtocol.MaximumContentBytes,
            bytes => McpCapabilityProtocol.ReadResource(bytes, id, uri), token);
    }

    public Task<McpConnectionReply<McpTextDocument>> ReadPromptAsync(McpCapability prompt, string arguments, CancellationToken token)
    {
        var id = nextId++;
        return ReadAsync(Request("prompts/get", id, new { name = prompt.Id, arguments = McpCapabilityProtocol.ReadPromptArguments(prompt, arguments) }),
            McpCapabilityProtocol.MaximumContentBytes,
            bytes => McpCapabilityProtocol.ReadPrompt(bytes, id), token);
    }

    private async Task<McpConnectionReply<T>> ReadAsync<T>(byte[] wire, int limit, Func<byte[], T> parse, CancellationToken token)
    {
        var bytes = await ExchangeAsync(ImmutableArray.CreateRange(wire), limit, token);
        return new(bytes.IsDefaultOrEmpty || bytes.Length > limit ? default! : parse(bytes.ToArray()), bytes.Length);
    }

    // Existing application race/hostile-reply fixtures use bytes; real transport construction belongs to the SDK.
    private static byte[] Request<T>(string method, int id, T parameters) =>
        JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", id, method, @params = parameters });
}
