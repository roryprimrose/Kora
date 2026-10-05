namespace Kora.Core.Voice;

public interface ISpeechCatalog
{
    IReadOnlyList<SpeechProvider> GetProviders();

    IReadOnlyList<SpeechVoice> GetVoices();

    SpeechVoice? GetDefaultVoice();
}