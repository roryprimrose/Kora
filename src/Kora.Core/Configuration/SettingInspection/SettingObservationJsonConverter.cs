using System.Text.Json;
using System.Text.Json.Serialization;
using Kora.Core.Configuration.RegisteredSettings;

namespace Kora.Core.Configuration.SettingInspection;

public sealed class SettingObservationJsonConverter : JsonConverter<SettingObservation>
{
    public override SettingObservation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var wire = JsonSerializer.Deserialize<ObservationWire>(ref reader, options)
            ?? throw new JsonException("A setting observation is required.");
        SettingObservation observation;
        if (wire.Setting == RegisteredSetting.QuietHours && wire.Value is null && wire.QuietHours is { } schedule)
        {
            var parsed = QuietHoursSchedule.Parse(schedule);
            if (!string.Equals(parsed.ToString(), schedule, StringComparison.Ordinal))
            { throw new JsonException("Quiet hours require canonical configured intent."); }
            observation = new(parsed, wire.IsSaved, wire.Revision);
        }
        else if (wire.Setting != RegisteredSetting.QuietHours && wire.Value is { } scalar && wire.QuietHours is null)
        { observation = new(wire.Setting, scalar, wire.IsSaved, wire.Revision); }
        else { throw new JsonException("A setting observation requires exactly its typed value."); }
        if (!string.Equals(wire.Text, observation.Text, StringComparison.Ordinal))
        { throw new JsonException("Setting observation text must match its typed value."); }
        return observation;
    }

    public override void Write(Utf8JsonWriter writer, SettingObservation value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, new ObservationWire(value.Setting,
            value.QuietHours is null ? value.Value : null, value.IsSaved, value.Revision, value.Text,
            value.QuietHours?.ToString()), options);

    private sealed record ObservationWire(RegisteredSetting Setting,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Value,
        bool IsSaved, long Revision, string Text,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? QuietHours);
}
