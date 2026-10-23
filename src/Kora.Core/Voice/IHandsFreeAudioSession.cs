namespace Kora.Core.Voice;

/// <summary>
/// Owns one capture generation. Ambient samples reach only the local keyword detector and
/// at most two seconds of memory-only pre-roll. Disposal joins capture and clears all audio.
/// </summary>
public interface IHandsFreeAudioSession : IAsyncDisposable
{
    bool IsQuiescent { get; }

    /// <summary>Never invokes general transcription. The boundary excludes the complete wake phrase.</summary>
    Task<WakeWordObservation> WaitForWakeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Transcribes only the bounded post-name interval, including immediate no-pause words.
    /// Excludes preceding audio, the wake phrase and known playback. Returns at most one final result.
    /// Empty activation returns null; partial results never leave this seam.
    /// </summary>
    Task<VoiceTranscriptEventArgs?> CaptureCommandAsync(
        WakeWordObservation wake, CancellationToken cancellationToken);

    /// <summary>Synchronously invalidates callbacks and clears bounded memory before asynchronous release.</summary>
    void Invalidate();
}
