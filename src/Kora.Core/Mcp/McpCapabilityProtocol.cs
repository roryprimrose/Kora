using System.Collections.Immutable;
using System.Text.Json;

using Kora.Core.Dependencies;

namespace Kora.Core.Mcp;

/// <summary>Bounded discovery and text reads for server primitives; metadata never establishes authority.</summary>
public static class McpCapabilityProtocol
{
    public const int MaximumPages = 32;
    public const int MaximumItems = 256;
    public const int MaximumInventoryBytes = 256 * 1024;
    public const int MaximumContentBytes = 16 * 1024;

    public static McpServerCapabilities ReadInitialization(ReadOnlySpan<byte> bytes)
    {
        using var document = ModelToolProtocol.ReadJson(bytes, McpProtocol.MaximumDiscoveryUtf8Bytes);
        var result = Result(document.RootElement, 1);
        Fields(result, ["protocolVersion", "capabilities", "serverInfo"], ["instructions"]);
        if (!string.Equals(Text(result, "protocolVersion", 32), McpProtocol.Version, StringComparison.Ordinal)
            || result.GetProperty("capabilities") is not { ValueKind: JsonValueKind.Object } capabilities
            || result.GetProperty("serverInfo") is not { ValueKind: JsonValueKind.Object } server)
        { throw new InvalidDataException("MCP negotiation did not establish the supported protocol."); }
        Fields(server, ["name", "version"], ["title"]);
        _ = Text(server, "name", 256);
        _ = Text(server, "version", 256);
        bool Has(string name)
        {
            if (!capabilities.TryGetProperty(name, out var value)) { return false; }
            if (value.ValueKind != JsonValueKind.Object)
            { throw new InvalidDataException("A declared MCP capability must be an object."); }
            return true;
        }
        var supported = new McpServerCapabilities(Has("tools"), Has("resources"), Has("prompts"));
        if (!supported.Tools && !supported.Resources && !supported.Prompts)
        { throw new InvalidDataException("The MCP server provides no supported primitive."); }
        return supported;
    }

    public static byte[] ListRequest(McpCapabilityKind kind, int requestId, string? cursor = null)
    {
        var method = kind switch
        {
            McpCapabilityKind.Tool => "tools/list",
            McpCapabilityKind.Resource => "resources/list",
            McpCapabilityKind.ResourceTemplate => "resources/templates/list",
            McpCapabilityKind.Prompt => "prompts/list",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        if (cursor is not null) { ValidateText(cursor, 2048); }
        return Request(method, requestId, cursor is null ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new(StringComparer.Ordinal) { ["cursor"] = cursor });
    }

    public static McpCapabilityPage ReadPage(ReadOnlySpan<byte> bytes, McpCapabilityKind kind, int requestId)
    {
        using var document = ModelToolProtocol.ReadJson(bytes, McpProtocol.MaximumDiscoveryUtf8Bytes);
        var result = Result(document.RootElement, requestId);
        var property = kind switch
        {
            McpCapabilityKind.Tool => "tools",
            McpCapabilityKind.Resource => "resources",
            McpCapabilityKind.ResourceTemplate => "resourceTemplates",
            McpCapabilityKind.Prompt => "prompts",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        Fields(result, [property], ["nextCursor"]);
        var array = result.GetProperty(property);
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > MaximumItems)
        { throw new InvalidDataException("The MCP capability page exceeds its item bound."); }
        var items = ImmutableArray.CreateBuilder<McpCapability>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            { throw new InvalidDataException("An MCP capability must be an object."); }
            var name = Text(item, "name", 128);
            var identity = kind == McpCapabilityKind.Resource ? Text(item, "uri", 2048)
                : kind == McpCapabilityKind.ResourceTemplate ? Text(item, "uriTemplate", 2048) : name;
            _ = McpCapabilitySelection.ItemKey(kind, identity);
            if (!identities.Add(identity)) { throw new InvalidDataException("Duplicate MCP capabilities are not admitted."); }
            if (item.TryGetProperty("description", out _)) { _ = Text(item, "description", 2048); }
            string? unavailable = null;
            try
            {
                if (kind == McpCapabilityKind.Tool)
                {
                    McpToolSchema.ValidateName(name);
                    if (!item.TryGetProperty("inputSchema", out var schema))
                    { throw new InvalidDataException("The tool has no input schema."); }
                    _ = new McpToolSchema(schema.GetRawText());
                }
                else if (kind == McpCapabilityKind.Prompt) { _ = PromptParameters(item); }
                else if (kind == McpCapabilityKind.ResourceTemplate) { _ = new McpResourceTemplate(identity); }
                else { ValidateUri(identity); }
            }
            catch (InvalidDataException) { unavailable = "Unsupported definition for the bounded text profile."; }
            items.Add(new(kind, identity, name, item.GetRawText(), unavailable));
        }
        var cursor = result.TryGetProperty("nextCursor", out _) ? Text(result, "nextCursor", 2048) : null;
        return new(items.ToImmutable(), cursor);
    }

