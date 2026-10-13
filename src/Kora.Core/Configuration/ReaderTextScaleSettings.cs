namespace Kora.Core.Configuration;

public static class ReaderTextScaleSettings
{
    public const int DefaultPercent = 100;
    public const int MinimumPercent = 75;
    public const int MaximumPercent = 200;

    public static void ValidatePercent(int percent)
    {
        if (percent is < MinimumPercent or > MaximumPercent)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), percent,
                "Native reader text scale must be an integer from 75 through 200 percent.");
        }
    }
}
