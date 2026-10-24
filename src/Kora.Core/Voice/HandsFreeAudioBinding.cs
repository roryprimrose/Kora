namespace Kora.Core.Voice;

public sealed record HandsFreeAudioBinding(
    long Generation, MicrophoneDevice Microphone, string AssistantName, SpeechInputGeneration Profile);
