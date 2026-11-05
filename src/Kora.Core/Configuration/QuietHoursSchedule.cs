using System.Globalization;

namespace Kora.Core.Configuration;

public sealed record QuietHoursSchedule
{
    private static readonly string[] DayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
    private readonly TimeZoneInfo? zone;
    private readonly int dayMask;

    private QuietHoursSchedule() { }

    private QuietHoursSchedule(string days, TimeOnly start, TimeOnly end, TimeZoneInfo zone, int dayMask)
    {
        Days = days;
        Start = start;
        End = end;
        ZoneId = zone.Id;
        this.zone = zone;
        this.dayMask = dayMask;
    }

    public static QuietHoursSchedule Off { get; } = new();
    public bool IsOff => zone is null;
    public string Days { get; } = "";
    public TimeOnly Start { get; }
    public TimeOnly End { get; }
    public string ZoneId { get; } = "";

    public static QuietHoursSchedule Weekly(string days, string start, string end, string zoneId)
    {
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(zoneId);
        var selected = days.Split(',');
        var canonical = DayNames.Where(day => selected.Contains(day, StringComparer.Ordinal)).ToArray();
        if (canonical.Length == 0 || !string.Equals(days, string.Join(',', canonical), StringComparison.Ordinal))
        { throw new ArgumentException("Choose distinct days in Mon,Tue,Wed,Thu,Fri,Sat,Sun order.", nameof(days)); }
        var from = ParseTime(start);
        var until = ParseTime(end);
        if (from == until) { throw new ArgumentException("Start and end must differ; an equal pair is not an all-day interval.", nameof(end)); }
        if (zoneId.Length is 0 or > 128 || zoneId.Any(char.IsControl) || !string.Equals(zoneId, zoneId.Trim(), StringComparison.Ordinal))
        { throw new ArgumentException("Choose one explicit installed time-zone ID.", nameof(zoneId)); }
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var selectedZone))
        { throw new ArgumentException("The selected time zone is unavailable. Choose an installed zone explicitly.", nameof(zoneId)); }
        var mask = canonical.Aggregate(0, (current, day) => current | (1 << ((Array.IndexOf(DayNames, day) + 1) % 7)));
        return new(days, from, until, selectedZone, mask);
    }

    private static TimeOnly ParseTime(string value)
    {
        if (!TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        { throw new ArgumentException("Use an explicit 24-hour HH:mm time.", nameof(value)); }
        return time;
    }

    public bool IsQuiet(DateTimeOffset utc)
    {
        if (zone is null) { return false; }
        var local = TimeZoneInfo.ConvertTime(utc, zone);
        var clock = TimeOnly.FromDateTime(local.DateTime);
        var day = (int)local.DayOfWeek;
        bool Includes(int candidate) => (dayMask & (1 << candidate)) != 0;
        return Start < End
            ? Includes(day) && clock >= Start && clock < End
            : Includes(day) && clock >= Start || Includes((day + 6) % 7) && clock < End;
    }

    public override string ToString() => IsOff ? "Off"
        : $"weekly {Days} {Start.ToString("HH:mm", CultureInfo.InvariantCulture)} {End.ToString("HH:mm", CultureInfo.InvariantCulture)} {ZoneId}";

    public static QuietHoursSchedule Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (string.Equals(value, "Off", StringComparison.Ordinal)) { return Off; }
        var parts = value.Split(' ', 5, StringSplitOptions.None);
        if (parts.Length != 5 || !string.Equals(parts[0], "weekly", StringComparison.Ordinal))
        { throw new ArgumentException("Use Off or weekly <days> <HH:mm start> <HH:mm end> <zone ID>.", nameof(value)); }
        return Weekly(parts[1], parts[2], parts[3], parts[4]);
    }
}
