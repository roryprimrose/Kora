using System.Globalization;

namespace Kora.Core.Calendar;

public static class CalendarDateArithmetic
{
    public static int MaximumDays { get; } = DateOnly.MaxValue.DayNumber - DateOnly.MinValue.DayNumber;

    public static DateOnly ParseDate(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length != 10 || text.Any(character => character is not (>= '0' and <= '9') and not '-')
            || !DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new InvalidDataException("Use a valid canonical yyyy-MM-dd calendar date from 0001-01-01 through 9999-12-31.");
        }
        return date;
    }

    public static int ParseDays(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length is < 1 or > 8
            || !int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var days)
            || !text.Equals(days.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            || days < -MaximumDays || days > MaximumDays)
        {
            throw new InvalidDataException("Use canonical ASCII whole days from -3652058 through 3652058; zero is 0, with no plus sign or leading zeroes.");
        }
        return days;
    }

    public static int Difference(DateOnly start, DateOnly end) => end.DayNumber - start.DayNumber;

    public static DateOnly Add(DateOnly start, int days)
    {
        if (days < -MaximumDays || days > MaximumDays
            || days < DateOnly.MinValue.DayNumber - start.DayNumber
            || days > DateOnly.MaxValue.DayNumber - start.DayNumber)
        {
            throw new InvalidDataException("Date addition is outside 0001-01-01 through 9999-12-31. No date was clamped or substituted.");
        }
        return start.AddDays(days);
    }

    public static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
