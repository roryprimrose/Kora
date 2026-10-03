namespace Kora.Core.Configuration;

public static class ConstellationSettings
{
    public const int DefaultSizePixels = 360;
    public const int MinimumSizePixels = 240;
    public const int MaximumSizePixels = 600;
    public const int DefaultDotSizePercent = 100;
    public const int MinimumDotSizePercent = 50;
    public const int MaximumDotSizePercent = 200;
    public const int DefaultMovementSpeedPercent = 100;
    public const int MinimumMovementSpeedPercent = 25;
    public const int MaximumMovementSpeedPercent = 200;

    public static void ValidateSizePixels(int value) =>
        ValidateRange(
            value,
            MinimumSizePixels,
            MaximumSizePixels,
            nameof(value),
            "constellation size");

    public static void ValidateDotSizePercent(int value) =>
        ValidateRange(
            value,
            MinimumDotSizePercent,
            MaximumDotSizePercent,
            nameof(value),
            "constellation dot size");

    public static void ValidateMovementSpeedPercent(int value) =>
        ValidateRange(
            value,
            MinimumMovementSpeedPercent,
            MaximumMovementSpeedPercent,
            nameof(value),
            "constellation movement speed");

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
