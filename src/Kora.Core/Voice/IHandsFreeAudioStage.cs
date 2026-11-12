namespace Kora.Core.Voice;

/// <summary>
/// Host-owned local wake processing, not a general transcription or provider interface.
/// Availability requires the exact configured name/profile and playback rejection to be qualified.
/// </summary>
public interface IHandsFreeAudioStage
{
    bool IsAvailable { get; }
    /// <summary>Checks exact installed host-qualified name/profile support without loading models or capture.</summary>
    bool SupportsProfile(string assistantName, SpeechInputSelection selection) => false;
    /// <summary>Verifies and holds exact installed resources during a joined profile switch, without capture.</summary>
    Task<IAsyncDisposable> PrepareProfileAsync(string assistantName, SpeechInputSelection selection, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("This wake stage does not provide qualified selectable profiles.");

    Task<IHandsFreeAudioSession> OpenAsync(
        HandsFreeAudioBinding binding, CancellationToken cancellationToken);

    /// <summary>Independent exact-profile qualification for wake and command PCM discrimination during playback.</summary>
    bool SupportsSpeechInterruption(string assistantName, SpeechInputSelection selection) => false;

    /// <summary>
    /// Opens one playback-discriminating wake/command session. Known playback must never yield a wake or command;
    /// missing or lost discrimination retires capture. Ordinary wake availability is not qualification for this path.
    /// </summary>
    Task<IHandsFreeAudioSession> OpenSpeechInterruptionAsync(
        HandsFreeAudioBinding binding, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Playback-discriminating wake capture is unavailable.");
}
