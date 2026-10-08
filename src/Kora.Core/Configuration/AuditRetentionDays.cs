using System.Globalization;

namespace Kora.Core.Configuration;

public readonly record struct AuditRetentionDays
{
    public const int Minimum = 30;
    public const int Maximum = 365;
    public const int DefaultDays = 90;
    public static AuditRetentionDays Default => new(DefaultDays);

    public AuditRetentionDays(int days)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, Minimum);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(days, Maximum);
        Days = days;
    }

    public int Days { get; }

    public static AuditRetentionDays Parse(string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var days)
            || !string.Equals(value, days.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Use one exact integer from 30 through 365 days.");
        }
        return new(days);
    }

    public static bool IsValidDeadline(DateTimeOffset committedUtc, DateTimeOffset dueUtc)
    {
        var days = (dueUtc - committedUtc).TotalDays;
        return days is >= Minimum and <= Maximum && days == Math.Truncate(days);
    }
}
