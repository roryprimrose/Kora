using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed record SpeechInputProfileReview(
    SpeechInputConfigurationState Current, SpeechInputGeneration Proposed, RequestOrigin Origin)
{
    public string? AssistantName { get; init; }
    public string Describe() => Current.Describe() + Environment.NewLine
        + $"Proposed: {Proposed.Selection}{Environment.NewLine}"
        + (Proposed.Selection.ProfileId is SpeechInputProfileCatalogue.NativeWakeProfileId
            ? $"Required: independently host-qualified exact name '{AssistantName}', installed PocketSphinx 5.1.1 en-US dictionary/acoustic/VAD assets and native ABI, CPU / CaptureLifetime, existing Windows command ASR and playback exclusion. "
            : "Required: the installed English Windows activated recognizer and an admitted microphone at explicit capture. "
                + "WindowsManaged / CaptureLifetime uses the existing capture-owned runtime; no additional model assets are selected. ")
        + Environment.NewLine
        + "Confirm stops and physically joins owned capture, transcription and runtime cleanup before saving and publishing the next immutable generation. "
        + "It never downloads assets, enables listening, opens capture, changes consent or selects hosted audio. "
        + "Cancel leaves the saved and desired selection unchanged. Start push-to-talk or qualified wake listening explicitly after a confirmed switch.";
}
