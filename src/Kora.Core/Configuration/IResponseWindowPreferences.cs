namespace Kora.Core.Configuration;

public interface IResponseWindowPreferences
{
    ResponseWindowSettings? LoadResponseWindowSettings();

    void SaveResponseWindowSettings(ResponseWindowSettings settings);
}