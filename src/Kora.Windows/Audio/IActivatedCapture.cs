using Kora.Core.Voice;

namespace Kora.Windows.Audio;

internal interface IActivatedCapture : IAsyncDisposable
{
    event ActivatedAudioAvailable? DataAvailable;

    event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;

    event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed;

    event EventHandler? SpeechDetected;

    Task Completion { get; }

    void Start();

    Task ReleaseRecorder();

    void FinishRecognition(bool cancel);
}
