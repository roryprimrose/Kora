using Kora.Application.Diagnostics;
using Kora.Core.Dependencies;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed class LocalAudioDevicePreferences(
    IApplicationDataPaths paths,
    ILogger<LocalAudioDevicePreferences> logger) : IAudioDevicePreferences
{
    private readonly string preferenceDirectory = Path.Combine(paths.LocalRoot, "Preferences");

    public string? LoadMicrophoneId() =>
        LoadIdentifier("microphone-id.txt", "microphone");

    public string? LoadOutputDeviceId() =>
        LoadIdentifier("output-device-id.txt", "audio output");

    public void SaveMicrophoneId(string microphoneId) =>
        SaveIdentifier(
            microphoneId,
            "microphone-id.txt",
            "microphone-id.tmp",
            "microphone");

    public void SaveOutputDeviceId(string outputDeviceId) =>
        SaveIdentifier(
            outputDeviceId,
            "output-device-id.txt",
            "output-device-id.tmp",
            "audio output");

    public void ClearMicrophoneId() =>
        ClearIdentifier("microphone-id.txt", "microphone");

    public void ClearOutputDeviceId() =>
        ClearIdentifier("output-device-id.txt", "audio output");

    private string? LoadIdentifier(string fileName, string deviceType)
    {
        var path = Path.Combine(preferenceDirectory, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var identifier = File.ReadAllText(path).Trim();
        var result = string.IsNullOrWhiteSpace(identifier)
            ? throw new InvalidDataException($"The saved {deviceType} preference is empty.")
            : identifier;
        ApplicationLog.AudioDevicePreferenceLoaded(logger, deviceType);
        return result;
    }

    private void SaveIdentifier(
        string identifier,
        string fileName,
        string temporaryFileName,
        string deviceType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        Directory.CreateDirectory(preferenceDirectory);
        var temporaryPath = Path.Combine(preferenceDirectory, temporaryFileName);
        File.WriteAllText(temporaryPath, identifier);
        File.Move(
            temporaryPath,
            Path.Combine(preferenceDirectory, fileName),
            overwrite: true);
        ApplicationLog.AudioDevicePreferenceSaved(logger, deviceType);
    }

    private void ClearIdentifier(string fileName, string deviceType)
    {
        var path = Path.Combine(preferenceDirectory, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        ApplicationLog.AudioDevicePreferenceCleared(logger, deviceType);
    }
}
