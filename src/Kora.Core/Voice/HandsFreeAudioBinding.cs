namespace Kora.Core.Voice;

public sealed record HandsFreeAudioBinding(
    long Generation, MicrophoneDevice Microphone, string AssistantName, SpeechInputGeneration Profile)
{
    public Func<bool> CanCapture { get; init; } = static () => false;
    public IReadOnlyList<string> CommandPhrases { get; init; } = [];
}
