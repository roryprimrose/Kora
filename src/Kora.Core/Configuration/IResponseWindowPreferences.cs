namespace Kora.Core.Configuration;

public interface IResponseWindowPreferences
{
    int? LoadResponseTimeoutSeconds();

    void SaveResponseTimeoutSeconds(int seconds);

    ResponseWindowSettings? LoadResponseWindowSettings();

    void SaveResponseWindowSettings(ResponseWindowSettings settings);
}