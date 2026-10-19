using System.Text;
using System.Text.Json;
using Kora.Core.Dependencies;

namespace Kora.Windows.Dependencies;

internal static partial class OllamaToolWire
{
    // This walker preserves the native envelope's fields until validation. Canonical admission is still Core-owned.
    private static JsonDocument ReadJson(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > ModelToolProtocol.MaximumOutputUtf8Bytes)
        { throw new InvalidDataException("The native JSON envelope exceeds the complete output bound."); }
        try
        {
            var text = StrictUtf8.GetString(bytes);
            var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 });
            try { ValidateNativeJson(document.RootElement); }
            catch { document.Dispose(); throw; }
            return document;
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException or InvalidOperationException)
        { throw new InvalidDataException("The complete native response is malformed.", exception); }
    }

    private static void ValidateNativeJson(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in element.EnumerateObject())
            {
                ValidateNativeText(field.Name);
                if (!names.Add(field.Name)) { throw new InvalidDataException("Duplicate native fields are not admitted."); }
                ValidateNativeJson(field.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in element.EnumerateArray()) { ValidateNativeJson(value); }
        }
        else if (element.ValueKind == JsonValueKind.String) { ValidateNativeText(element.GetString()!); }
    }

    private static void ValidateNativeText(string value)
    {
        try { StrictUtf8.GetByteCount(value); }
        catch (EncoderFallbackException exception) { throw new InvalidDataException("Malformed native Unicode.", exception); }
        if (value.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
        { throw new InvalidDataException("Native control characters are not admitted."); }
    }

    internal static void VerifyVersion(byte[] bytes, string version)
    {
        using var document = ReadJson(bytes);
        RequireFields(document.RootElement, ["version"]);
        if (!string.Equals(Text(document.RootElement, "version"), version, StringComparison.Ordinal))
        { throw new InvalidDataException("The observed runtime differs from the exact host-owned profile."); }
    }

    internal static void VerifyModel(byte[] bytes, string model, string digest)
    {
        using var document = ReadJson(bytes);
        RequireFields(document.RootElement, ["models"]);
        var models = document.RootElement.GetProperty("models");
        if (models.ValueKind != JsonValueKind.Array) { throw new InvalidDataException("Invalid native model catalogue."); }
        var names = new HashSet<string>(StringComparer.Ordinal);
        var found = false;
        foreach (var item in models.EnumerateArray())
        {
            RequireFields(item, ["name", "digest"], ["model", "modified_at", "size", "details"]);
            var name = Text(item, "name");
            if (!names.Add(name)) { throw new InvalidDataException("Ambiguous native model catalogue."); }
            if (item.TryGetProperty("model", out _) && !string.Equals(Text(item, "model"), name, StringComparison.Ordinal))
            { throw new InvalidDataException("Native model aliases disagree."); }
            if (item.TryGetProperty("size", out var size)
                && (size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out var length) || length < 0))
            { throw new InvalidDataException("Invalid native model size."); }
            if (item.TryGetProperty("modified_at", out _)) { _ = Text(item, "modified_at"); }
            if (item.TryGetProperty("details", out var details))
            {
                RequireFields(details, ["parent_model", "format", "family", "families", "parameter_size", "quantization_level"]);
                foreach (var field in details.EnumerateObject())
                {
                    if (string.Equals(field.Name, "families", StringComparison.Ordinal))
                    {
                        if (field.Value.ValueKind != JsonValueKind.Array
                            || field.Value.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String))
                        { throw new InvalidDataException("Invalid native model families."); }
                    }
                    else { _ = Text(details, field.Name); }
                }
            }
            var observed = Text(item, "digest");
            if (observed.StartsWith("sha256:", StringComparison.Ordinal)) { observed = observed[7..]; }
            if (!WindowsOllamaToolEndpointBinding.IsDigest(observed))
            { throw new InvalidDataException("Invalid native model digest."); }
            if (string.Equals(name, model, StringComparison.Ordinal))
            {
                if (!string.Equals(observed, digest, StringComparison.OrdinalIgnoreCase))
                { throw new InvalidDataException("The observed model differs from the exact host-owned digest."); }
                found = true;
            }
        }
        if (!found) { throw new InvalidDataException("The exact host-selected model is unavailable."); }
    }
}
