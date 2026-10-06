namespace Kora.Windows.IntegrationTests.Session;

internal sealed class PrivacyPollingTimeProvider : TimeProvider
{
    public PollTimer Timer { get; private set; } = null!;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Timer = new PollTimer(callback, state, dueTime, period);
        return Timer;
    }

    internal sealed class PollTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) : ITimer
    {
        public TimeSpan DueTime { get; private set; } = dueTime;
        public TimeSpan Period { get; private set; } = period;
        public bool IsDisposed { get; private set; }
        public Action? BeforeDispose { get; set; }

        // Also models a callback already queued when the underlying timer is disposed.
        public void Tick() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            DueTime = dueTime;
            Period = period;
            return !IsDisposed;
        }

        public void Dispose()
        {
            IsDisposed = true;
            BeforeDispose?.Invoke();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
