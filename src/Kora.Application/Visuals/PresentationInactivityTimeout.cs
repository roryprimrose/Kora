using Kora.Core.Configuration;

namespace Kora.Application.Visuals;

public sealed class PresentationInactivityTimeout
{
    private readonly TimeProvider timeProvider;
    private long? startedAt;
    private TimeSpan timeout;

    public PresentationInactivityTimeout()
        : this(TimeProvider.System)
    {
    }

    public PresentationInactivityTimeout(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.timeProvider = timeProvider;
    }

    public bool IsScheduled => startedAt is not null;

    public TimeSpan Remaining => startedAt is { } start
        ? TimeSpan.FromTicks(Math.Max(0, (timeout - timeProvider.GetElapsedTime(start)).Ticks))
        : TimeSpan.Zero;

    public void Restart(int seconds)
    {
        VisibilityTimeoutSettings.ValidateSeconds(seconds);
        timeout = TimeSpan.FromSeconds(seconds);
        startedAt = timeProvider.GetTimestamp();
    }

    public void Stop() => startedAt = null;

    public bool TryExpire()
    {
        if (!IsScheduled || Remaining > TimeSpan.Zero)
        {
            return false;
        }

        Stop();
        return true;
    }
}
