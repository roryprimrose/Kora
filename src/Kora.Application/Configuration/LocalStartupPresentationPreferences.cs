using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed class LocalStartupPresentationPreferences : IStartupPresentationPreferences
{
    private const string FileName = "manual-startup-presentation.txt";
    private const string PendingFileName = "manual-startup-presentation-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalStartupPresentationPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalStartupPresentationPreferences(IPreferenceStore store) => this.store = store;

    public StartupPresentation? Load()
    {
        if (store.ReadText(PendingFileName, 16) is not null)
        { throw new InvalidDataException("Manual startup presentation write is unconfirmed. Inspect saved intent and audit before manual recovery."); }
        return ReadBack();
    }

    public StartupPresentation? ReadBack() => store.ReadText(FileName, 32) switch
    {
        null => null,
        "v1:Tray" => StartupPresentation.Tray,
        "v1:Workspace" => StartupPresentation.Workspace,
        _ => throw new InvalidDataException("Saved manual startup presentation is malformed or has an unsupported version."),
    };

    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void Save(StartupPresentation? value)
    {
        if (value is null) { store.Delete(FileName); }
        else
        {
            if (!Enum.IsDefined(value.Value)) { throw new ArgumentOutOfRangeException(nameof(value)); }
            store.WriteText(FileName, value == StartupPresentation.Tray ? "v1:Tray" : "v1:Workspace");
        }
    }
    public void ConfirmWrite() => store.Delete(PendingFileName);
}
