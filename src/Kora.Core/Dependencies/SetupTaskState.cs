namespace Kora.Core.Dependencies;

public enum SetupTaskState
{
    Queued,
    Running,
    NeedsAction,
    Completed,
    Failed,
    Cancelled,
}
