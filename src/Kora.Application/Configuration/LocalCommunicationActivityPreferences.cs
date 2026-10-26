using Kora.Core.Communication;
using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed class LocalCommunicationActivityPreferences
{
    private const string FileName = "communication-activity.txt";
    private const string PendingFileName = "communication-activity-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalCommunicationActivityPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalCommunicationActivityPreferences(IPreferenceStore store) => this.store = store;

    public CommunicationActivitySelection Load()
    {
        if (store.ReadText(PendingFileName) is not null)
        { throw new InvalidDataException("Communication activity preference has unconfirmed evidence. Inspect saved state and receipts before explicit recovery."); }
        return ReadBack();
    }

    internal CommunicationActivitySelection ReadBack()
    {
        var lines = store.ReadLines(FileName);
        if (lines is null) { return CommunicationActivitySelection.None; }
        if (lines.Length != 2 || !string.Equals(lines[0], "1", StringComparison.Ordinal))
        { throw new InvalidDataException("Communication activity preference format is invalid."); }
        return lines[1] switch
        {
            "None" => CommunicationActivitySelection.None,
            "WindowsCommunicationsAudio" => CommunicationActivitySelection.WindowsCommunicationsAudio,
            _ => throw new InvalidDataException("Communication activity source is invalid."),
        };
    }

    internal void BeginWrite() => store.WriteText(PendingFileName, "1");
    internal void ConfirmWrite() => store.Delete(PendingFileName);
    internal void Save(CommunicationActivitySelection selection)
    {
        if (!Enum.IsDefined(selection)) { throw new ArgumentOutOfRangeException(nameof(selection)); }
        if (selection == CommunicationActivitySelection.None) { store.Delete(FileName); }
        else { store.WriteLines(FileName, ["1", selection.ToString()]); }
    }
}