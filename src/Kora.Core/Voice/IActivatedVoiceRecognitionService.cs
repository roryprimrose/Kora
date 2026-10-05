namespace Kora.Core.Voice;

/// <summary>
/// Local transcription of explicitly activated commands, never ambient audio.
/// Callers still enforce consent, exclusive ownership and current-run recovery holds.
/// </summary>
public interface IActivatedVoiceRecognitionService : IVoiceRecognitionService
{
    /// <summary>Raised on the capture/observer thread; dispatch presentation without blocking native event delivery.</summary>
    event EventHandler<VoiceCaptureStateChangedEventArgs>? CaptureStateChanged;

    event EventHandler<VoiceRecognitionCompletedEventArgs>? RecognitionCompleted;

    /// <summary>
    /// Acknowledge the host's stored activation generation after StartAsync completes.
    /// Releases at most one gated transcript; never opens capture or changes consent.
    /// </summary>
    bool AcceptCaptureGeneration(long generation);

    /// <summary>Current activation generation; retained after normal completion, changed by invalidation or a new activation.</summary>
    long CaptureGeneration { get; }

    bool IsAmbientListeningAvailable { get; }

    /// <summary>True only when recording, native opens/starts and owned cleanup are confirmed quiescent.</summary>
    bool IsCaptureQuiescent { get; }

    Task BeginPushToTalkAsync(
        MicrophoneDevice microphone,
        IEnumerable<string> phrases,
        string? assistantName = null,
        CancellationToken cancellationToken = default);

    Task EndPushToTalkAsync(CancellationToken cancellationToken = default);
}
