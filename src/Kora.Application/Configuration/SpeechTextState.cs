using System.Text.Json;
using System.Text.Json.Serialization;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed record SpeechTextState(string Outcome, long Revision, SpeechTextMode? Saved,
    SpeechTextMode? Effective, string Source, bool Available, string? Recovery,
    SpeechCaptionOptions? SavedCaptionOptions = null, SpeechCaptionOptions? EffectiveCaptionOptions = null,
    SpeechCaptionPinState? PinControl = null)
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
    public SpeechCaptionOptions CaptionDefaults => SpeechCaptionOptions.Default;
    public string CaptionOptionsSource => !Available ? "unavailable" : SavedCaptionOptions is null ? "default" : "saved";
    public IReadOnlyList<SpeechCaptionPlacement> PlacementChoices => Enum.GetValues<SpeechCaptionPlacement>();
    public int MinimumDismissalDelaySeconds => SpeechCaptionOptions.MinimumDelaySeconds;
    public int MaximumDismissalDelaySeconds => SpeechCaptionOptions.MaximumDelaySeconds;
    public IReadOnlyList<SpeechCaptionOptionState> CaptionOptions =>
        Enum.GetValues<SpeechCaptionOption>().Select(GetCaptionOption).ToArray();
    public SpeechCaptionOptionState GetCaptionOption(SpeechCaptionOption option) => new(
        option == SpeechCaptionOption.Placement ? SpeechTextCommand.PlacementId : SpeechTextCommand.DelayId,
        option == SpeechCaptionOption.Placement ? "primary-screen-corner" : "integer",
        option == SpeechCaptionOption.Placement ? "corner" : "seconds",
        GetValue(SavedCaptionOptions, option), GetValue(EffectiveCaptionOptions, option),
        GetValue(SpeechCaptionOptions.Default, option)!, CaptionOptionsSource, Available,
        option == SpeechCaptionOption.Placement ? PlacementChoices.Select(item => item.ToString()).ToArray() : [],
        option == SpeechCaptionOption.DismissalDelay ? MinimumDismissalDelaySeconds : null,
        option == SpeechCaptionOption.DismissalDelay ? MaximumDismissalDelaySeconds : null,
        Revision, Outcome, Recovery);
    private static string? GetValue(SpeechCaptionOptions? options, SpeechCaptionOption option) => options is null ? null
        : option == SpeechCaptionOption.Placement ? options.Placement.ToString()
        : new SpeechCaptionValue.Delay(options.DismissalDelaySeconds).Label;
    private static readonly JsonSerializerOptions Json = CreateJson();
    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter<SpeechTextMode>());
        options.Converters.Add(new JsonStringEnumConverter<SpeechCaptionPlacement>());
        return options;
    }
    public static string Serialize(SpeechTextState state) => JsonSerializer.Serialize(state, Json);
    public static string Serialize(SpeechTextState state, SpeechCaptionOption option) => JsonSerializer.Serialize(state.GetCaptionOption(option), Json);
    public static string SerializePin(SpeechCaptionPinState state) => JsonSerializer.Serialize(state, Json);
}
