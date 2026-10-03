using Kora.Application.Diagnostics;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed class LocalAssistantNamePreferences(
    IApplicationDataPaths paths,
    ILogger<LocalAssistantNamePreferences> logger) : IAssistantNamePreferences
{
    private const string FileName = "assistant-name.txt";
    private const string TemporaryFileName = "assistant-name.tmp";

    private readonly string preferenceDirectory = Path.Combine(paths.LocalRoot, "Preferences");

    public string? LoadName()
    {
        var path = Path.Combine(preferenceDirectory, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var name = AssistantNameRules.Normalize(File.ReadAllText(path));
            ApplicationLog.AssistantNamePreferenceLoaded(logger);
            return name;
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The saved assistant name preference is invalid.", exception);
        }
    }

    public void SaveName(string name)
    {
        var normalizedName = AssistantNameRules.Normalize(name);
        Directory.CreateDirectory(preferenceDirectory);
        var temporaryPath = Path.Combine(preferenceDirectory, TemporaryFileName);
        File.WriteAllText(temporaryPath, normalizedName);
        File.Move(
            temporaryPath,
            Path.Combine(preferenceDirectory, FileName),
            overwrite: true);
        ApplicationLog.AssistantNamePreferenceSaved(logger);
    }
}
