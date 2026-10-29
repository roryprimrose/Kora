namespace Kora.Core.Voice;

public interface ITextToSpeechPreferences
{
    Kora.Core.Configuration.SpokenSummaryLimits? LoadSummaryLimits();

    void SaveSummaryLimits(Kora.Core.Configuration.SpokenSummaryLimits limits);

    /// <summary>Removes the saved summary pair, restoring both default caps.</summary>
    void ResetSummaryLimits();

    Kora.Core.Configuration.SpeechSelection? LoadSelection();

    void SaveSelection(Kora.Core.Configuration.SpeechSelection selection);

    string? LoadProviderId();

    void SaveProviderId(string providerId);

    string? LoadVoiceId();

    void SaveVoiceId(string voiceId);
}