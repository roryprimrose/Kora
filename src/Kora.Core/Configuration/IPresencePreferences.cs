namespace Kora.Core.Configuration;

public interface IPresencePreferences
{
    bool? LoadPresenceDisplayEnabled();

    int? LoadPresenceTimeoutSeconds();

    int? LoadPresenceSizePixels();

    int? LoadPresenceDotSizePercent();

    int? LoadPresenceDotDensityPercent();

    int? LoadPresenceMovementSpeedPercent();

    bool? LoadPresenceSpeechScalingEnabled();

    int? LoadPresenceSpeechScaleAmountPercent();

    PresencePosition? LoadPresencePosition();

    void SavePresenceDisplayEnabled(bool value);

    void SavePresenceTimeoutSeconds(int seconds);

    void SavePresenceSizePixels(int value);

    void SavePresenceDotSizePercent(int value);

    void SavePresenceDotDensityPercent(int value);

    void SavePresenceMovementSpeedPercent(int value);

    void SavePresenceSpeechScalingEnabled(bool value);

    void SavePresenceSpeechScaleAmountPercent(int value);

    void SavePresencePosition(PresencePosition position);
}