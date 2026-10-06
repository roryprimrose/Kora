namespace Kora.Core.Configuration;

public interface IModelExecutionPreferences
{
    ModelExecutionSettings Load();

    void Save(ModelExecutionSettings settings);
}
