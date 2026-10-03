using Kora.Core.Dependencies;
using Kora.Core.Voice;
using Kora.Application.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed class LocalTextToSpeechPreferences(
    IApplicationDataPaths paths,
    ILogger<LocalTextToSpeechPreferences> logger) : ITextToSpeechPreferences
{
    private readonly string preferenceDirectory = Path.Combine(paths.LocalRoot, "Preferences");
    private readonly string providerPreferencePath =
        Path.Combine(paths.LocalRoot, "Preferences", "speech-provider.txt");
    private readonly string preferencePath = Path.Combine(paths.LocalRoot, "Preferences", "speech-voice.txt");

    public string? LoadProviderId() =>
        LoadIdentifier(providerPreferencePath, "speech provider");

    public void SaveProviderId(string providerId) =>
        SaveIdentifier(
            providerPreferencePath,
            "speech-provider.tmp",
            providerId,
            "speech provider");

    public string? LoadVoiceId()
        => LoadIdentifier(preferencePath, "speech voice");

    public void SaveVoiceId(string voiceId) =>
        SaveIdentifier(
            preferencePath,
            "speech-voice.tmp",
            voiceId,
            "speech voice");

    private string? LoadIdentifier(string path, string preferenceName)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var identifier = File.ReadAllText(path).Trim();
        var result = string.IsNullOrWhiteSpace(identifier)
            ? throw new InvalidDataException($"The saved {preferenceName} preference is empty.")
            : identifier;
        ApplicationLog.Debug(logger, $"Loaded the saved {preferenceName} preference");
        return result;
    }

    private void SaveIdentifier(
        string path,
        string temporaryFileName,
        string identifier,
        string preferenceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        Directory.CreateDirectory(preferenceDirectory);
        var temporaryPath = Path.Combine(preferenceDirectory, temporaryFileName);
        File.WriteAllText(temporaryPath, identifier);
        File.Move(temporaryPath, path, overwrite: true);
        ApplicationLog.Information(logger, $"Saved the {preferenceName} preference");
    }
}