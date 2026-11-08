using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kora.Core.Configuration.SettingInspection;

namespace Kora.Core.Tools;

[JsonConverter(typeof(RegisteredSettingsResult.Converter))]
public sealed record RegisteredSettingsResult(ImmutableArray<RegisteredSettingMetadata> Records,
    int TotalRecords, int? NextOffset, SettingObservation? Observation = null,
    SettingInspectionProfile? Profile = null)
{
    public sealed class Converter : JsonConverter<RegisteredSettingsResult>
    {
        public override RegisteredSettingsResult Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var value = document.RootElement.Deserialize<Wire>(options)
                ?? throw new JsonException("A registered settings result is required.");
            if (document.RootElement.TryGetProperty(options.PropertyNamingPolicy?.ConvertName(nameof(Profile)) ?? nameof(Profile),
                out var profile) && (profile.ValueKind != JsonValueKind.String
                    || !string.Equals(profile.GetString(), nameof(SettingInspectionProfile.WindowsRate), StringComparison.Ordinal)))
            { throw new JsonException("Only the dedicated WindowsRate schema may carry an explicit profile."); }
            return new(value.Records, value.TotalRecords, value.NextOffset, value.Observation, value.Profile);
        }

        public override void Write(Utf8JsonWriter writer, RegisteredSettingsResult value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new Wire(value.Records, value.TotalRecords, value.NextOffset, value.Observation, value.Profile), options);

        [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
        private sealed record Wire(ImmutableArray<RegisteredSettingMetadata> Records, int TotalRecords,
            int? NextOffset, SettingObservation? Observation,
            [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SettingInspectionProfile? Profile);
    }
}
