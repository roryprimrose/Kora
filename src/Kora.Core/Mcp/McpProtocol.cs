using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Kora.Core.Dependencies;

namespace Kora.Core.Mcp;

/// <summary>Bounded JSON-RPC for the first HTTPS profile. Peer metadata never conveys effect authority.</summary>
public static class McpProtocol
{
    public const string Version = "2025-06-18";
    public const int MaximumDiscoveryUtf8Bytes = 16384;
    public const int MaximumTools = 8;
    public const int MaximumResultUtf8Bytes = 1536;

    public static byte[] InitializeRequest() => Encoding.UTF8.GetBytes(
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"Kora","version":"1"}}}""");

    public static byte[] InitializedNotification() =>
        Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");

    public static byte[] DiscoveryRequest() =>
        Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""");

    public static void ValidateInitialization(ReadOnlySpan<byte> bytes)
    {
        using var document = ModelToolProtocol.ReadJson(bytes, MaximumDiscoveryUtf8Bytes);
        var result = ReadResult(document.RootElement, 1);
        RequireFields(result, ["protocolVersion", "capabilities", "serverInfo"], ["instructions"]);
        if (!TextEquals(result.GetProperty("protocolVersion"), Version)
            || result.GetProperty("capabilities") is not { ValueKind: JsonValueKind.Object } capabilities
            || !capabilities.TryGetProperty("tools", out var tools) || tools.ValueKind != JsonValueKind.Object
            || result.GetProperty("serverInfo") is not { ValueKind: JsonValueKind.Object } server)
        { throw new InvalidDataException("MCP negotiation did not establish the exact supported tool protocol."); }
        RequireFields(server, ["name", "version"], ["title"]);
        if (server.GetProperty("name").ValueKind != JsonValueKind.String
            || server.GetProperty("version").ValueKind != JsonValueKind.String)
        { throw new InvalidDataException("MCP negotiation lacks a server identity."); }
        // Server instructions and capability hints are bounded untrusted data, never host policy.
    }

    public static ImmutableArray<McpDiscoveredTool> ReadDiscovery(ReadOnlySpan<byte> bytes)
    {
        using var document = ModelToolProtocol.ReadJson(bytes, MaximumDiscoveryUtf8Bytes);
        var result = ReadResult(document.RootElement, 2);
        RequireFields(result, ["tools"], []);
        var tools = result.GetProperty("tools");
        if (tools.ValueKind != JsonValueKind.Array || tools.GetArrayLength() is < 1 or > MaximumTools)
        { throw new InvalidDataException("MCP discovery requires a finite nonempty unpaginated tool set."); }
        var names = new HashSet<string>(StringComparer.Ordinal);
        var discovered = ImmutableArray.CreateBuilder<McpDiscoveredTool>();
        foreach (var tool in tools.EnumerateArray())
        {
            RequireFields(tool, ["name", "inputSchema"], ["description", "annotations", "outputSchema", "title"]);
            var name = tool.GetProperty("name");
            if (name.ValueKind != JsonValueKind.String)
            { throw new InvalidDataException("An MCP tool requires an exact bounded name."); }
            McpToolSchema.ValidateName(name.GetString()!);
            if (!names.Add(name.GetString()!))
            { throw new InvalidDataException("Duplicate MCP tools are not admitted."); }
            var schema = new McpToolSchema(tool.GetProperty("inputSchema").GetRawText());
            discovered.Add(new(name.GetString()!, schema));
        }
        return discovered.ToImmutable();
    }

    public static byte[] InvocationRequest(string toolName, McpToolSchema schema, string arguments)
    {
        McpToolSchema.ValidateName(toolName);
        ArgumentNullException.ThrowIfNull(schema);
        schema.ValidateArguments(arguments);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteNumber("id", 3);
            writer.WriteString("method", "tools/call");
            writer.WriteStartObject("params");
            writer.WriteString("name", toolName);
            writer.WritePropertyName("arguments");
            writer.WriteRawValue(arguments);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static McpRemoteResult ReadInvocationResult(ReadOnlySpan<byte> bytes)
    {
        using var document = ModelToolProtocol.ReadJson(bytes, MaximumResultUtf8Bytes);
        var result = ReadResult(document.RootElement, 3);
        RequireFields(result, ["content"], ["isError", "structuredContent"]);
        if (result.TryGetProperty("isError", out var error) && error.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        { throw new InvalidDataException("MCP error status must be an explicit Boolean."); }
        var content = result.GetProperty("content");
        if (content.ValueKind != JsonValueKind.Array || content.GetArrayLength() is < 1 or > 4)
        { throw new InvalidDataException("MCP returned an unsupported content collection."); }
        var text = new StringBuilder();
        foreach (var item in content.EnumerateArray())
        {
            RequireFields(item, ["type", "text"], []);
            if (!TextEquals(item.GetProperty("type"), "text") || item.GetProperty("text").ValueKind != JsonValueKind.String)
            { throw new InvalidDataException("The first MCP profile accepts inert text only, without embedded resources."); }
            if (text.Length > 0) { text.Append('\n'); }
            text.Append(item.GetProperty("text").GetString());
        }
        string? structured = null;
        if (result.TryGetProperty("structuredContent", out var value))
        {
            if (value.ValueKind != JsonValueKind.Object)
            { throw new InvalidDataException("MCP structured content must be an inert object."); }
            structured = value.GetRawText();
        }
        return new(error.ValueKind == JsonValueKind.True, text.ToString(), structured);
    }

    private static JsonElement ReadResult(JsonElement root, int id)
    {
        RequireFields(root, ["jsonrpc", "id", "result"], []);
        if (!TextEquals(root.GetProperty("jsonrpc"), "2.0")
            || root.GetProperty("id").ValueKind != JsonValueKind.Number
            || !root.GetProperty("id").TryGetInt32(out var observed) || observed != id)
        { throw new InvalidDataException("The MCP response does not match the exact request."); }
        return root.GetProperty("result");
    }

    private static bool TextEquals(JsonElement value, string expected) => value.ValueKind == JsonValueKind.String
        && string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    private static void RequireFields(JsonElement value, string[] required, string[] optional)
    {
        if (value.ValueKind != JsonValueKind.Object
            || required.Any(name => !value.TryGetProperty(name, out _))
            || value.EnumerateObject().Any(property =>
                !required.Contains(property.Name, StringComparer.Ordinal)
                && !optional.Contains(property.Name, StringComparer.Ordinal)))
        { throw new InvalidDataException("The MCP response has missing or unsupported fields."); }
    }
}
