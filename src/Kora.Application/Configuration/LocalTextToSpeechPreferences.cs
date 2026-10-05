using Kora.Core.Dependencies;
using Kora.Core.Voice;
using Kora.Application.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed class LocalTextToSpeechPreferences : ITextToSpeechPreferences
{
    private const string ProviderFileName = "speech-provider.txt";
    private const string VoiceFileName = "speech-voice.txt";
    private readonly IPreferenceStore store;
    private readonly ILogger<LocalTextToSpeechPreferences> logger;

    public LocalTextToSpeechPreferences(
        IApplicationDataPaths paths,
        ILogger<LocalTextToSpeechPreferences> logger)
        : this(new LocalPreferenceStore(paths), logger)
    {
    }

    internal LocalTextToSpeechPreferences(
        IPreferenceStore store,
        ILogger<LocalTextToSpeechPreferences> logger)
    {
        this.store = store;
        this.logger = logger;
    }

    public string? LoadProviderId() =>
        LoadIdentifier(ProviderFileName, "speech provider");

    public void SaveProviderId(string providerId) =>
        SaveIdentifier(ProviderFileName, providerId, "speech provider");

    public string? LoadVoiceId()
        => LoadIdentifier(VoiceFileName, "speech voice");

    public void SaveVoiceId(string voiceId) =>
        SaveIdentifier(VoiceFileName, voiceId, "speech voice");

    private string? LoadIdentifier(string fileName, string preferenceName)
    {
        var contents = store.ReadText(fileName);
        if (contents is null)
        {
            return null;
        }

        var identifier = contents.Trim();
        var result = string.IsNullOrWhiteSpace(identifier)
            ? throw new InvalidDataException($"The saved {preferenceName} preference is empty.")
            : identifier;
        ApplicationLog.Debug(logger, $"Loaded the saved {preferenceName} preference");
        return result;
    }

    private void SaveIdentifier(
        string fileName,
        string identifier,
        string preferenceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        store.WriteText(fileName, identifier);
        ApplicationLog.Information(logger, $"Saved the {preferenceName} preference");
    }
}