namespace Kora.Core.Voice;

/// <summary>One local utterance with independently signalled speech start and completion, not button-release input.</summary>
public interface IQuestionVoiceRecognitionService : IActivatedVoiceRecognitionService
{
    event EventHandler<VoiceCaptureStateChangedEventArgs>? ReplySpeechStarted;

    Task BeginReplyAsync(MicrophoneDevice microphone, IEnumerable<string> phrases,
        Func<bool> isAdmitted, CancellationToken cancellationToken);
}
