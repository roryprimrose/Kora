using System.Globalization;

namespace Kora.Core.Clock;

public sealed class LocalClockAnswer
{
    public LocalClockAnswer(DateTimeOffset instant, LocalClockContext context, LocalClockOperation operation)
    {
        Instant = instant;
        LocalTime = TimeZoneInfo.ConvertTime(instant, context.TimeZone);
        TimeZoneId = context.TimeZone.Id;
        var value = operation switch
        {
            LocalClockOperation.Time => "Local time: " + LocalTime.ToString("T", context.Culture),
            LocalClockOperation.Date => "Local date: " + LocalTime.ToString("D", context.Culture),
            LocalClockOperation.DateAndTime => "Local date and time: " + LocalTime.ToString("D", context.Culture)
                + ", " + LocalTime.ToString("T", context.Culture),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        Text = value + " (UTC" + LocalTime.ToString("zzz", CultureInfo.InvariantCulture) + "; " + TimeZoneId + ").";
    }

    public DateTimeOffset Instant { get; }
    public DateTimeOffset LocalTime { get; }
    public string TimeZoneId { get; }
    public string Text { get; }
}
