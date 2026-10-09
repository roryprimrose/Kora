namespace Kora.Core.Hosting;

public sealed record SessionQueueLimits
{
    public const int MaximumPendingPerSession = 10;
    public const int MaximumExecutionSlots = 2;
    public const int DefaultExecutionSlots = 1;

    public SessionQueueLimits(int pendingPerSession = MaximumPendingPerSession, int executionSlots = DefaultExecutionSlots)
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
    }

    public int PendingPerSession { get; }
    public int ExecutionSlots { get; }
}
