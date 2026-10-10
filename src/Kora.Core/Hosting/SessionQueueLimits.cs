namespace Kora.Core.Hosting;

public sealed record SessionQueueLimits
{
    public const int MaximumPendingPerSession = 10;
    public const int MaximumExecutionSlots = 2;
    public const int DefaultExecutionSlots = 1;
    public const int DefaultPendingLifetimeMinutes = 30;
    public const int MaximumPendingLifetimeMinutes = 120;
    public const int DefaultActiveBudgetMinutes = 5;
    public const int MaximumActiveBudgetMinutes = 60;

    public SessionQueueLimits(int pendingPerSession = MaximumPendingPerSession, int executionSlots = DefaultExecutionSlots,
        int pendingLifetimeMinutes = DefaultPendingLifetimeMinutes, int activeBudgetMinutes = DefaultActiveBudgetMinutes)
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
        ValidateActiveBudgetMinutes(activeBudgetMinutes);
        ActiveBudgetMinutes = activeBudgetMinutes;
    }

    public int PendingPerSession { get; }
    public int ExecutionSlots { get; }
    public int PendingLifetimeMinutes { get; }
    public int ActiveBudgetMinutes { get; }

    public static void ValidateActiveBudgetMinutes(int activeBudgetMinutes)
    {
        if (activeBudgetMinutes is < 1 or > MaximumActiveBudgetMinutes)
        {
            throw new ArgumentOutOfRangeException(nameof(activeBudgetMinutes));
        }
    }

    public static TimeSpan ActiveBudget(int activeBudgetMinutes)
    {
        ValidateActiveBudgetMinutes(activeBudgetMinutes);
        return TimeSpan.FromMinutes(activeBudgetMinutes);
    }

    public static DateTimeOffset ActiveDeadlineAt(DateTimeOffset admittedAt, int activeBudgetMinutes) =>
        admittedAt.Add(ActiveBudget(activeBudgetMinutes));

    public static void ValidatePendingLifetimeMinutes(int pendingLifetimeMinutes)
    {
        if (pendingLifetimeMinutes is < 1 or > MaximumPendingLifetimeMinutes)
        {
            throw new ArgumentOutOfRangeException(nameof(pendingLifetimeMinutes));
        }
    }
}
