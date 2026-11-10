using System.Text;
using System.Text.Json;
using Kora.Application.Tools;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Mcp;
using Kora.Core.Tools;

namespace Kora.Application.Mcp;

public sealed partial class KoraMcpServer
{
    public async Task<byte[]> HandleRequestAsync(CapabilityCaller caller, ReadOnlyMemory<byte> request,
        CancellationToken token, int maximumResponseBytes = McpProtocol.MaximumDiscoveryUtf8Bytes)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (maximumResponseBytes is < 1 or > McpProtocol.MaximumDiscoveryUtf8Bytes)
        { throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes)); }
        token.ThrowIfCancellationRequested();
        if (!registry.IsCurrentCaller(caller) || !preferences.IsServerEnabled(KoraMcpCatalog.ServerId))
        { throw new InvalidOperationException("The private Kora MCP caller or server is not admitted."); }
        using var document = ModelToolProtocol.ReadJson(request.Span, McpProtocol.MaximumDiscoveryUtf8Bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        { return HandleRequest(caller, request.Span, token, maximumResponseBytes); }
        if (!root.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String
            || !string.Equals(method.GetString(), "tools/call", StringComparison.Ordinal)
            || !root.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
            || MaintenanceTools.FindExact(name.GetString()!) is null)
        { return HandleRequest(caller, request.Span, token, maximumResponseBytes); }
        if (root.EnumerateObject().Count() != 4
            || !root.TryGetProperty("jsonrpc", out var version) || version.ValueKind != JsonValueKind.String
            || !string.Equals(version.GetString(), "2.0", StringComparison.Ordinal)
            || !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out _)
            || parameters.EnumerateObject().Count() != 2 || !parameters.TryGetProperty("arguments", out var arguments))
        { throw new InvalidDataException("Maintenance requires an exact bounded JSON-RPC tool call."); }
        var parsed = CapabilityInputPolicy.Read(MaintenanceTools.FindExact(name.GetString()!)!, arguments.GetRawText());
        var revision = preferences.Revision;
        CapabilityReply reply;
        if (parsed.DenialReason is { } denial)
        { reply = new(CapabilityOutcome.Denied, denial); }
        else if (caller.Lane != CapabilityLane.Native || caller.Activity.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        { reply = new(CapabilityOutcome.Denied, "original-maintenance-request-required"); }
        else if (!preferences.IsToolEnabled(KoraMcpCatalog.ServerId, name.GetString()!))
        { reply = new(CapabilityOutcome.Denied, "mcp-capability-disabled"); }
        else if (maintenance is null)
        { reply = new(CapabilityOutcome.Denied, "maintenance-mcp-unavailable"); }
        else
        {
            reply = await maintenance.InvokeAsync(name.GetString()!, arguments.GetRawText(),
                () => registry.IsLiveCaller(caller) && string.Equals(preferences.Revision, revision, StringComparison.Ordinal),
                token).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
        if (!registry.IsCurrentCaller(caller) || !string.Equals(preferences.Revision, revision, StringComparison.Ordinal))
        { throw new InvalidOperationException("Private maintenance MCP admission changed before publication."); }
        if (!MaintenanceTools.Matches(reply, name.GetString()!, parsed.ReleaseVersion) || !CapabilityReplyWire.IsWithinLimit(reply))
        { reply = new(CapabilityOutcome.Failed, "invalid-maintenance-result"); }
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WritePropertyName("id");
            id.WriteTo(writer);
            writer.WriteStartObject("result");
            writer.WriteBoolean("isError", reply.Outcome != CapabilityOutcome.Succeeded);
            writer.WriteStartArray("content");
            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", Encoding.UTF8.GetString(CapabilityReplyWire.Serialize(reply)));
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        var response = stream.ToArray();
        if (response.Length > maximumResponseBytes)
        { throw new InvalidDataException("The complete private maintenance response exceeds its byte bound."); }
        return response;
    }
}
