using System.Text.Json;
using System.Text.Json.Serialization;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Voice;

namespace Kora.Core.Configuration.SettingInspection;

public sealed class SettingObservationJsonConverter : JsonConverter<SettingObservation>
{
    public override SettingObservation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var wire = JsonSerializer.Deserialize<ObservationWire>(ref reader, options)
            ?? throw new JsonException("A setting observation is required.");
        SettingObservation observation;
        if (wire.Setting == RegisteredSetting.QuietHours && wire.Value is null && wire.Selection is null
            && wire.QuietHours is { } schedule)
        {
            var parsed = QuietHoursSchedule.Parse(schedule);
            if (!string.Equals(parsed.ToString(), schedule, StringComparison.Ordinal))
            { throw new JsonException("Quiet hours require canonical configured intent."); }
            observation = new(parsed, wire.IsSaved, wire.Revision);
        }
        else if (wire.Setting == RegisteredSetting.SpeechVoice && wire.Value is null && wire.QuietHours is null
            && wire.Selection is { } selection)
        { observation = new(selection, wire.IsSaved, wire.Revision); }
        else if (wire.Setting is not (RegisteredSetting.QuietHours or RegisteredSetting.SpeechVoice)
            && wire.Value is { } scalar && wire.QuietHours is null && wire.Selection is null)
        {
            if (scalar.ValueKind != JsonValueKind.Number)
            { throw new JsonException("A setting scalar must be a JSON number."); }
            if (wire.Setting is RegisteredSetting.CommandStartWait or RegisteredSetting.ReplyStartWait or RegisteredSetting.TrailingSilence)
            {
                var timing = VoiceCaptureTiming.Default.With(RegisteredSettingRegistry.Get(wire.Setting).Id, scalar.GetRawText());
                observation = new(wire.Setting, timing, wire.IsSaved, wire.Revision);
            }
            else
            {
                if (!scalar.TryGetInt32(out var value))
                { throw new JsonException("An integer setting requires an exact supported integer."); }
                observation = new(wire.Setting, value, wire.IsSaved, wire.Revision);
            }
        }
        else { throw new JsonException("A setting observation requires exactly its typed value."); }
        if (!string.Equals(wire.Text, observation.Text, StringComparison.Ordinal))
        { throw new JsonException("Setting observation text must match its typed value."); }
        return observation;
    }

    public override void Write(Utf8JsonWriter writer, SettingObservation value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, new ObservationWire(value.Setting,
            value.Value is { } scalar ? JsonSerializer.SerializeToElement(scalar, options) : null,
            value.IsSaved, value.Revision, value.Text,
            value.QuietHours?.ToString(), value.Selection), options);

    private sealed record ObservationWire(RegisteredSetting Setting,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Value,
        bool IsSaved, long Revision, string Text,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? QuietHours,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SpeechSelection? Selection);
}
