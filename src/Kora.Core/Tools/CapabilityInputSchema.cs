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
        if (descriptor.Input == CapabilityInputShape.SettingsQuery)
        {
            writer.WriteStartObject("query");
            writer.WriteString("type", "string");
            writer.WriteNumber("minLength", 1);
            writer.WriteNumber("maxLength", SettingsNavigationTool.MaximumQueryLength);
            writer.WriteEndObject();
        }
        else if (descriptor.Input == CapabilityInputShape.ReleaseVersion)
        {
            writer.WriteStartObject("version");
            writer.WriteString("type", "string");
            writer.WriteNumber("minLength", 5);
            writer.WriteNumber("maxLength", 64);
            writer.WriteEndObject();
        }
        else if (descriptor.Input == CapabilityInputShape.Page)
        {
            Integer(writer, "offset", 0, boundedOffset ? int.MaxValue : null);
            Integer(writer, "count", 1, descriptor.Limits.MaximumRecords);
        }
        else if (descriptor.Input == CapabilityInputShape.SessionId)
        {
            writer.WriteStartObject("id");
            writer.WriteString("type", "string");
            writer.WriteNumber("minLength", 36);
            writer.WriteNumber("maxLength", 36);
            writer.WriteString("pattern", "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$");
            writer.WriteEndObject();
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
        else if (descriptor.Input == CapabilityInputShape.ProviderSettings)
        {
            writer.WriteStartObject("service");
            writer.WriteString("type", "string");
            writer.WriteStartArray("enum");
            foreach (var service in Enum.GetValues<Providers.ProviderService>())
            {
                if (service != Providers.ProviderService.Unset
                    || string.Equals(descriptor.Id, ProviderSettingsTools.Propose, StringComparison.Ordinal))
                { writer.WriteStringValue(service.ToString()); }
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            if (string.Equals(descriptor.Id, ProviderSettingsTools.Models, StringComparison.Ordinal))
            {
                Integer(writer, "offset", 0, int.MaxValue);
                Integer(writer, "count", 1, descriptor.Limits.MaximumRecords);
            }
            if (descriptor.Id is ProviderSettingsTools.Model or ProviderSettingsTools.Propose)
            {
                foreach (var field in string.Equals(descriptor.Id, ProviderSettingsTools.Model, StringComparison.Ordinal)
                    ? new[] { "model" } : ["model", "reasoning", "context", "account"])
                {
                    writer.WriteStartObject(field);
                    writer.WriteString("type", "string");
                    writer.WriteEndObject();
                }
            }
            if (string.Equals(descriptor.Id, ProviderSettingsTools.Propose, StringComparison.Ordinal))
            {
                Integer(writer, "revision", 1, null);
                writer.WriteStartObject("clearModel");
                writer.WriteString("type", "boolean");
                writer.WriteEndObject();
            }
        }
        writer.WriteEndObject();
        var required = descriptor.Input switch
        {
            CapabilityInputShape.Id or CapabilityInputShape.RegisteredSettingId or CapabilityInputShape.SessionId => "id",
            CapabilityInputShape.WebPageAddress => "address",
            CapabilityInputShape.MetadataReference => "ref",
            CapabilityInputShape.ProviderSettings => "service",
            CapabilityInputShape.SettingsQuery => "query",
            _ => null,
        };
        if (required is not null || includeEmptyRequired)
        {
            writer.WriteStartArray("required");
            if (required is not null) { writer.WriteStringValue(required); }
            if (string.Equals(descriptor.Id, ProviderSettingsTools.Model, StringComparison.Ordinal))
            { writer.WriteStringValue("model"); }
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
