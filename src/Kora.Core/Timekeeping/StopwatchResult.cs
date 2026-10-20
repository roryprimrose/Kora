namespace Kora.Core.Timekeeping;

public sealed record StopwatchResult(StopwatchOutcome Outcome, StopwatchSnapshot? Snapshot);
