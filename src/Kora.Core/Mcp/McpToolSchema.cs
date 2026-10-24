using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kora.Core.Dependencies;

namespace Kora.Core.Mcp;

/// <summary>The first profile supports only bounded, closed, flat scalar argument objects.</summary>
public sealed class McpToolSchema
{
    public const int MaximumSchemaUtf8Bytes = 4096;
    public const int MaximumArgumentsUtf8Bytes = 2048;
    public const int MaximumProperties = 8;
    private readonly ImmutableDictionary<string, Field> fields;
    private readonly ImmutableHashSet<string> required;

    public McpToolSchema(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var bytes = Encode(json);
        using var document = ModelToolProtocol.ReadJson(bytes, MaximumSchemaUtf8Bytes);
        var root = document.RootElement;
        ExactFields(root, "type", "additionalProperties", "properties", "required");
        if (root.GetProperty("type").ValueKind != JsonValueKind.String
            || !string.Equals(root.GetProperty("type").GetString(), "object", StringComparison.Ordinal)
            || root.GetProperty("additionalProperties").ValueKind != JsonValueKind.False
            || root.GetProperty("properties").ValueKind != JsonValueKind.Object
            || root.GetProperty("required").ValueKind != JsonValueKind.Array)
        { throw new InvalidDataException("MCP arguments require a closed object schema."); }
        var properties = root.GetProperty("properties").EnumerateObject().ToArray();
        if (properties.Length > MaximumProperties)
        { throw new InvalidDataException("The MCP schema has too many properties."); }
        var builder = ImmutableDictionary.CreateBuilder<string, Field>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            ValidateName(property.Name);
            var value = property.Value;
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String)
            { throw new InvalidDataException("An MCP schema field requires a supported scalar type."); }
            Field field;
            switch (type.GetString())
            {
                case "string":
                    ExactFields(value, "type", "maxLength");
                    if (value.GetProperty("maxLength").ValueKind != JsonValueKind.Number
                        || !value.GetProperty("maxLength").TryGetInt32(out var length) || length is < 1 or > 512)
                    { throw new InvalidDataException("An MCP string requires a bounded maximum length."); }
                    field = new(JsonValueKind.String, length, 0, 0);
                    break;
                case "integer":
                    ExactFields(value, "type", "minimum", "maximum");
                    if (value.GetProperty("minimum").ValueKind != JsonValueKind.Number
                        || value.GetProperty("maximum").ValueKind != JsonValueKind.Number
                        || !value.GetProperty("minimum").TryGetInt64(out var minimum)
                        || !value.GetProperty("maximum").TryGetInt64(out var maximum)
                        || minimum > maximum)
                    { throw new InvalidDataException("An MCP integer requires an ordered finite range."); }
                    field = new(JsonValueKind.Number, 0, minimum, maximum);
                    break;
                case "boolean":
                    ExactFields(value, "type");
                    field = new(JsonValueKind.True, 0, 0, 0);
                    break;
                default:
                    throw new InvalidDataException("The MCP schema type is unsupported.");
            }
            builder.Add(property.Name, field);
        }
        fields = builder.ToImmutable();
        var requiredBuilder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var item in root.GetProperty("required").EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !fields.ContainsKey(item.GetString()!)
                || !requiredBuilder.Add(item.GetString()!))
            { throw new InvalidDataException("MCP required properties must be unique declared names."); }
        }
        required = requiredBuilder.ToImmutable();
        Json = json;
        Digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public string Json { get; }
    public string Digest { get; }

    public void ValidateArguments(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = ModelToolProtocol.ReadJson(Encode(json), MaximumArgumentsUtf8Bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || required.Any(name => !root.TryGetProperty(name, out _)))
        { throw new InvalidDataException("MCP arguments require every mandatory field."); }
        foreach (var property in root.EnumerateObject())
        {
            if (!fields.TryGetValue(property.Name, out var field) || !ValidValue(field, property.Value))
            { throw new InvalidDataException("MCP arguments do not match the exact reviewed schema."); }
        }
    }

    internal static void ValidateName(string name)
    {
        if (name.Length is < 1 or > 64 || !char.IsAsciiLetter(name[0])
            || name.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-' or '.')))
        { throw new InvalidDataException("MCP names must be bounded ASCII identifiers."); }
    }

    private static bool ValidValue(Field field, JsonElement value) => field.Kind switch
    {
        JsonValueKind.String => value.ValueKind == JsonValueKind.String
            && value.GetString()!.Length <= field.Length,
        JsonValueKind.Number => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            && number >= field.Minimum && number <= field.Maximum,
        _ => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
    };

    private static void ExactFields(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != names.Length
            || names.Any(name => !value.TryGetProperty(name, out _)))
        { throw new InvalidDataException("The MCP schema contains missing or unsupported fields."); }
    }

    private static byte[] Encode(string value)
    {
        try { return new UTF8Encoding(false, true).GetBytes(value); }
        catch (EncoderFallbackException exception)
        { throw new InvalidDataException("Malformed Unicode is not admitted in MCP schemas or arguments.", exception); }
    }

    public override string ToString() => nameof(McpToolSchema);

    private sealed record Field(JsonValueKind Kind, int Length, long Minimum, long Maximum);
}
