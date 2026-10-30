namespace Kora.Application.Skills;

/// <summary>Cancellation is terminal only after the owned work has joined.</summary>
public enum ObservationWorkflowState
{
    Reviewed,
    Enabled,
    Running,
    Cancelling,
    Succeeded,
    Cancelled,
    Failed,
}
