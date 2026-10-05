namespace Kora.Core.Voice;

public interface IVoiceRecognitionService : IAsyncDisposable
{
    event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;

    event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed;

    bool IsListening { get; }

    long Generation { get; }

    /// <summary>Closes the input/result gate immediately; asynchronous StopAsync releases remaining resources.</summary>
    void InvalidateCapture();

    IReadOnlyList<MicrophoneDevice> GetMicrophones();

    MicrophoneDevice? GetDefaultMicrophone();

    /// <summary>Starts an explicitly activated command; callers must not use a transcription recognizer for ambient wake listening.</summary>
    Task StartAsync(
        MicrophoneDevice microphone,
        IEnumerable<string> phrases,
        string? assistantName = null,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task EndCaptureAsync(CancellationToken cancellationToken = default);
}