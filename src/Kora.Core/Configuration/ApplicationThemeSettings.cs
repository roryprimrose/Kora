namespace Kora.Core.Configuration;

public static class ApplicationThemeSettings
{
    public const ApplicationThemeMode DefaultMode = ApplicationThemeMode.System;

    public static void Validate(ApplicationThemeMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "The appearance theme is invalid.");
        }
    }
}
