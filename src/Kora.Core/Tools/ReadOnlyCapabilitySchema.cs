using System.Text;
using System.Text.Json;

namespace Kora.Core.Tools;

/// <summary>Exact portable input schemas for the original six observation capabilities.</summary>
public static class ReadOnlyCapabilitySchema
{
    public static string Serialize(string id)
    {
        var descriptor = ReadOnlyCapabilityCatalog.FindExact(id)
            ?? throw new InvalidDataException("Only original observation capabilities have this schema.");
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WriteBoolean("additionalProperties", false);
            writer.WriteStartObject("properties");
            if (descriptor.Input == CapabilityInputShape.Page)
            {
                Integer(writer, "offset", 0, int.MaxValue);
                Integer(writer, "count", 1, descriptor.Limits.MaximumRecords);
            }
            else if (descriptor.Input == CapabilityInputShape.Id)
            {
                writer.WriteStartObject("id");
                writer.WriteString("type", "string");
                writer.WriteStartArray("enum");
                if (string.Equals(id, ReadOnlyCapabilityCatalog.Get, StringComparison.Ordinal))
                {
                    foreach (var item in ReadOnlyCapabilityCatalog.Descriptors) { writer.WriteStringValue(item.Id); }
                }
                else { writer.WriteStringValue(ReadOnlyCapabilityCatalog.LocalRuntimeId); }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteStartArray("required");
            if (descriptor.Input == CapabilityInputShape.Id) { writer.WriteStringValue("id"); }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(memory.ToArray());
    }

    private static void Integer(Utf8JsonWriter writer, string name, int minimum, int maximum)
    {
        writer.WriteStartObject(name);
        writer.WriteString("type", "integer");
        writer.WriteNumber("minimum", minimum);
        writer.WriteNumber("maximum", maximum);
        writer.WriteEndObject();
    }
}
