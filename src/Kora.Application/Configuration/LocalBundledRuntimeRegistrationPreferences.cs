using Kora.Core.Dependencies;
using Kora.Core.Skills;

namespace Kora.Application.Configuration;

public sealed class LocalBundledRuntimeRegistrationPreferences : IBundledRuntimeRegistrationPreferences
{
    private const string FileName = "bundled-runtime-registration.txt";
    private const string PendingFileName = "bundled-runtime-pending.txt";
    private readonly IPreferenceStore store;

    public LocalBundledRuntimeRegistrationPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalBundledRuntimeRegistrationPreferences(IPreferenceStore store) => this.store = store;

    public BundledRuntimeRegistration? Load() => store.ReadText(FileName, 256) is { } text
        ? BundledRuntimeRegistration.Parse(text) : null;

    public void Save(BundledRuntimeRegistration? registration)
    {
        if (registration is null) { store.Delete(FileName); }
        else { store.WriteText(FileName, registration.Serialize()); }
    }

    public BundledRuntimeRegistration? LoadPending() => store.ReadText(PendingFileName, 256) is { } text
        ? BundledRuntimeRegistration.Parse(text) : null;

    public void SavePending(BundledRuntimeRegistration? registration)
    {
        if (registration is null) { store.Delete(PendingFileName); }
        else { store.WriteText(PendingFileName, registration.Serialize()); }
    }
}
