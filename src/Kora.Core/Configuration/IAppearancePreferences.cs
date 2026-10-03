namespace Kora.Core.Configuration;

public interface IAppearancePreferences
{
    ApplicationThemeMode? LoadThemeMode();

    int? LoadPresenceTimeoutSeconds();

    int? LoadConstellationSizePixels();

    int? LoadConstellationDotSizePercent();

    int? LoadConstellationMovementSpeedPercent();

    ConstellationPosition? LoadConstellationPosition();

    ResponseWindowSettings? LoadResponseWindowSettings();

    void SaveThemeMode(ApplicationThemeMode mode);

    void SavePresenceTimeoutSeconds(int seconds);

    void SaveConstellationSizePixels(int value);

    void SaveConstellationDotSizePercent(int value);

    void SaveConstellationMovementSpeedPercent(int value);

    void SaveConstellationPosition(ConstellationPosition position);

    void SaveResponseWindowSettings(ResponseWindowSettings settings);
}
