using Kora.Core.Dependencies;
using Kora.Core.Providers;

namespace Kora.Application.Configuration;

public sealed class LocalProviderProfilePreferences : IProviderProfilePreferences
{
    private const string FileName = "provider-profiles.json";
    private const string PendingFileName = "provider-profiles-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalProviderProfilePreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalProviderProfilePreferences(IPreferenceStore store) => this.store = store;

    public ProviderProfilePreferences? Load()
    {
        if (store.ReadText(PendingFileName, 64) is not null)
        { throw new InvalidDataException("Provider profile evidence is unconfirmed. Inspect saved state and receipts before explicit repair; no automatic retry."); }
        return ReadBack();
    }

    public ProviderProfilePreferences? ReadBack() => store.ReadText(FileName, ProviderProfilePreferences.MaximumUtf8Bytes) is { } text
        ? ProviderProfilePreferences.Parse(text) : null;
    public void BeginWrite() => store.WriteText(PendingFileName, "1");
    public void Save(ProviderProfilePreferences preferences) => store.WriteText(FileName, preferences.Serialize());
    public void ConfirmWrite() => store.Delete(PendingFileName);
}
