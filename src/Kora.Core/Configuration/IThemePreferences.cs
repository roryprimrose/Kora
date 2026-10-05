namespace Kora.Core.Configuration;

public interface IThemePreferences
{
    ApplicationThemeMode? LoadThemeMode();

    void SaveThemeMode(ApplicationThemeMode mode);
}