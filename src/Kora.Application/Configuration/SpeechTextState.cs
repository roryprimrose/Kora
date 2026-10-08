using System.Text.Json;
using System.Text.Json.Serialization;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed record SpeechTextState(string Outcome, long Revision, SpeechTextMode? Saved,
    SpeechTextMode? Effective, string Source, bool Available, string? Recovery)
{
    public int Schema => 1;
    public string Id => SpeechTextCommand.OptionId;
    public string Type => "speech-text-mode";
    public SpeechTextMode Default => SpeechTextMode.Off;
    public IReadOnlyList<SpeechTextMode> Choices => Enum.GetValues<SpeechTextMode>();
    public string Scope => "device-local";
    public string Effect => "local-current-playback-presentation-only";
    public string ApplicationTiming => "next eligible playback only; old captions retired immediately";
    public string ResetEffect => "Saves Off; no speech, capture, replay, response-mode or call-policy changes.";
    public string Syntax => SpeechTextCommand.Syntax;
    private static readonly JsonSerializerOptions Json = CreateJson();
    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter<SpeechTextMode>());
        return options;
    }
    public static string Serialize(SpeechTextState state) => JsonSerializer.Serialize(state, Json);
}
