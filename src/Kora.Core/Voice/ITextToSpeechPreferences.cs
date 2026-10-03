namespace Kora.Core.Voice;

public interface ITextToSpeechPreferences
{
    string? LoadProviderId();

    void SaveProviderId(string providerId);

    string? LoadVoiceId();

    void SaveVoiceId(string voiceId);
}