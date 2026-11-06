using System.Globalization;

namespace Kora.Core.Configuration;

/// <summary>A future ordinary task preference, never an extension of provider or context authority.</summary>
public readonly record struct OrdinaryTurnDeadline
{
    public const int DefaultMinutes = 5;
    public const int MinimumMinutes = 1;
    public const int MaximumMinutes = 60;
    public static OrdinaryTurnDeadline Default => new(DefaultMinutes);

    public OrdinaryTurnDeadline(int minutes)
    {
        if (minutes is < MinimumMinutes or > MaximumMinutes)
        { throw new ArgumentOutOfRangeException(nameof(minutes), "Choose an integer deadline from 1 through 60 minutes."); }
        Minutes = minutes;
    }

    public int Minutes { get; }
    public TimeSpan Duration => TimeSpan.FromMinutes(new OrdinaryTurnDeadline(Minutes).Minutes);

    public static OrdinaryTurnDeadline Parse(string text)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || !string.Equals(text, minutes.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        { throw new ArgumentOutOfRangeException(nameof(text), "The deadline must be a canonical integer from 1 through 60."); }
        return new(minutes);
    }
}
