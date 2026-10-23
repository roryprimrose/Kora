using Kora.Core.Dependencies;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed class LocalSpeechInputPreferences : ISpeechInputPreferences
{
    private const string FileName = "speech-input-profile.txt";
    private const string PendingFileName = "speech-input-profile-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalSpeechInputPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalSpeechInputPreferences(IPreferenceStore store) => this.store = store;

    public SpeechInputSelection? Load()
    {
        if (store.ReadText(PendingFileName) is not null)
        {
            throw new InvalidDataException("The speech-input preference has an unconfirmed write. Inspect saved bytes and audit before explicit repair; no Windows fallback is selected.");
        }
        return ReadBack();
    }

    public SpeechInputSelection? ReadBack()
    {
        var contents = store.ReadText(FileName, 1024);
        if (contents is null) { return null; }
        var lines = contents.Split('\n');
        if (lines.Length != 4 || !string.Equals(lines[0], "1", StringComparison.Ordinal)
            || !Enum.TryParse<SpeechInputResource>(lines[2], out var resource) || !string.Equals(resource.ToString(), lines[2], StringComparison.Ordinal)
            || !Enum.TryParse<SpeechInputResidency>(lines[3], out var residency) || !string.Equals(residency.ToString(), lines[3], StringComparison.Ordinal))
        {
            throw new InvalidDataException("The saved speech-input tuple format is unknown or malformed.");
        }
        var selection = new SpeechInputSelection(lines[1], resource, residency);
        try { selection.Validate(); }
        catch (ArgumentOutOfRangeException exception) { throw new InvalidDataException("The saved speech-input tuple is unknown.", exception); }
        return selection;
    }

    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void ConfirmWrite() => store.Delete(PendingFileName);
    public void Save(SpeechInputSelection selection)
    {
        selection.Validate();
        store.WriteText(FileName, string.Join('\n', "1", selection.ProfileId, selection.Resource.ToString(), selection.Residency.ToString()));
    }
}
