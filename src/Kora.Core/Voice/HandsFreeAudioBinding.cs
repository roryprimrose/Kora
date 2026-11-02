namespace Kora.Core.Voice;

public sealed record HandsFreeAudioBinding(
    long Generation, MicrophoneDevice Microphone, string AssistantName, SpeechInputGeneration Profile)
{
    public Func<bool> CanCapture { get; init; } = static () => false;
    public IReadOnlyList<string> CommandPhrases { get; init; } = [];
    /// <summary>Notifies the bound host at the validated native span, before releasing wake or processing later PCM.</summary>
    public Action<WakeWordObservation> WakeActivated { get; init; } = static _ => { };
}