    public static byte[] ResourceRequest(string uri, int requestId)
    {
        ValidateUri(uri);
        return Request("resources/read", requestId, new { uri });
    }

    public static byte[] PromptRequest(McpCapability prompt, string arguments, int requestId)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        if (prompt.Kind != McpCapabilityKind.Prompt || prompt.UnavailableReason is not null)
        { throw new InvalidDataException("Choose a supported exact MCP prompt."); }
        using var definition = ModelToolProtocol.ReadJson(System.Text.Encoding.UTF8.GetBytes(prompt.DefinitionJson),
            McpProtocol.MaximumDiscoveryUtf8Bytes);
        var parameters = PromptParameters(definition.RootElement);
        using var input = ModelToolProtocol.ReadJson(System.Text.Encoding.UTF8.GetBytes(arguments), McpToolSchema.MaximumArgumentsUtf8Bytes);
        if (input.RootElement.ValueKind != JsonValueKind.Object)
        { throw new InvalidDataException("Prompt arguments must be a JSON object."); }
        var supplied = input.RootElement.EnumerateObject().ToArray();
        if (supplied.Any(item => !parameters.Any(parameter => string.Equals(parameter.Name, item.Name, StringComparison.Ordinal))
                || item.Value.ValueKind != JsonValueKind.String || item.Value.GetString()!.Length > 1024)
            || parameters.Any(parameter => parameter.Required && !supplied.Any(item => string.Equals(item.Name, parameter.Name, StringComparison.Ordinal))))
        { throw new InvalidDataException("Prompt arguments must match the exact declared string parameters."); }
        return Request("prompts/get", requestId, new { name = prompt.Id, arguments = input.RootElement });
    }

    public static McpTextDocument ReadResource(ReadOnlySpan<byte> bytes, int requestId, string requestedUri)
    {
        ValidateUri(requestedUri);
        using var document = ModelToolProtocol.ReadJson(bytes, MaximumContentBytes);
        var result = Result(document.RootElement, requestId);
        Fields(result, ["contents"], []);
        var contents = result.GetProperty("contents");
        if (contents.ValueKind != JsonValueKind.Array || contents.GetArrayLength() is < 1 or > 16)
        { throw new InvalidDataException("The complete resource content collection is unsupported."); }
        var blocks = contents.EnumerateArray().Select(value => ResourceBlock(value, null)).ToArray();
        if (blocks.Any(block => !string.Equals(block.Uri, requestedUri, StringComparison.Ordinal)))
        { throw new InvalidDataException("The resource response does not match the reviewed exact URI."); }
        return new(blocks);
    }

    public static McpTextDocument ReadPrompt(ReadOnlySpan<byte> bytes, int requestId)
    {
        using var document = ModelToolProtocol.ReadJson(bytes, MaximumContentBytes);
        var result = Result(document.RootElement, requestId);
        Fields(result, ["messages"], ["description"]);
        var messages = result.GetProperty("messages");
        if (messages.ValueKind != JsonValueKind.Array || messages.GetArrayLength() is < 1 or > 16)
        { throw new InvalidDataException("The complete prompt message collection is unsupported."); }
        var blocks = new List<McpTextBlock>();
        foreach (var message in messages.EnumerateArray())
        {
            Fields(message, ["role", "content"], []);
            var role = Text(message, "role", 16);
            if (role is not ("user" or "assistant")) { throw new InvalidDataException("Unsupported MCP prompt role."); }
            var content = message.GetProperty("content");
            var type = Text(content, "type", 32);
            if (string.Equals(type, "text", StringComparison.Ordinal))
            {
                Fields(content, ["type", "text"], ["annotations"]);
                blocks.Add(new(Text(content, "text", MaximumContentBytes, allowEmpty: true), null, null, role));
            }
            else if (string.Equals(type, "resource", StringComparison.Ordinal))
            {
                Fields(content, ["type", "resource"], ["annotations"]);
                blocks.Add(ResourceBlock(content.GetProperty("resource"), role));
            }
            else { throw new InvalidDataException("This MCP profile accepts text and embedded text resources only. No part of this prompt was applied."); }
        }
        return new(blocks);
    }

    public static void ValidateUri(string uri)
    {
        ValidateText(uri, 2048);
        if (!System.Uri.TryCreate(uri, UriKind.Absolute, out _))
        { throw new InvalidDataException("An MCP resource requires an absolute server-owned URI."); }
    }

    private static McpTextBlock ResourceBlock(JsonElement content, string? role)
    {
        Fields(content, ["uri", "text"], ["mimeType", "annotations"]);
        var uri = Text(content, "uri", 2048);
        ValidateUri(uri);
        var mime = content.TryGetProperty("mimeType", out _) ? Text(content, "mimeType", 128) : null;
        if (mime is not null && !mime.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(mime, "application/json", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(mime, "application/xml", StringComparison.OrdinalIgnoreCase)
            && !mime.EndsWith("+json", StringComparison.OrdinalIgnoreCase) && !mime.EndsWith("+xml", StringComparison.OrdinalIgnoreCase))
        { throw new InvalidDataException("This MCP profile accepts text resource MIME types only."); }
        return new(Text(content, "text", MaximumContentBytes, allowEmpty: true), uri, mime, role);
    }

    private static (string Name, bool Required)[] PromptParameters(JsonElement prompt)
    {
        if (!prompt.TryGetProperty("arguments", out var arguments)) { return []; }
        if (arguments.ValueKind != JsonValueKind.Array || arguments.GetArrayLength() > 8)
        { throw new InvalidDataException("The MCP prompt has unsupported parameters."); }
        var result = arguments.EnumerateArray().Select(argument =>
        {
            Fields(argument, ["name"], ["description", "required"]);
            var name = Text(argument, "name", 64);
            McpToolSchema.ValidateName(name);
            var required = argument.TryGetProperty("required", out var value)
                ? value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean()
                    : throw new InvalidDataException("Prompt required must be a Boolean.")
                : false;
            return (Name: name, Required: required);
        }).ToArray();
        if (result.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != result.Length)
        { throw new InvalidDataException("Duplicate prompt arguments are not admitted."); }
        return result;
    }

    private static byte[] Request<T>(string method, int requestId, T parameters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(requestId, 2);
        var wire = JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", id = requestId, method, @params = parameters });
        if (wire.Length > McpProtocol.MaximumDiscoveryUtf8Bytes)
        { throw new InvalidDataException("The complete MCP request exceeds its byte bound."); }
        return wire;
    }

    private static JsonElement Result(JsonElement root, int requestId)
    {
        Fields(root, ["jsonrpc", "id"], ["result", "error"]);
        if (!string.Equals(Text(root, "jsonrpc", 8), "2.0", StringComparison.Ordinal) || root.GetProperty("id").ValueKind != JsonValueKind.Number
            || !root.GetProperty("id").TryGetInt32(out var id) || id != requestId
            || root.TryGetProperty("result", out var result) == root.TryGetProperty("error", out var error))
        { throw new InvalidDataException("The MCP response does not match the exact request."); }
        if (error.ValueKind != JsonValueKind.Undefined)
        {
            Fields(error, ["code", "message"], ["data"]);
            if (error.GetProperty("code").ValueKind != JsonValueKind.Number || !error.GetProperty("code").TryGetInt32(out var code))
            { throw new InvalidDataException("The MCP error code is invalid."); }
            _ = Text(error, "message", 2048);
            throw new McpProtocolException(code);
        }
        return result;
    }

    private static string Text(JsonElement value, string name, int maximum, bool allowEmpty = false)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var text)
            || text.ValueKind != JsonValueKind.String)
        { throw new InvalidDataException("The MCP response contains missing or unbounded text."); }
        var result = text.GetString()!;
        if (result.Length > maximum || !allowEmpty && string.IsNullOrWhiteSpace(result))
        { throw new InvalidDataException("The MCP response contains missing or unbounded text."); }
        if (!allowEmpty && result.Any(char.IsControl)) { throw new InvalidDataException("MCP metadata contains control characters."); }
        return result;
    }

    private static void ValidateText(string text, int maximum)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximum || text.Any(char.IsControl))
        { throw new InvalidDataException("MCP metadata must be nonempty bounded text."); }
    }

    private static void Fields(JsonElement value, string[] required, string[] optional)
    {
        if (value.ValueKind != JsonValueKind.Object || required.Any(name => !value.TryGetProperty(name, out _))
            || value.EnumerateObject().Any(property => !required.Contains(property.Name, StringComparer.Ordinal)
                && !optional.Contains(property.Name, StringComparer.Ordinal)))
        { throw new InvalidDataException("The MCP response has missing or unsupported fields."); }
    }
}