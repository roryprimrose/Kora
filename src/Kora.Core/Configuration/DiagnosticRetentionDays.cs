using System.Globalization;

namespace Kora.Core.Configuration;

public readonly record struct DiagnosticRetentionDays
{
    public const int Minimum = 1;
    public const int Maximum = 365;
    public const int DefaultDays = 30;
    public static DiagnosticRetentionDays Default => new(DefaultDays);

    public DiagnosticRetentionDays(int days)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, Minimum);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(days, Maximum);
        Days = days;
    }

    public int Days { get; }

    public static DiagnosticRetentionDays Parse(string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var days)
            || !string.Equals(value, days.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Use one exact integer from 1 through 365 days.");
        }
        return new(days);
    }
}
