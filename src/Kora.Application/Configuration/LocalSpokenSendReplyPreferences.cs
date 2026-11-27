using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed class LocalSpokenSendReplyPreferences
{
    private readonly IPreferenceStore store;
    private const string FileName = "spoken-send-replies.txt";
    private const string PendingFileName = "spoken-send-replies-unconfirmed.txt";

    public LocalSpokenSendReplyPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalSpokenSendReplyPreferences(IPreferenceStore store) => this.store = store;

    public bool? Load()
    {
        if (store.ReadText(PendingFileName) is not null)
        { throw new InvalidDataException("Spoken send reply preference is unconfirmed. Inspect saved state and audit before repair."); }
        return ReadBack();
    }

    public bool? ReadBack() => store.ReadText(FileName) switch
    {
        null => null,
        "Off" => false,
        "On" => true,
        _ => throw new InvalidDataException("The saved spoken send reply preference is invalid."),
    };

    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void Save(bool enabled) => store.WriteText(FileName, enabled ? "On" : "Off");
    public void ConfirmWrite() => store.Delete(PendingFileName);
}
