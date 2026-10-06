namespace Kora.Setup;

public enum SetupPhase
{
    Detecting,
    Ready,
    Planning,
    Applying,
    PreparingOptional,
    Succeeded,
    Failed,
    Cancelled,
}
