using Kora.Core.Diagnostics;
using Kora.Core.Timekeeping;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Timekeeping;

public sealed partial class StopwatchWorkflow(TimeProvider time, ILogger<StopwatchWorkflow> logger) : IDisposable
{
    private readonly Lock gate = new();
    private StopwatchSnapshot snapshot = new(StopwatchState.Idle, TimeSpan.Zero);
    private long started;
    private long lastTimestamp;
    private long frequency;
    private bool disposed;
    private long revision;

    public long Revision { get { lock (gate) { return revision; } } }

    public void Retire()
    {
        lock (gate)
        {
            snapshot = new(StopwatchState.Idle, TimeSpan.Zero);
            started = lastTimestamp = frequency = 0;
            revision++;
        }
    }

    public StopwatchResult Execute(StopwatchOperation operation, long expectedRevision, Func<bool> admitted,
        CancellationToken cancellationToken)
    {
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
        lock (gate)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Retire();
                activity.Complete(HostOperationOutcome.Cancelled);
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (disposed || expectedRevision != revision || !admitted())
            {
                Retire();
                activity.Complete(HostOperationOutcome.Completed);
                return new(StopwatchOutcome.Denied, null);
            }
            var outcome = StopwatchRules.Decide(snapshot.State, operation);
            try
            {
                if (outcome == StopwatchOutcome.Started)
                {
                    frequency = time.TimestampFrequency;
                    if (frequency <= 0) { throw new InvalidOperationException("Timestamp frequency must be positive."); }
                    started = lastTimestamp = time.GetTimestamp();
                    snapshot = new(StopwatchState.Running, TimeSpan.Zero);
                }
                else if (outcome is StopwatchOutcome.Observed or StopwatchOutcome.Stopped
                    && snapshot.State == StopwatchState.Running)
                {
                    var now = time.GetTimestamp();
                    var delta = (Int128)now - started;
                    if (time.TimestampFrequency != frequency || now < lastTimestamp
                        || delta * TimeSpan.TicksPerSecond > (Int128)TimeSpan.MaxValue.Ticks * frequency)
                    { throw new InvalidOperationException("Timestamp observation is invalid or exceeds TimeSpan."); }
                    var elapsed = time.GetElapsedTime(started, now);
                    if (elapsed < snapshot.ObservedElapsed)
                    { throw new InvalidOperationException("Elapsed time regressed."); }
                    lastTimestamp = now;
                    snapshot = new(outcome == StopwatchOutcome.Stopped ? StopwatchState.Stopped : StopwatchState.Running, elapsed);
                }
                else if (outcome == StopwatchOutcome.Reset) { Retire(); }
            }
            catch (Exception exception) when (exception is InvalidOperationException or OverflowException or ArgumentOutOfRangeException)
            {
                Retire();
                TimeObservationFailed(logger, exception.GetType().Name);
                activity.Complete(HostOperationOutcome.Failed);
                return new(StopwatchOutcome.TimeUnavailable, null);
            }
            if (!admitted() || cancellationToken.IsCancellationRequested)
            {
                Retire();
                activity.Complete(cancellationToken.IsCancellationRequested
                    ? HostOperationOutcome.Cancelled : HostOperationOutcome.Completed);
                cancellationToken.ThrowIfCancellationRequested();
                return new(StopwatchOutcome.Denied, null);
            }
            activity.Complete(HostOperationOutcome.Completed);
            return new(outcome, snapshot);
        }
    }

    public void Dispose()
    {
        lock (gate) { disposed = true; Retire(); }
    }
}
