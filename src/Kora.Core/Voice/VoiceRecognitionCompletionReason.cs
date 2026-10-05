namespace Kora.Core.Voice;

public enum VoiceRecognitionCompletionReason
{
    Recognized,
    EmptySpeechTimeout,
    MaximumDuration,
    PushToTalkReleased,
    NoSpeechRecognized,
    Invalidated,
    Failed,
}
