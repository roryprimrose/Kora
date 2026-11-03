using Kora.Core.Dependencies;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed class LocalForegroundPttPreferences
{
    private const string FileName = "foreground-ptt-shortcut.txt";
    private const string PendingFileName = "foreground-ptt-shortcut-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalForegroundPttPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalForegroundPttPreferences(IPreferenceStore store) => this.store = store;

    public ForegroundPttShortcut Load()
    {
        if (store.ReadText(PendingFileName) is not null)
        { throw new InvalidDataException("The foreground PTT preference has an unconfirmed write. Inspect saved state and audit before repair; the shortcut remains unavailable."); }
        return ReadBack();
    }

    public ForegroundPttShortcut ReadBack() => store.ReadText(FileName, 128) is { } text
        ? ForegroundPttShortcut.Parse(text) : ForegroundPttShortcut.Unassigned;

    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void ConfirmWrite() => store.Delete(PendingFileName);
    public void Save(ForegroundPttShortcut shortcut) => store.WriteText(FileName, shortcut.Label);
}
