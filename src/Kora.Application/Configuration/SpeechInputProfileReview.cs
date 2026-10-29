using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed record SpeechInputProfileReview(
    SpeechInputConfigurationState Current, SpeechInputGeneration Proposed, RequestOrigin Origin)
{
    public string Describe() => Current.Describe() + Environment.NewLine
        + $"Proposed: {Proposed.Selection}{Environment.NewLine}"
        + "Required: the installed English Windows activated recognizer and an admitted microphone at explicit capture. "
        + "WindowsManaged / CaptureLifetime uses the existing capture-owned runtime; no additional model assets are selected. "
        + SpeechInputProfileCatalogue.LocalProfilesUnavailable + Environment.NewLine
        + "Confirm stops and physically joins owned capture, transcription and runtime cleanup before saving and publishing the next immutable generation. "
        + "It never downloads assets, enables listening, opens capture, changes consent or selects hosted audio. "
        + "Cancel leaves the saved and desired selection unchanged. Start push-to-talk explicitly after a confirmed switch.";
}
