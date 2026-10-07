using Kora.Core.Dependencies;
using Kora.Core.Voice;
using Kora.Application.Diagnostics;
using Kora.Core.Configuration;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class LocalTextToSpeechPreferences : ITextToSpeechPreferences
{
    private const string ProviderFileName = "speech-provider.txt";
    private const string VoiceFileName = "speech-voice.txt";
    private const string SelectionFileName = "speech-selection.txt";
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

    public SpeechSelection? LoadSelection()
    {
        var contents = store.ReadLines(SelectionFileName);
        if (contents is null)
        {
            var provider = LoadProviderId();
            var voice = LoadVoiceId();
            if (provider is null && voice is null) { return null; }
            return ValidateSaved(new(provider ?? SpeechProviderIds.Windows, voice));
        }
        if (contents.Length != 3 || !string.Equals(contents[0], "1", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The saved speech selection format is unknown or malformed.");
        }
        return ValidateSaved(new(contents[1], contents[2].Length == 0 ? null : contents[2]));
    }

    public void SaveSelection(SpeechSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        selection.Validate();
        store.WriteLines(SelectionFileName, ["1", selection.ProviderId, selection.VoiceId ?? string.Empty]);
        SelectionSaved(logger);
    }

    private static SpeechSelection ValidateSaved(SpeechSelection selection)
    {
        try { selection.Validate(); }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("The saved speech selection is invalid.", exception);
        }
        return selection;
    }

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
        IdentifierLoaded(logger, preferenceName);
        return result;
    }

    private void SaveIdentifier(
        string fileName,
        string identifier,
        string preferenceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        store.WriteText(fileName, identifier);
        IdentifierSaved(logger, preferenceName);
    }
}