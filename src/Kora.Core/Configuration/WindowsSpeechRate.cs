using System.Globalization;

namespace Kora.Core.Configuration;

public readonly record struct WindowsSpeechRate
{
    public const int Minimum = -10;
    public const int Maximum = 10;
    public static WindowsSpeechRate Default => new(0);

    public WindowsSpeechRate(int value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, Minimum);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, Maximum);
        Value = value;
    }

    public int Value { get; }

    public static WindowsSpeechRate Parse(string value)
    {
        if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var rate)
            || !string.Equals(value, rate.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Use one canonical Windows-native integer rate from -10 through 10.");
        }
        return new(rate);
    }
}
