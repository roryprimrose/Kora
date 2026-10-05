namespace Kora.Core.Voice;

/// <summary>One activation's terminal outcome; recording may close before this notification.</summary>
public sealed class VoiceRecognitionCompletedEventArgs(
    long generation,
    VoiceRecognitionCompletionReason reason) : EventArgs
{
    public long Generation { get; } = generation;

    public VoiceRecognitionCompletionReason Reason { get; } = reason;
}
