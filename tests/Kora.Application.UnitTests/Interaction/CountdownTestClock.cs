namespace Kora.Application.UnitTests.Interaction;

internal sealed class CountdownTestClock : TimeProvider
{
    private long timestamp;
    public List<ClockTimer> Timers { get; } = [];
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => timestamp;
    public override DateTimeOffset GetUtcNow() => UtcNow;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ClockTimer(() => callback(state));
        Timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan elapsed)
    {
        timestamp += elapsed.Ticks;
        foreach (var timer in Timers.ToArray().Where(timer => !timer.Disposed)) { timer.Fire(); }
    }

    internal sealed class ClockTimer(Action callback) : ITimer
    {
        internal bool Disposed { get; private set; }
        internal void Fire() => callback();
        public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
