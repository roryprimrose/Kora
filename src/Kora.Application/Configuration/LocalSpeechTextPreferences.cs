using Kora.Core.Dependencies;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed class LocalSpeechTextPreferences : ISpeechTextPreferences
{
    private const string FileName = "speech-text-mode.txt";
    private const string PendingFileName = "speech-text-mode-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalSpeechTextPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalSpeechTextPreferences(IPreferenceStore store) => this.store = store;

    public SpeechTextMode? Load()
    {
        if (store.ReadText(PendingFileName) is not null)
        {
            throw new InvalidDataException("Speech-text evidence is unconfirmed. Inspect saved state and audit receipts before explicit repair.");
        }
        return ReadBack();
    }

    public SpeechTextMode? ReadBack() => store.ReadText(FileName) switch
    {
        null => null,
        "Off" => SpeechTextMode.Off,
        "CurrentUtterance" => SpeechTextMode.CurrentUtterance,
        _ => throw new InvalidDataException("The saved speech-text mode is invalid."),
    };

    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void ConfirmWrite() => store.Delete(PendingFileName);
    public void Save(SpeechTextMode mode)
    {
        if (!Enum.IsDefined(mode)) { throw new ArgumentOutOfRangeException(nameof(mode)); }
        store.WriteText(FileName, mode.ToString());
    }
}
