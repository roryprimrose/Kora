using System.Globalization;
using Kora.Core.Hosting;

namespace Kora.Core.Configuration;

/// <summary>Overrides for the existing synchronous read-only local-version queue, not general execution.</summary>
public sealed record SessionQueuePreferences
{
    public SessionQueuePreferences(int? pendingPerSession = null, int? executionSlots = null, int? pendingLifetimeMinutes = null)
    {
        _ = new SessionQueueLimits(pendingPerSession ?? SessionQueueLimits.MaximumPendingPerSession,
            executionSlots ?? SessionQueueLimits.DefaultExecutionSlots,
            pendingLifetimeMinutes ?? SessionQueueLimits.DefaultPendingLifetimeMinutes);
        PendingPerSession = pendingPerSession;
        ExecutionSlots = executionSlots;
        PendingLifetimeMinutes = pendingLifetimeMinutes;
    }

    public int? PendingPerSession { get; }
    public int? ExecutionSlots { get; }
    public int? PendingLifetimeMinutes { get; }
    public SessionQueueLimits Limits => new(PendingPerSession ?? SessionQueueLimits.MaximumPendingPerSession,
        ExecutionSlots ?? SessionQueueLimits.DefaultExecutionSlots,
        PendingLifetimeMinutes ?? SessionQueueLimits.DefaultPendingLifetimeMinutes);
    public bool IsDefault => PendingPerSession is null && ExecutionSlots is null && PendingLifetimeMinutes is null;

    public SessionQueuePreferences With(SessionQueueOption option, string? value)
    {
        var parsed = value is null ? (int?)null : Parse(value);
        return option switch
        {
            SessionQueueOption.PendingPerSession => new(parsed, ExecutionSlots, PendingLifetimeMinutes),
            SessionQueueOption.ExecutionSlots => new(PendingPerSession, parsed, PendingLifetimeMinutes),
            SessionQueueOption.PendingLifetimeMinutes => new(PendingPerSession, ExecutionSlots, parsed),
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
