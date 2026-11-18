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

    public static McpRemoteResult ReadInvocationResult(ReadOnlySpan<byte> bytes, int requestId = 3)
    {
        using var document = ModelToolProtocol.ReadJson(bytes, MaximumResultUtf8Bytes);
        var result = ReadResult(document.RootElement, requestId);
        RequireFields(result, ["content"], ["isError", "structuredContent"]);
        if (result.TryGetProperty("isError", out var error) && error.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        { throw new InvalidDataException("MCP error status must be an explicit Boolean."); }
        var content = result.GetProperty("content");
        if (content.ValueKind != JsonValueKind.Array || content.GetArrayLength() is < 1 or > 4)
        { throw new InvalidDataException("MCP returned an unsupported content collection."); }
        var text = new List<string>();
        foreach (var item in content.EnumerateArray())
        {
            RequireFields(item, ["type", "text"], []);
            if (!TextEquals(item.GetProperty("type"), "text") || item.GetProperty("text").ValueKind != JsonValueKind.String)
            { throw new InvalidDataException("The first MCP profile accepts inert text only, without embedded resources."); }
            text.Add(item.GetProperty("text").GetString()!);
        }
        string? structured = null;
        if (result.TryGetProperty("structuredContent", out var value))
        {
            if (value.ValueKind != JsonValueKind.Object)
            { throw new InvalidDataException("MCP structured content must be an inert object."); }
            structured = value.GetRawText();
        }
        return new(error.ValueKind == JsonValueKind.True, string.Join('\n', text), structured);
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