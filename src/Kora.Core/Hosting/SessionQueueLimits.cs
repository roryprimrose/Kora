namespace Kora.Core.Hosting;

public sealed record SessionQueueLimits
{
    public const int MaximumPendingPerSession = 10;
    public const int MaximumExecutionSlots = 2;
    public const int DefaultExecutionSlots = 1;
    public const int DefaultPendingLifetimeMinutes = 30;
    public const int MaximumPendingLifetimeMinutes = 120;

    public SessionQueueLimits(int pendingPerSession = MaximumPendingPerSession, int executionSlots = DefaultExecutionSlots,
        int pendingLifetimeMinutes = DefaultPendingLifetimeMinutes)
    {
        if (pendingPerSession is < 1 or > MaximumPendingPerSession)
        {
            throw new ArgumentOutOfRangeException(nameof(pendingPerSession));
        }
        if (executionSlots is < 1 or > MaximumExecutionSlots)
        {
            throw new ArgumentOutOfRangeException(nameof(executionSlots));
        }
        PendingPerSession = pendingPerSession;
        ExecutionSlots = executionSlots;
        ValidatePendingLifetimeMinutes(pendingLifetimeMinutes);
        PendingLifetimeMinutes = pendingLifetimeMinutes;
    }

    public int PendingPerSession { get; }
    public int ExecutionSlots { get; }
    public int PendingLifetimeMinutes { get; }

    public static void ValidatePendingLifetimeMinutes(int pendingLifetimeMinutes)
    {
        if (pendingLifetimeMinutes is < 1 or > MaximumPendingLifetimeMinutes)
        {
            throw new ArgumentOutOfRangeException(nameof(pendingLifetimeMinutes));
        }
    }
}
