namespace Kora.Application.Voice;

public enum HandsFreeVoiceState
{
    Unavailable,
    Stopped,
    WakeListening,
    CapturingCommand,
    CompletingOperation,
    RecoveryRequired,
}
