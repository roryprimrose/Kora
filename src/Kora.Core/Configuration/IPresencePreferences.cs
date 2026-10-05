namespace Kora.Core.Configuration;

public interface IPresencePreferences
{
    int? LoadPresenceTimeoutSeconds();

    int? LoadPresenceSizePixels();

    int? LoadPresenceDotSizePercent();

    int? LoadPresenceMovementSpeedPercent();

    PresencePosition? LoadPresencePosition();

    void SavePresenceTimeoutSeconds(int seconds);

    void SavePresenceSizePixels(int value);

    void SavePresenceDotSizePercent(int value);

    void SavePresenceMovementSpeedPercent(int value);

    void SavePresencePosition(PresencePosition position);
}