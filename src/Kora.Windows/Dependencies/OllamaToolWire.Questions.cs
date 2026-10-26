using System.Text.Json;
using Kora.Core.Dependencies;

namespace Kora.Windows.Dependencies;

internal static partial class OllamaToolWire
{
    private static void WriteQuestionFormat(Utf8JsonWriter writer, ModelToolMode mode)
    {
        // This exact negotiated structured response is not the ordinary assistant prose channel.
        writer.WritePropertyName("format");
        writer.WriteStartObject();
        writer.WriteStartArray("oneOf");
        writer.WriteRawValue("""
            {"type":"object","properties":{"kind":{"const":"answer"},"answer":{"type":"string"}},"required":["kind","answer"],"additionalProperties":false}
            """);
        writer.WriteStartObject();
        writer.WriteString("type", "object");
        writer.WriteStartObject("properties");
        writer.WriteStartObject("kind");
        writer.WriteString("const", "question");
        writer.WriteEndObject();
        writer.WritePropertyName("prompt");
        writer.WriteRawValue("""{"type":"string","maxLength":500}""");
        writer.WriteStartObject("options");
        writer.WriteString("type", "array");
        if (mode == ModelToolMode.ReadOnly) { writer.WriteNumber("minItems", 2); }
        writer.WriteNumber("maxItems", 4);
        writer.WritePropertyName("items");
        writer.WriteRawValue("""{"type":"string","maxLength":80}""");
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteStartArray("required");
        writer.WriteStringValue("kind");
        writer.WriteStringValue("prompt");
        writer.WriteStringValue("options");
        writer.WriteEndArray();
        writer.WriteBoolean("additionalProperties", false);
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
