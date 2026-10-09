using System.Text.Json.Serialization;

namespace Kora.Core.Voice;

[JsonConverter(typeof(JsonStringEnumConverter<SpeechRateSupport>))]
public enum SpeechRateSupport
{
    Unsupported,
    WindowsNative,
}
