namespace Kora.Core.Configuration;

public static class PresenceSettings
{
    public const bool DefaultDisplayEnabled = true;
    public const int DefaultTimeoutSeconds = 10;
    public const int MinimumTimeoutSeconds = VisibilityTimeoutSettings.MinimumSeconds;
    public const int MaximumTimeoutSeconds = VisibilityTimeoutSettings.MaximumSeconds;
    public const int DefaultSizePixels = 360;
    public const int MinimumSizePixels = 240;
    public const int MaximumSizePixels = 600;
    public const int DefaultDotSizePercent = 100;
    public const int MinimumDotSizePercent = 50;
    public const int MaximumDotSizePercent = 200;
    public const int DefaultDotDensityPercent = 100;
    public const int MinimumDotDensityPercent = 25;
    public const int MaximumDotDensityPercent = 200;
    public const int BaseParticleCount = 150;
    public const int DefaultMovementSpeedPercent = 100;
    public const int MinimumMovementSpeedPercent = 25;
    public const int MaximumMovementSpeedPercent = 200;
    public const bool DefaultSpeechScalingEnabled = true;
    public const int DefaultSpeechScaleAmountPercent = 100;
    public const int MinimumSpeechScaleAmountPercent = 0;
    public const int MaximumSpeechScaleAmountPercent = 200;

    public static void ValidateTimeoutSeconds(int seconds) =>
        VisibilityTimeoutSettings.ValidateSeconds(seconds);

    public static void ValidateSizePixels(int value) =>
        ValidateRange(
            value,
            MinimumSizePixels,
            MaximumSizePixels,
            nameof(value),
            "presence size");

    public static void ValidateDotSizePercent(int value) =>
        ValidateRange(
            value,
            MinimumDotSizePercent,
            MaximumDotSizePercent,
            nameof(value),
            "presence dot size");

    public static void ValidateDotDensityPercent(int value) =>
        ValidateRange(
            value,
            MinimumDotDensityPercent,
            MaximumDotDensityPercent,
            nameof(value),
            "presence dot density");

    public static int GetParticleCount(int densityPercent)
    {
        ValidateDotDensityPercent(densityPercent);
        return (int)Math.Round(
            BaseParticleCount * densityPercent / 100d,
            MidpointRounding.AwayFromZero);
    }

    public static void ValidateMovementSpeedPercent(int value) =>
        ValidateRange(
            value,
            MinimumMovementSpeedPercent,
            MaximumMovementSpeedPercent,
            nameof(value),
            "presence movement speed");

    public static void ValidateSpeechScaleAmountPercent(int value) =>
        ValidateRange(
            value,
            MinimumSpeechScaleAmountPercent,
            MaximumSpeechScaleAmountPercent,
            nameof(value),
            "presence speech scale amount");

    private static void ValidateRange(
        int value,
        int minimum,
        int maximum,
        string parameterName,
        string settingName)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"The {settingName} must be between {minimum} and {maximum}.");
        }
    }
}
