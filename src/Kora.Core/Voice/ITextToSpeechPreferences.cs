namespace Kora.Core.Voice;

public interface ITextToSpeechPreferences
{
    Kora.Core.Configuration.SpokenSummaryLimits? LoadSummaryLimits();

    void SaveSummaryLimits(Kora.Core.Configuration.SpokenSummaryLimits limits);

    Kora.Core.Configuration.SpeechSelection? LoadSelection();

    void SaveSelection(Kora.Core.Configuration.SpeechSelection selection);

    string? LoadProviderId();

    void SaveProviderId(string providerId);

    string? LoadVoiceId();

    void SaveVoiceId(string voiceId);
}