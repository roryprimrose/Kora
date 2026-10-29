using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed class LocalLogonStartupPreferences : ILogonStartupPreferences
{
    private const string FileName = "logon-startup.txt";
    private const string PendingFileName = "logon-startup-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalLogonStartupPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalLogonStartupPreferences(IPreferenceStore store) => this.store = store;

    public bool? Load()
    {
        if (store.ReadText(PendingFileName) is not null)
        { throw new InvalidDataException("Logon startup write is unconfirmed. Inspect registration, saved intent and audit before manual recovery."); }
        return ReadBack();
    }

    public bool? ReadBack() => store.ReadText(FileName) switch
    {
        null => null,
        "On" => true,
        "Off" => false,
        _ => throw new InvalidDataException("Saved logon startup intent is malformed."),
    };

    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void Save(bool? enabled)
    {
        if (enabled is null) { store.Delete(FileName); }
        else { store.WriteText(FileName, enabled.Value ? "On" : "Off"); }
    }
    public void ConfirmWrite() => store.Delete(PendingFileName);
}
