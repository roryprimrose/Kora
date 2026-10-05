using Kora.Application.Diagnostics;
using Kora.Core.Dependencies;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed class LocalAudioDevicePreferences : IAudioDevicePreferences
{
    private readonly IPreferenceStore store;
    private readonly ILogger<LocalAudioDevicePreferences> logger;

    public LocalAudioDevicePreferences(
        IApplicationDataPaths paths,
        ILogger<LocalAudioDevicePreferences> logger)
        : this(new LocalPreferenceStore(paths), logger)
    {
    }

    internal LocalAudioDevicePreferences(
        IPreferenceStore store,
        ILogger<LocalAudioDevicePreferences> logger)
    {
        this.store = store;
        this.logger = logger;
    }

    public string? LoadMicrophoneId() =>
        LoadIdentifier("microphone-id.txt", "microphone");

    public string? LoadOutputDeviceId() =>
        LoadIdentifier("output-device-id.txt", "audio output");

    public void SaveMicrophoneId(string microphoneId) =>
        SaveIdentifier(microphoneId, "microphone-id.txt", "microphone");

    public void SaveOutputDeviceId(string outputDeviceId) =>
        SaveIdentifier(outputDeviceId, "output-device-id.txt", "audio output");

    public void ClearMicrophoneId() =>
        ClearIdentifier("microphone-id.txt", "microphone");

    public void ClearOutputDeviceId() =>
        ClearIdentifier("output-device-id.txt", "audio output");

    private string? LoadIdentifier(string fileName, string deviceType)
    {
        var contents = store.ReadText(fileName);
        if (contents is null)
        {
            return null;
        }

        var identifier = contents.Trim();
        var result = string.IsNullOrWhiteSpace(identifier)
            ? throw new InvalidDataException($"The saved {deviceType} preference is empty.")
            : identifier;
        ApplicationLog.AudioDevicePreferenceLoaded(logger, deviceType);
        return result;
    }

    private void SaveIdentifier(
        string identifier,
        string fileName,
        string deviceType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        store.WriteText(fileName, identifier);
        ApplicationLog.AudioDevicePreferenceSaved(logger, deviceType);
    }

    private void ClearIdentifier(string fileName, string deviceType)
    {
        store.Delete(fileName);
        ApplicationLog.AudioDevicePreferenceCleared(logger, deviceType);
    }
}
