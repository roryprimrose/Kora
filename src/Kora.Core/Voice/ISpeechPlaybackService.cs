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

    /// <summary>Requires fresh host admission at every synthesis and playback start; completion joins owned output.</summary>
    Task SpeakAdmittedAsync(string text, SpeechVoice voice, AudioOutputDevice outputDevice,
        SpeechStartAdmission admission, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This speech provider has no queued host-admission contract.");

    // Unqualified providers retain speech support but cannot claim caption identity.
    Task SpeakAsync(string text, SpeechVoice voice, AudioOutputDevice outputDevice,
        Guid playbackId, CancellationToken cancellationToken = default) =>
        SpeakAsync(text, voice, outputDevice, cancellationToken);
}