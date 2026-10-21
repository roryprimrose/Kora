using System.Globalization;

namespace Kora.Core.Clock;

public sealed class LocalClockContext(TimeZoneInfo timeZone, CultureInfo culture)
{
    public TimeZoneInfo TimeZone { get; } = timeZone;
    public CultureInfo Culture { get; } = CultureInfo.ReadOnly((CultureInfo)culture.Clone());
}
