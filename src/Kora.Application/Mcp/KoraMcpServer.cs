using System.Text;
using System.Text.Json;
using Kora.Application.Configuration;
using Kora.Application.Tools;
using Kora.Core.Dependencies;
using Kora.Core.Mcp;
using Kora.Core.Tools;

namespace Kora.Application.Mcp;

/// <summary>Private MCP projection of admitted host observations; never a network listener.</summary>
public sealed class KoraMcpServer(ReadOnlyCapabilityRegistry registry, McpCapabilityPreferences preferences)
{
    public void HandleInitialized(CapabilityCaller caller, ReadOnlySpan<byte> notification, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(caller);
        token.ThrowIfCancellationRequested();
        if (!registry.IsCurrentCaller(caller) || !preferences.IsServerEnabled(KoraMcpCatalog.ServerId))
        { throw new InvalidOperationException("The private Kora MCP caller or server is not admitted."); }
        using var document = ModelToolProtocol.ReadJson(notification, McpProtocol.MaximumDiscoveryUtf8Bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2
            || !root.TryGetProperty("jsonrpc", out var version) || version.ValueKind != JsonValueKind.String
            || !string.Equals(version.GetString(), "2.0", StringComparison.Ordinal)
            || !root.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String
            || !string.Equals(method.GetString(), "notifications/initialized", StringComparison.Ordinal))
        { throw new InvalidDataException("The private Kora MCP notification is unsupported."); }
    }

    internal async ValueTask<CapabilityReply> InvokeAsync(ModelToolMode mode, string id, string input,
        Func<CancellationToken, ValueTask<CapabilityReply>> invoke, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!ModelToolProtocol.GetTools(mode).Any(tool => string.Equals(tool.Id, id, StringComparison.Ordinal)))
        { return new(CapabilityOutcome.Denied, "model-tools-not-permitted"); }
        if (!preferences.IsToolEnabled(KoraMcpCatalog.ServerId, id))
        { return new(CapabilityOutcome.Denied, "mcp-capability-disabled"); }
        var revision = preferences.Revision;
        var reply = await invoke(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return string.Equals(preferences.Revision, revision, StringComparison.Ordinal) ? reply
            : new(CapabilityOutcome.Denied, "mcp-capability-admission-changed");
    }

    public byte[] HandleRequest(CapabilityCaller caller, ReadOnlySpan<byte> request, CancellationToken token,
        int maximumResponseBytes = McpProtocol.MaximumDiscoveryUtf8Bytes)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (maximumResponseBytes is < 1 or > McpProtocol.MaximumDiscoveryUtf8Bytes)
        { throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes)); }
        token.ThrowIfCancellationRequested();
        if (!registry.IsCurrentCaller(caller) || !preferences.IsServerEnabled(KoraMcpCatalog.ServerId))
        { throw new InvalidOperationException("The private Kora MCP caller or server is not admitted."); }
        var revision = preferences.Revision;
        using var document = ModelToolProtocol.ReadJson(request, McpProtocol.MaximumDiscoveryUtf8Bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || root.EnumerateObject().Any(field => field.Name is not ("jsonrpc" or "id" or "method" or "params"))
            || !root.TryGetProperty("jsonrpc", out var version) || version.ValueKind != JsonValueKind.String
            || !string.Equals(version.GetString(), "2.0", StringComparison.Ordinal)
            || !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out _)
            || !root.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
        { throw new InvalidDataException("A private MCP request requires an exact bounded JSON-RPC envelope."); }
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WritePropertyName("id");
            id.WriteTo(writer);
            writer.WriteStartObject("result");
            switch (method.GetString())
            {
                case "initialize":
                    if (!parameters.TryGetProperty("protocolVersion", out var protocol)
                        || protocol.ValueKind != JsonValueKind.String
                        || !string.Equals(protocol.GetString(), McpProtocol.Version, StringComparison.Ordinal))
                    { throw new InvalidDataException("The private Kora MCP protocol version is unsupported."); }
                    writer.WriteString("protocolVersion", McpProtocol.Version);
                    writer.WriteStartObject("capabilities");
                    writer.WriteStartObject("tools");
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                    writer.WriteStartObject("serverInfo");
                    writer.WriteString("name", "Kora");
                    writer.WriteString("version", "1");
                    writer.WriteEndObject();
                    break;
                case "tools/list":
                    if (parameters.EnumerateObject().Any())
                    { throw new InvalidDataException("Private Kora discovery does not accept additional fields."); }
                    writer.WriteStartArray("tools");
                    foreach (var descriptor in ReadOnlyCapabilityCatalog.Descriptors.Where(item =>
                        preferences.IsToolEnabled(KoraMcpCatalog.ServerId, item.Id)))
                    {
                        writer.WriteStartObject();
                        writer.WriteString("name", descriptor.Id);
                        writer.WriteString("description", "Host-owned read-only observation: " + descriptor.Id);
                        writer.WritePropertyName("inputSchema");
                        writer.WriteRawValue(ReadOnlyCapabilitySchema.Serialize(descriptor.Id));
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    break;
                case "tools/call":
                    if (parameters.EnumerateObject().Count() != 2
                        || !parameters.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                        || !parameters.TryGetProperty("arguments", out var arguments))
                    { throw new InvalidDataException("A private Kora call requires one exact name and argument object."); }
                    var reply = registry.Invoke(caller, name.GetString()!, arguments.GetRawText(), token);
                    writer.WriteBoolean("isError", reply.Outcome != CapabilityOutcome.Succeeded);
                    writer.WriteStartArray("content");
                    writer.WriteStartObject();
                    writer.WriteString("type", "text");
                    writer.WriteString("text", Encoding.UTF8.GetString(CapabilityReplyWire.Serialize(reply)));
                    writer.WriteEndObject();
                    writer.WriteEndArray();
                    break;
                default:
                    throw new InvalidDataException("The private Kora MCP method is unsupported.");
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        token.ThrowIfCancellationRequested();
        if (!registry.IsCurrentCaller(caller) || !string.Equals(preferences.Revision, revision, StringComparison.Ordinal))
        { throw new InvalidOperationException("Private Kora MCP admission changed before publication."); }
        var result = stream.ToArray();
        if (result.Length > maximumResponseBytes)
        { throw new InvalidDataException("The complete private Kora MCP response exceeds its byte bound."); }
        return result;
    }
}
