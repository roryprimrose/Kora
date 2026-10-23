using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed record SpeechInputConfigurationState(
    SpeechInputSelection? Saved, SpeechInputSelection? Desired, SpeechInputGeneration? Effective,
    long Revision, string? UnavailableReason, bool SavedStateKnown = true)
{
    public string Describe() => $"Saved: {(SavedStateKnown ? Saved?.ToString() ?? "not saved (baseline default)" : "unknown / unconfirmed")}{Environment.NewLine}"
        + $"Desired: {Desired?.ToString() ?? "unknown"}{Environment.NewLine}"
        + $"Effective: {Effective?.Selection.ToString() ?? "unavailable"}{Environment.NewLine}"
        + (UnavailableReason ?? "Windows activated adapter selected; actual recognizer/device availability is checked at explicit capture. Name activation and literal dictation are not enabled.");
}
