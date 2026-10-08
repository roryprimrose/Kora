using System.Diagnostics;

namespace Kora.Core.Platform;

public sealed record PrivacyObservation
{
    public PrivacyObservation(Guid id, long observedTimestamp, long timestampFrequency)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A privacy observation identity is required.", nameof(id));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timestampFrequency);
        Id = id;
        ObservedTimestamp = observedTimestamp;
        TimestampFrequency = timestampFrequency;
    }

    public Guid Id { get; }
    public long ObservedTimestamp { get; }
    public long TimestampFrequency { get; }

    // WTS/power/MMDevice callbacks do not supply the underlying OS event time.
    public double? OsEventToNotificationDelayMilliseconds => null;

    public static PrivacyObservation Create(long? observedTimestamp = null)
    {
        var timestamp = observedTimestamp ?? Stopwatch.GetTimestamp();
        return new PrivacyObservation(Guid.NewGuid(), timestamp, Stopwatch.Frequency);
    }
}
