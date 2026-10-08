namespace Kora.Core.Hosting;

public sealed record SessionQueueLimits
{
    public SessionQueueLimits(int pendingPerSession = 10, int executionSlots = 1)
    {
        if (pendingPerSession is < 1 or > 10 || executionSlots is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(pendingPerSession));
        }
        PendingPerSession = pendingPerSession;
        ExecutionSlots = executionSlots;
    }

    public int PendingPerSession { get; }
    public int ExecutionSlots { get; }
}
