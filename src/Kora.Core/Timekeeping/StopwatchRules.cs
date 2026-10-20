namespace Kora.Core.Timekeeping;

public static class StopwatchRules
{
    public static StopwatchOutcome Decide(StopwatchState state, StopwatchOperation operation) => operation switch
    {
        StopwatchOperation.Start => state switch
        {
            StopwatchState.Idle => StopwatchOutcome.Started,
            StopwatchState.Running => StopwatchOutcome.AlreadyRunning,
            _ => StopwatchOutcome.ResetRequired,
        },
        StopwatchOperation.Status => StopwatchOutcome.Observed,
        StopwatchOperation.Stop => state switch
        {
            StopwatchState.Running => StopwatchOutcome.Stopped,
            StopwatchState.Stopped => StopwatchOutcome.AlreadyStopped,
            _ => StopwatchOutcome.NotRunning,
        },
        StopwatchOperation.Reset => StopwatchOutcome.Reset,
        StopwatchOperation.Help => StopwatchOutcome.Help,
        _ => StopwatchOutcome.Invalid,
    };
}
