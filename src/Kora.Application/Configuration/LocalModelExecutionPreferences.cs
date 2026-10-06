using System.Text.Json;

using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed class LocalModelExecutionPreferences : IModelExecutionPreferences
{
    private const int Version = 1;
    private const string FileName = "model-execution-settings.json";
    private readonly IPreferenceStore store;

    public LocalModelExecutionPreferences(IApplicationDataPaths paths)
        : this(new LocalPreferenceStore(paths))
    {
    }

    internal LocalModelExecutionPreferences(IPreferenceStore store)
    {
        this.store = store;
    }

    public ModelExecutionSettings Load()
    {
        var contents = store.ReadText(FileName);
        if (contents is null)
        {
            return ModelExecutionSettings.Default;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(contents);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The saved model execution settings are invalid.", exception);
        }

        using var ownedDocument = document;
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("Version", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var savedVersion)
            || savedVersion != Version
            || !root.TryGetProperty("LocalModelsEnabled", out var localModelsEnabled)
            || localModelsEnabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !root.TryGetProperty("HostedModelsEnabled", out var hostedModelsEnabled)
            || hostedModelsEnabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException("The saved model execution settings are invalid.");
        }

        return new ModelExecutionSettings(
            localModelsEnabled.GetBoolean(),
            hostedModelsEnabled.GetBoolean());
    }

    public void Save(ModelExecutionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        store.WriteText(
            FileName,
            JsonSerializer.Serialize(new
            {
                Version,
                settings.LocalModelsEnabled,
                settings.HostedModelsEnabled,
            }));
    }
}
