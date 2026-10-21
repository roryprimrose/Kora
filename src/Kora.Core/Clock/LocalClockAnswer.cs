using System.Globalization;

namespace Kora.Core.Clock;

public sealed class LocalClockAnswer
{
    public LocalClockAnswer(DateTimeOffset instant, LocalClockContext context, LocalClockOperation operation,
        LocalClockZone zone = LocalClockZone.HostLocal)
    {
        if (!Enum.IsDefined(zone)) { throw new ArgumentOutOfRangeException(nameof(zone)); }
        Instant = instant;
        var timeZone = zone == LocalClockZone.Utc ? TimeZoneInfo.Utc : context.TimeZone;
        var label = zone == LocalClockZone.Utc ? "UTC" : "Local";
        LocalTime = TimeZoneInfo.ConvertTime(instant, timeZone);
        TimeZoneId = timeZone.Id;
        var value = operation switch
        {
            LocalClockOperation.Time => label + " time: " + LocalTime.ToString("T", context.Culture),
            LocalClockOperation.Date => label + " date: " + LocalTime.ToString("D", context.Culture),
            LocalClockOperation.DateAndTime => label + " date and time: " + LocalTime.ToString("D", context.Culture)
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
