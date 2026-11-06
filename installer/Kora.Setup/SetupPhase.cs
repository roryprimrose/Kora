namespace Kora.Setup;

public enum SetupPhase
{
    Detecting,
    Ready,
    StoppingHost,
    Planning,
    Applying,
    PreparingOptional,
    Succeeded,
    Failed,
    Cancelled,
}
