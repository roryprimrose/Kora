using Kora.Core.Dependencies;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed class LocalGlobalPttPreferences
{
    private const string FileName = "global-ptt-shortcut.txt";
    private const string PendingFileName = "global-ptt-shortcut-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalGlobalPttPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalGlobalPttPreferences(IPreferenceStore store) => this.store = store;

    public GlobalPttShortcut Load()
    {
        if (store.ReadText(PendingFileName) is not null)
        { throw new InvalidDataException("The global PTT preference has an unconfirmed write. Inspect saved state and audit before repair; the shortcut remains unavailable."); }
        return ReadBack();
    }

    public GlobalPttShortcut ReadBack() => store.ReadText(FileName, 128) is { } text
        ? GlobalPttShortcut.Parse(text) : GlobalPttShortcut.Unassigned;

    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void ConfirmWrite() => store.Delete(PendingFileName);
    public void Save(GlobalPttShortcut shortcut) => store.WriteText(FileName, shortcut.Label);
}
