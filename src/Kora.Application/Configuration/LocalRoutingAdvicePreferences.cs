using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed class LocalRoutingAdvicePreferences
{
    private readonly IPreferenceStore store;
    private const string FileName = "routing-advice.txt";
    private const string PendingFileName = "routing-advice-unconfirmed.txt";

    public LocalRoutingAdvicePreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalRoutingAdvicePreferences(IPreferenceStore store) => this.store = store;

    public bool? Load()
    {
        if (store.ReadText(PendingFileName) is not null)
        { throw new InvalidDataException("Routing advice preference is unconfirmed. Inspect saved state and audit before repair."); }
        return ReadBack();
    }

    public bool? ReadBack() => store.ReadText(FileName) switch
    {
        null => null,
        "Off" => false,
        "On" => true,
        _ => throw new InvalidDataException("The saved routing advice preference is invalid."),
    };

    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void Save(bool enabled) => store.WriteText(FileName, enabled ? "On" : "Off");
    public void ConfirmWrite() => store.Delete(PendingFileName);
}
