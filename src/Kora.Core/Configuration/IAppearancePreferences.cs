namespace Kora.Core.Configuration;

public interface IAppearancePreferences :
    IThemePreferences,
    IPresencePreferences,
    IResponseWindowPreferences,
    IReaderTextScalePreferences
{
    /// <summary>Loads the explicit reduced-motion preference, or null when the system default is in use.</summary>
    bool? LoadReducedMotionEnabled();

    /// <summary>Saves whether motion is explicitly reduced; false still follows system accessibility.</summary>
    void SaveReducedMotionEnabled(bool enabled);

    /// <summary>Removes the saved override for one registered appearance preference.</summary>
    void ResetRegisteredOption(AppearanceOption setting);
}