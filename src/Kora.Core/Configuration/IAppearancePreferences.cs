namespace Kora.Core.Configuration;

public interface IAppearancePreferences :
    IThemePreferences,
    IPresencePreferences,
    IResponseWindowPreferences,
    IReaderTextScalePreferences
{
    /// <summary>Removes the saved override for one registered appearance preference.</summary>
    void ResetRegisteredOption(AppearanceOption setting);
}