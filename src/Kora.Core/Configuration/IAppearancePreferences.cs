namespace Kora.Core.Configuration;

public interface IAppearancePreferences
{
    ApplicationThemeMode? LoadThemeMode();

    int? LoadPresenceTimeoutSeconds();

    int? LoadPresenceSizePixels();

    int? LoadPresenceDotSizePercent();

    int? LoadPresenceMovementSpeedPercent();

    PresencePosition? LoadPresencePosition();

    ResponseWindowSettings? LoadResponseWindowSettings();

    void SaveThemeMode(ApplicationThemeMode mode);

    void SavePresenceTimeoutSeconds(int seconds);

    void SavePresenceSizePixels(int value);

    void SavePresenceDotSizePercent(int value);

    void SavePresenceMovementSpeedPercent(int value);

    void SavePresencePosition(PresencePosition position);

    void SaveResponseWindowSettings(ResponseWindowSettings settings);
}
