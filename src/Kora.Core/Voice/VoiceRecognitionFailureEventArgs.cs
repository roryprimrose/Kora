namespace Kora.Core.Voice;

public sealed class VoiceRecognitionFailureEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}