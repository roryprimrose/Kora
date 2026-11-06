using System.Text.Json;
using Kora.Core.Configuration.RegisteredSettings;

namespace Kora.Core.Tools;

/// <summary>Shared schemas for host-owned tools, independent of provider or transport framing.</summary>
public static class CapabilityInputSchema
{
    public static void Write(Utf8JsonWriter writer, CapabilityDescriptor descriptor,
        bool includeEmptyRequired = false, bool boundedOffset = false)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        writer.WriteStartObject("properties");
        if (descriptor.Input == CapabilityInputShape.Page)
        {
            Integer(writer, "offset", 0, boundedOffset ? int.MaxValue : null);
            Integer(writer, "count", 1, descriptor.Limits.MaximumRecords);
        }
        else if (descriptor.Input is CapabilityInputShape.Id or CapabilityInputShape.RegisteredSettingId)
        {
            writer.WriteStartObject("id");
            writer.WriteString("type", "string");
            writer.WriteStartArray("enum");
            if (descriptor.Input == CapabilityInputShape.RegisteredSettingId)
            {
                foreach (var item in RegisteredSettingRegistry.Settings) { writer.WriteStringValue(item.Id); }
            }
            else if (string.Equals(descriptor.Id, ReadOnlyCapabilityCatalog.Get, StringComparison.Ordinal))
            {
                foreach (var item in ReadOnlyCapabilityCatalog.Descriptors) { writer.WriteStringValue(item.Id); }
            }
            else { writer.WriteStringValue(ReadOnlyCapabilityCatalog.LocalRuntimeId); }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        else if (descriptor.Input == CapabilityInputShape.WebPageAddress)
        {
            writer.WriteStartObject("address");
            writer.WriteString("type", "string");
            writer.WriteEndObject();
        }
        else if (descriptor.Input == CapabilityInputShape.MetadataReference)
        {
            writer.WriteStartObject("ref");
            writer.WriteString("type", "string");
            writer.WriteNumber("minLength", 32);
            writer.WriteNumber("maxLength", 32);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        var required = descriptor.Input switch
        {
            CapabilityInputShape.Id or CapabilityInputShape.RegisteredSettingId => "id",
            CapabilityInputShape.WebPageAddress => "address",
            CapabilityInputShape.MetadataReference => "ref",
            _ => null,
        };
        if (required is not null || includeEmptyRequired)
        {
            writer.WriteStartArray("required");
            if (required is not null) { writer.WriteStringValue(required); }
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }

    private static void Integer(Utf8JsonWriter writer, string name, int minimum, int? maximum)
    {
        writer.WriteStartObject(name);
        writer.WriteString("type", "integer");
        writer.WriteNumber("minimum", minimum);
        if (maximum is { } value) { writer.WriteNumber("maximum", value); }
        writer.WriteEndObject();
    }
}
