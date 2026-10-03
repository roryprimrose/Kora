namespace Kora.Core.Voice;

public interface IVoiceRecognitionService : IAsyncDisposable
{
    event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;

    event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed;

    bool IsListening { get; }

    IReadOnlyList<MicrophoneDevice> GetMicrophones();

    Task StartAsync(
        MicrophoneDevice microphone,
        IEnumerable<string> phrases,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}