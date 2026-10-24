using System.Text;
using System.Text.Json;

namespace Kora.Core.Customizations;

public sealed class McpDefinitionDocument
{
    private readonly byte[] bytes;
    private readonly List<Entry> entries = [];
    private readonly int insertion;
    private sealed record Entry(string Name, int Start, int End);

    public McpDefinitionDocument(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > 256 * 1024) { throw new InvalidDataException("MCP configuration exceeds its 256 KiB bound or is empty."); }
        this.bytes = bytes.ToArray();
        var prefix = bytes.StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
        try
        {
            using var document = JsonDocument.Parse(bytes[prefix..].ToArray(), new JsonDocumentOptions
            { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 16 });
            RequireUniqueProperties(document.RootElement);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.TryGetProperty("servers", out _) == root.TryGetProperty("mcpServers", out _))
            { throw new InvalidDataException("MCP configuration requires exactly one servers or mcpServers object."); }
            var reader = new Utf8JsonReader(bytes[prefix..], new JsonReaderOptions
            { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 16 });
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1
                    || reader.GetString() is not ("servers" or "mcpServers")) { continue; }
                reader.Read();
                if (reader.TokenType != JsonTokenType.StartObject) { throw new InvalidDataException("Invalid MCP server collection."); }
                insertion = checked((int)reader.BytesConsumed) + prefix;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var name = reader.GetString();
                    ValidateServerName(name);
                    reader.Read();
                    var start = checked((int)reader.TokenStartIndex) + prefix;
                    if (reader.TokenType != JsonTokenType.StartObject) { throw new InvalidDataException("An MCP server definition must be an object."); }
                    reader.Skip();
                    entries.Add(new(name!, start, checked((int)reader.BytesConsumed) + prefix));
                    if (entries.Count > 128) { throw new InvalidDataException("MCP configuration exceeds 128 servers."); }
                }
                break;
            }
        }
        catch (JsonException exception) { throw new InvalidDataException("Invalid MCP JSON/JSONC configuration.", exception); }
    }

    public IReadOnlyList<string> Names => entries.Select(entry => entry.Name).ToArray();

    public byte[] Read(string name)
    {
        var entry = entries.SingleOrDefault(entry => string.Equals(entry.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidDataException("The exact MCP server entry is missing.");
        return bytes.AsSpan(entry.Start, entry.End - entry.Start).ToArray();
    }

    public byte[] Replace(string name, ReadOnlySpan<byte> definition)
    {
        ValidateServerName(name);
        ValidateServer(definition);
        var entry = entries.SingleOrDefault(entry => string.Equals(entry.Name, name, StringComparison.Ordinal));
        byte[] result;
        if (entry is not null)
        { result = [.. bytes.AsSpan(0, entry.Start), .. definition, .. bytes.AsSpan(entry.End)]; }
        else
        {
            var offset = entries.Count == 0 ? insertion : entries[^1].End;
            var header = Encoding.UTF8.GetBytes((entries.Count == 0 ? "\n" : ",\n") + JsonSerializer.Serialize(name) + ": ");
            result = [.. bytes.AsSpan(0, offset), .. header, .. definition, .. bytes.AsSpan(offset)];
        }
        _ = new McpDefinitionDocument(result);
        return result;
    }

    public static void ValidateServer(ReadOnlySpan<byte> definition)
    {
        try
        {
            using var document = JsonDocument.Parse(definition.ToArray(), new JsonDocumentOptions
            { MaxDepth = 16, CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = document.RootElement;
            RequireUniqueProperties(root);
            if (root.ValueKind != JsonValueKind.Object
                || root.TryGetProperty("command", out var command) == root.TryGetProperty("url", out var url)
                || command.ValueKind != JsonValueKind.Undefined && (command.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(command.GetString()))
                || url.ValueKind != JsonValueKind.Undefined && (url.ValueKind != JsonValueKind.String
                    || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("https" or "http")))
            { throw new InvalidDataException("An MCP server requires exactly one nonempty command or HTTP(S) URL. Saving never authorizes connection or execution."); }
        }
        catch (JsonException exception) { throw new InvalidDataException("Invalid MCP server JSON.", exception); }
    }

    private static void ValidateServerName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl))
        { throw new InvalidDataException("Use a nonempty bounded MCP server name."); }
    }

    private static void RequireUniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) { throw new InvalidDataException("Duplicate JSON properties are not supported."); }
                RequireUniqueProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        { foreach (var item in value.EnumerateArray()) { RequireUniqueProperties(item); } }
    }
}
