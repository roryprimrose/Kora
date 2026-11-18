using System.Net;
using System.Text.Json;
using Kora.Application.Mcp;
using Kora.Application.Tools;

namespace Kora.Windows.IntegrationTests;

// In-process SDK consumer of the private dispatcher, not a listener or a second wire protocol.
internal sealed class KoraMcpSdkClientTransport(KoraMcpServer server, CapabilityCaller caller) : HttpMessageHandler
{
    internal List<string> Methods { get; } = [];
    internal int MaximumResponseBytes { get; set; } = Kora.Core.Mcp.McpProtocol.MaximumDiscoveryUtf8Bytes;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var wire = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
        using var document = JsonDocument.Parse(wire);
        var method = document.RootElement.GetProperty("method").GetString()!;
        Methods.Add(method);
        if (string.Equals(method, "notifications/initialized", StringComparison.Ordinal))
        {
            // The private dispatcher deliberately has a two-field notification contract.
            // Only the SDK's empty parameter envelope is adapted; it grants no caller authority.
            var root = document.RootElement;
            if (root.EnumerateObject().Any(field => field.Name is not ("jsonrpc" or "method" or "params"))
                || root.GetProperty("jsonrpc").GetString() is not "2.0"
                || root.TryGetProperty("params", out var parameters)
                    && (parameters.ValueKind != JsonValueKind.Object || parameters.EnumerateObject().Any()))
            { throw new InvalidDataException("The private SDK fixture cannot adapt a nonempty notification."); }
            server.HandleInitialized(caller, JsonSerializer.SerializeToUtf8Bytes(
                new { jsonrpc = root.GetProperty("jsonrpc").GetString(), method }), cancellationToken);
            return new(HttpStatusCode.Accepted) { Content = new ByteArrayContent([]) };
        }
        var bytes = server.HandleRequest(caller, wire, cancellationToken, MaximumResponseBytes);
        return new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes) { Headers = { ContentType = new("application/json") } },
        };
    }
}
