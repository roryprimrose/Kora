namespace Kora.Core.Voice;

public sealed class VoiceRecognitionFailureEventArgs(string message, long generation = 0,
    VoiceRecognitionFailureReason reason = VoiceRecognitionFailureReason.CaptureFailure) : EventArgs
{
    public string Message { get; } = message;

    public long Generation { get; } = generation;

    public VoiceRecognitionFailureReason Reason { get; } = Enum.IsDefined(reason)
        ? reason : throw new ArgumentOutOfRangeException(nameof(reason));
}