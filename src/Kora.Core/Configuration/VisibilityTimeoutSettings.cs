namespace Kora.Core.Configuration;

public static class VisibilityTimeoutSettings
{
    public const int MinimumSeconds = 1;
    public const int MaximumSeconds = 60;

    public static void ValidateSeconds(int seconds)
    {
        if (seconds is < MinimumSeconds or > MaximumSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seconds),
                seconds,
                $"The visibility timeout must be between {MinimumSeconds} and {MaximumSeconds} seconds.");
        }
    }
}
