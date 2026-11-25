using Kora.Core.Mcp;
using ModelContextProtocol.Client;

namespace Kora.Windows.Mcp;

internal sealed partial class McpHttpConnection
{
    private McpSdkConnection? sdk;
    private HttpClient? sdkHttp;

    private McpSdkConnection Sdk => sdk ??= CreateSdk();

    private McpSdkConnection CreateSdk()
    {
        var handler = new McpSdkRequestHandler(Endpoint, PostAsync);
        sdkHttp = new(handler) { Timeout = Timeout.InfiniteTimeSpan };
        return new(handler, () => new HttpClientTransport(new()
        {
            Endpoint = Endpoint.Address,
            Name = "kora-reviewed-mcp",
            TransportMode = HttpTransportMode.StreamableHttp,
            EnableStandaloneGetStream = false,
            OwnsSession = false,
            MaxReconnectionAttempts = 0,
        }, sdkHttp), token => WindowsMcpConnectionBroker.RequireCurrentAsync(lease, Account, token), time);
    }

    public Task<McpConnectionReply<McpServerCapabilities>> InitializeAsync(CancellationToken token) =>
        RunAsync(current => Sdk.InitializeAsync(current), token);
    public Task<McpConnectionReply<McpCapabilityPage>> ListAsync(McpCapabilityKind kind, string? cursor, CancellationToken token) =>
        RunAsync(current => Sdk.ListAsync(kind, cursor, current), token);
    public Task<McpConnectionReply<McpRemoteResult>> InvokeAsync(string name, McpToolSchema schema, string arguments, CancellationToken token) =>
        RunAsync(current => Sdk.InvokeAsync(name, schema, arguments, current), token);
    public Task<McpConnectionReply<McpTextDocument>> ReadResourceAsync(string uri, CancellationToken token) =>
        RunAsync(current => Sdk.ReadResourceAsync(uri, current), token);
    public Task<McpConnectionReply<McpTextDocument>> ReadPromptAsync(McpCapability prompt, string arguments, CancellationToken token) =>
        RunAsync(current => Sdk.ReadPromptAsync(prompt, arguments, current), token);
}
