namespace Kora.Core.Voice;

public sealed class VoiceRecognitionFailureEventArgs(string message, long generation = 0) : EventArgs
{
    public string Message { get; } = message;

    public long Generation { get; } = generation;
}