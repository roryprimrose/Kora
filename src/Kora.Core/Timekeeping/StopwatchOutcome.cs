namespace Kora.Core.Timekeeping;

public enum StopwatchOutcome
{
    Started, Observed, Stopped, Reset, Help, AlreadyRunning, ResetRequired, AlreadyStopped,
    NotRunning, Invalid, Denied, TimeUnavailable,
}
