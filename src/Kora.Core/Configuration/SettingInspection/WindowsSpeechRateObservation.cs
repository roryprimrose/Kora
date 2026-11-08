using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using Kora.Core.Voice;

namespace Kora.Core.Configuration.SettingInspection;

[JsonConverter(typeof(WindowsSpeechRateObservation.Converter))]
public sealed record WindowsSpeechRateObservation
{
    public WindowsSpeechRateObservation(int desired, int? effective, bool isSaved, string providerId,
        SpeechRateSupport support, long providerRevision, long revision)
    {
        _ = new WindowsSpeechRate(desired);
        if (effective is { } value) { _ = new WindowsSpeechRate(value); }
        new SpeechSelection(providerId, null).Validate();
        if (!Enum.IsDefined(support) || support == SpeechRateSupport.Unsupported && effective is not null
            || support == SpeechRateSupport.WindowsNative && !string.Equals(providerId, SpeechProviderIds.Windows, StringComparison.Ordinal)
            || effective is not null && effective != desired || !isSaved && desired != WindowsSpeechRate.Default.Value)
        { throw new InvalidDataException("Windows rate requires exact confirmed desired and configured provider state."); }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(providerRevision);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        Desired = desired;
        Effective = effective;
        IsSaved = isSaved;
        ProviderId = providerId;
        Support = support;
        ProviderRevision = providerRevision;
        Revision = revision;
    }

    public int Desired { get; }
    public int? Effective { get; }
    public bool IsSaved { get; }
    public string ProviderId { get; }
    public SpeechRateSupport Support { get; }
    public long ProviderRevision { get; }
    public long Revision { get; }
    public string Text => "desired " + Desired.ToString(CultureInfo.InvariantCulture)
        + "; configured effective " + (Effective?.ToString(CultureInfo.InvariantCulture)
            ?? "null (known unsupported/unavailable configured mapping, not zero)")
        + "; support " + Support + "; selected provider " + JsonSerializer.Serialize(ProviderId)
        + "; provider revision " + ProviderRevision.ToString(CultureInfo.InvariantCulture)
        + "; rate revision " + Revision.ToString(CultureInfo.InvariantCulture)
        + "; native units -10 through 10, default 0; Kora-owned Windows synthesizer only; Kokoro unchanged"
        + "; configured state, not actual playback speed, ready voice, acoustic alignment or permission to speak";
    public sealed class Converter : JsonConverter<WindowsSpeechRateObservation>
    {
        private static readonly string[] Fields =
            [nameof(WindowsSpeechRateObservation.Desired), nameof(WindowsSpeechRateObservation.Effective),
         nameof(WindowsSpeechRateObservation.IsSaved), nameof(WindowsSpeechRateObservation.ProviderId),
         nameof(WindowsSpeechRateObservation.Support), nameof(WindowsSpeechRateObservation.ProviderRevision),
         nameof(WindowsSpeechRateObservation.Revision)];

        private static string Name(string name, JsonSerializerOptions options) =>
            options.PropertyNamingPolicy?.ConvertName(name) ?? name;

        public override WindowsSpeechRateObservation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(Fields.Select(field => Name(field, options)).Order(StringComparer.Ordinal), StringComparer.Ordinal))
            { throw new JsonException("Windows rate requires exactly its complete typed fields, including explicit effective null."); }
            JsonElement Field(string name) => root.GetProperty(Name(name, options));
            var effective = Field(nameof(WindowsSpeechRateObservation.Effective));
            return new(Field(nameof(WindowsSpeechRateObservation.Desired)).GetInt32(),
                effective.ValueKind == JsonValueKind.Null ? null : effective.GetInt32(),
                Field(nameof(WindowsSpeechRateObservation.IsSaved)).GetBoolean(),
                Field(nameof(WindowsSpeechRateObservation.ProviderId)).GetString()
                    ?? throw new JsonException("A selected provider is required."),
                Field(nameof(WindowsSpeechRateObservation.Support)).Deserialize<SpeechRateSupport>(options),
                Field(nameof(WindowsSpeechRateObservation.ProviderRevision)).GetInt64(),
                Field(nameof(WindowsSpeechRateObservation.Revision)).GetInt64());
        }

        public override void Write(Utf8JsonWriter writer, WindowsSpeechRateObservation value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber(Name(nameof(value.Desired), options), value.Desired);
            writer.WritePropertyName(Name(nameof(value.Effective), options));
            if (value.Effective is { } effective) { writer.WriteNumberValue(effective); }
            else { writer.WriteNullValue(); }
            writer.WriteBoolean(Name(nameof(value.IsSaved), options), value.IsSaved);
            writer.WriteString(Name(nameof(value.ProviderId), options), value.ProviderId);
            writer.WritePropertyName(Name(nameof(value.Support), options));
            JsonSerializer.Serialize(writer, value.Support, options);
            writer.WriteNumber(Name(nameof(value.ProviderRevision), options), value.ProviderRevision);
            writer.WriteNumber(Name(nameof(value.Revision), options), value.Revision);
            writer.WriteEndObject();
        }
    }
}