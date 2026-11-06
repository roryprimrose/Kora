using System.Globalization;

using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed class LocalOrdinaryTurnDeadlinePreferences
{
    private const string FileName = "ordinary-turn-deadline-minutes.txt";
    private const string PendingFileName = "ordinary-turn-deadline-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalOrdinaryTurnDeadlinePreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalOrdinaryTurnDeadlinePreferences(IPreferenceStore store) => this.store = store;

    public OrdinaryTurnDeadline? Load()
    {
        if (store.ReadText(PendingFileName) is not null)
        { throw new InvalidDataException("The ordinary task deadline write is unconfirmed. Inspect preference and audit evidence before repair."); }
        return ReadBack();
    }

    public OrdinaryTurnDeadline? ReadBack()
    {
        var text = store.ReadText(FileName);
        if (text is null) { return null; }
        try { return OrdinaryTurnDeadline.Parse(text); }
        catch (ArgumentOutOfRangeException exception)
        { throw new InvalidDataException("The saved ordinary task deadline is invalid.", exception); }
    }

    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void ConfirmWrite() => store.Delete(PendingFileName);
    public void Save(OrdinaryTurnDeadline? value)
    {
        if (value is { } deadline)
        {
            _ = deadline.Duration;
            store.WriteText(FileName, deadline.Minutes.ToString(CultureInfo.InvariantCulture));
        }
        else { store.Delete(FileName); }
    }
}
