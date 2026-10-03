namespace Kora.Core.Configuration;

public interface IAssistantNamePreferences
{
    string? LoadName();

    void SaveName(string name);
}
