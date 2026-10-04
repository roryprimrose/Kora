using System.Text.Json;
using System.Text.Json.Serialization;

using Kora.Core.Commands;
using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed class LocalModelApprovalPreferences(IApplicationDataPaths paths)
    : IModelApprovalPreferences
{
    private const int Version = 1;
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter<BuiltInAction>() },
    };
    private readonly string directory = Path.Combine(paths.LocalRoot, "Preferences");
    private readonly string path = Path.Combine(paths.LocalRoot, "Preferences", "model-approvals.json");

    public ModelApprovalPreferences Load()
    {
        if (!File.Exists(path))
        {
            return new ModelApprovalPreferences(true, []);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The saved model approval preferences are invalid.", exception);
        }

        using var ownedDocument = document;
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("Version", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var savedVersion)
            || savedVersion != Version
            || !root.TryGetProperty("RequireAssistantNameForVoiceApproval", out var requireName)
            || requireName.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !root.TryGetProperty("AlwaysAllowedActions", out var actions)
            || actions.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The saved model approval preferences are invalid.");
        }

        var grants = new List<BuiltInAction>();
        foreach (var item in actions.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String
                || !Enum.TryParse<BuiltInAction>(item.GetString(), out var action)
                || !Enum.IsDefined(action)
                || !string.Equals(item.GetString(), action.ToString(), StringComparison.Ordinal)
                || grants.Contains(action))
            {
                throw new InvalidDataException("The saved model approval grants are invalid.");
            }

            grants.Add(action);
        }

        return new ModelApprovalPreferences(requireName.GetBoolean(), grants);
    }

    public void Save(ModelApprovalPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (preferences.AlwaysAllowedActions is null)
        {
            throw new ArgumentException("The model approval grants are required.", nameof(preferences));
        }
        if (preferences.AlwaysAllowedActions.Any(action => !Enum.IsDefined(action))
            || preferences.AlwaysAllowedActions.Distinct().Count() != preferences.AlwaysAllowedActions.Count)
        {
            throw new ArgumentException("Model approval grants must be unique registered actions.", nameof(preferences));
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, "model-approvals.tmp");
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(new
            {
                Version,
                preferences.RequireAssistantNameForVoiceApproval,
                preferences.AlwaysAllowedActions,
            }, Options));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
