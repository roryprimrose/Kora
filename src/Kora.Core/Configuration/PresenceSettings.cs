namespace Kora.Core.Configuration;

public static class PresenceSettings
{
    public const int DefaultTimeoutSeconds = 5;
    public const int MinimumTimeoutSeconds = 1;
    public const int MaximumTimeoutSeconds = 60;

    public static void ValidateTimeoutSeconds(int seconds)
    {
        if (seconds is < MinimumTimeoutSeconds or > MaximumTimeoutSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seconds),
                seconds,
                $"The presence timeout must be between {MinimumTimeoutSeconds} and {MaximumTimeoutSeconds} seconds.");
        }
    }
}
