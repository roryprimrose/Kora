namespace Kora.Core.Voice;

public interface ISpeechPlaybackService : IAsyncDisposable
{
    bool IsSpeaking { get; }

    SpeechPlaybackFrame PlaybackFrame { get; }

    void InvalidateOutput();

    Task SpeakAsync(
        string text,
        SpeechVoice voice,
        AudioOutputDevice outputDevice,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}