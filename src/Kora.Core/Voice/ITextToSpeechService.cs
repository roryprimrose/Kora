namespace Kora.Core.Voice;

public interface ITextToSpeechService :
    ISpeechPlaybackService,
    ISpeechCatalog,
    IAudioOutputDeviceCatalog,
    ISpeechProviderManager
{
}