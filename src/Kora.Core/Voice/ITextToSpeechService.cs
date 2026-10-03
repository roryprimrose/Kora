namespace Kora.Core.Voice;

public interface ITextToSpeechService : IAsyncDisposable
{
    bool IsSpeaking { get; }

    IReadOnlyList<SpeechProvider> GetProviders();

    IReadOnlyList<SpeechVoice> GetVoices();

    SpeechVoice? GetDefaultVoice();

    IReadOnlyList<AudioOutputDevice> GetOutputDevices();

    AudioOutputDevice? GetDefaultOutputDevice();

    Task SpeakAsync(
        string text,
        SpeechVoice voice,
        AudioOutputDevice outputDevice,
        CancellationToken cancellationToken = default);

    Task InstallProviderAsync(
        string providerId,
        IProgress<SpeechProviderInstallProgress> progress,
        CancellationToken cancellationToken = default);

    Task RemoveProviderAsync(
        string providerId,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}