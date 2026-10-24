using System.Diagnostics;
using System.Diagnostics.Metrics;
using Kora.Core.Auditing;
using Kora.Core.Configuration;

namespace Kora.Core.Diagnostics;

/// <summary>Content-free phase timestamps, not host identity, permission or durable evidence.</summary>
public static class PresenceSwitchTiming
{
    private static readonly Meter Meter = new("Kora.PresenceTiming", "1.0.0");
    private static readonly Histogram<long> Timestamps =
        Meter.CreateHistogram<long>("kora.presence.renderer.phase.timestamp", "ticks");

    public static Instrument Instrument => Timestamps;

    public static bool IsRendererAudit(SecurityAuditEvent audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        return Timestamps.Enabled && string.Equals(audit.ActionId,
            AppearanceOptionRegistry.Get(AppearanceOption.NativePresenceRenderer).AuditAction, StringComparison.Ordinal);
    }

    public static void Mark(PresenceSwitchPhase phase)
    {
        if (!Enum.IsDefined(phase)) { throw new ArgumentOutOfRangeException(nameof(phase)); }
        if (Timestamps.Enabled)
        {
            Timestamps.Record(Stopwatch.GetTimestamp(), new KeyValuePair<string, object?>("phase", phase));
        }
    }
}
