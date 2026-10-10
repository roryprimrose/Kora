using System.Globalization;
using Kora.Core.Hosting;

namespace Kora.Core.Configuration;

/// <summary>Overrides for the existing synchronous read-only local-version queue, not general execution.</summary>
public sealed record SessionQueuePreferences
{
    public SessionQueuePreferences(int? pendingPerSession = null, int? executionSlots = null, int? pendingLifetimeMinutes = null,
        int? activeBudgetMinutes = null)
    {
        _ = new SessionQueueLimits(pendingPerSession ?? SessionQueueLimits.MaximumPendingPerSession,
            executionSlots ?? SessionQueueLimits.DefaultExecutionSlots,
            pendingLifetimeMinutes ?? SessionQueueLimits.DefaultPendingLifetimeMinutes,
            activeBudgetMinutes ?? SessionQueueLimits.DefaultActiveBudgetMinutes);
        PendingPerSession = pendingPerSession;
        ExecutionSlots = executionSlots;
        PendingLifetimeMinutes = pendingLifetimeMinutes;
        ActiveBudgetMinutes = activeBudgetMinutes;
    }

    public int? PendingPerSession { get; }
    public int? ExecutionSlots { get; }
    public int? PendingLifetimeMinutes { get; }
    public int? ActiveBudgetMinutes { get; }
    public SessionQueueLimits Limits => new(PendingPerSession ?? SessionQueueLimits.MaximumPendingPerSession,
        ExecutionSlots ?? SessionQueueLimits.DefaultExecutionSlots,
        PendingLifetimeMinutes ?? SessionQueueLimits.DefaultPendingLifetimeMinutes,
        ActiveBudgetMinutes ?? SessionQueueLimits.DefaultActiveBudgetMinutes);
    public bool IsDefault => PendingPerSession is null && ExecutionSlots is null && PendingLifetimeMinutes is null && ActiveBudgetMinutes is null;

    public SessionQueuePreferences With(SessionQueueOption option, string? value)
    {
        var parsed = value is null ? (int?)null : Parse(value);
        return option switch
        {
            SessionQueueOption.PendingPerSession => new(parsed, ExecutionSlots, PendingLifetimeMinutes, ActiveBudgetMinutes),
            SessionQueueOption.ExecutionSlots => new(PendingPerSession, parsed, PendingLifetimeMinutes, ActiveBudgetMinutes),
            SessionQueueOption.PendingLifetimeMinutes => new(PendingPerSession, ExecutionSlots, parsed, ActiveBudgetMinutes),
            SessionQueueOption.ActiveBudgetMinutes => new(PendingPerSession, ExecutionSlots, PendingLifetimeMinutes, parsed),
            _ => throw new ArgumentOutOfRangeException(nameof(option)),
        };
    }

    private static int Parse(string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || !string.Equals(value, parsed.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Choose a canonical positive integer within the fixed queue limits.");
        }
        return parsed;
    }
}
