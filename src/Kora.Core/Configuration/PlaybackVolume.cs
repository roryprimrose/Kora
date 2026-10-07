using System.Globalization;

namespace Kora.Core.Configuration;

public readonly record struct PlaybackVolume
{
    public const int MaximumPercent = 100;
    public static PlaybackVolume Default => new(MaximumPercent);

    public PlaybackVolume(int percent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(percent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, MaximumPercent);
        Percent = percent;
    }

    public int Percent { get; }
    public bool AllowsSpeech => Percent > 0;

    public static PlaybackVolume Parse(string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var percent)
            || !string.Equals(value, percent.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Use one exact integer percent from 0 through 100.");
        }
        return new(percent);
    }
}
