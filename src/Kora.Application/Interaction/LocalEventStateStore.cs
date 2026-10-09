using Kora.Application.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Interaction;

namespace Kora.Application.Interaction;

public sealed class LocalEventStateStore : ILocalEventStateStore
{
    private const string FileName = "local-events.json";
    private const string PendingFileName = "local-events-unconfirmed.txt";
    private readonly IPreferenceStore store;
    public LocalEventStateStore(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalEventStateStore(IPreferenceStore store) => this.store = store;

    public LocalEventBrokerState? Load()
    {
        var marker = store.ReadText(PendingFileName, 16);
        if (marker is not null && !string.Equals(marker, "0", StringComparison.Ordinal))
        { throw new InvalidDataException("Local event suppression state is unconfirmed; no presentation or replay is admitted."); }
        var text = store.ReadText(FileName, LocalEventBrokerState.MaximumBytes);
        if ((marker is null) != (text is null))
        { throw new InvalidDataException("Local event suppression state or its confirmed commit marker is missing."); }
        return text is null ? null : LocalEventBrokerState.Deserialize(text);
    }

    public void BeginWrite()
    {
        store.WriteText(PendingFileName, "1");
        if (!string.Equals(store.ReadText(PendingFileName, 16), "1", StringComparison.Ordinal))
        { throw new InvalidDataException("Local event pending commit marker readback is unconfirmed."); }
    }
    public void Save(LocalEventBrokerState state)
    {
        var expected = LocalEventBrokerState.Serialize(state);
        store.WriteText(FileName, expected);
        if (!string.Equals(store.ReadText(FileName, LocalEventBrokerState.MaximumBytes), expected, StringComparison.Ordinal))
        { throw new InvalidDataException("Local event suppression state readback is unconfirmed."); }
    }
    public void ConfirmWrite()
    {
        store.WriteText(PendingFileName, "0");
        if (!string.Equals(store.ReadText(PendingFileName, 16), "0", StringComparison.Ordinal))
        { throw new InvalidDataException("Local event commit confirmation readback is unconfirmed."); }
    }
}
