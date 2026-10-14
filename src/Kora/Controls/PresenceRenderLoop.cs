namespace Kora.Controls;

internal sealed class PresenceRenderLoop(
    IPresenceFrameScheduler scheduler,
    Func<bool> canRender,
    Action<TimeSpan, bool> update,
    Func<TimeSpan?> cadence) : IDisposable
{
    internal static readonly TimeSpan ActiveInterval = TimeSpan.FromMilliseconds(33);
    internal static readonly TimeSpan SpeechInterval = TimeSpan.FromMilliseconds(16);
    internal static readonly TimeSpan QuietInterval = TimeSpan.FromMilliseconds(198);

    private IDisposable? pending;
    private long generation;
    private TimeSpan previous;
    private bool running;
    private bool updating;
    private bool disposed;

    internal long TickCount { get; private set; }

    internal void Wake()
    {
        if (disposed || updating)
        {
            return;
        }

        Cancel();
        if (!canRender())
        {
            running = false;
            return;
        }

        var resumed = !running;
        running = true;
        var elapsed = resumed ? TimeSpan.Zero : BoundedElapsed(scheduler.Now - previous);
        previous = scheduler.Now;
        updating = true;
        try
        {
            update(elapsed, resumed);
        }
        finally
        {
            updating = false;
        }
        Queue();
    }

    internal void Stop()
    {
        Cancel();
        running = false;
    }

    public void Dispose()
    {
        disposed = true;
        Stop();
    }

    private void Cancel()
    {
        generation++;
        pending?.Dispose();
        pending = null;
    }

    private void Queue()
    {
        if (disposed || !running || !canRender())
        {
            Stop();
            return;
        }

        if (cadence() is not { } delay)
        {
            return;
        }

        var admittedGeneration = generation;
        pending = scheduler.Schedule(delay, () => Tick(admittedGeneration));
    }

    private void Tick(long admittedGeneration)
    {
        if (disposed || admittedGeneration != generation || !running)
        {
            return;
        }

        pending = null;
        generation++;
        if (!canRender())
        {
            Stop();
            return;
        }

        var now = scheduler.Now;
        var elapsed = now - previous;
        previous = now;
        // A delayed dispatcher must not replay a backlog or integrate a long suspension.
        elapsed = BoundedElapsed(elapsed);
        TickCount++;
        updating = true;
        try
        {
            update(elapsed, false);
        }
        finally
        {
            updating = false;
        }
        Queue();
    }

    private static TimeSpan BoundedElapsed(TimeSpan elapsed) =>
        TimeSpan.FromTicks(Math.Clamp(elapsed.Ticks, 0, QuietInterval.Ticks));
}
