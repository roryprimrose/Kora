namespace Kora.Core.Timekeeping;

public sealed record StopwatchSnapshot(StopwatchState State, TimeSpan ObservedElapsed);
