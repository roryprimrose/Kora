using Kora.Core.Dependencies;
using Kora.Core.Maintenance;

namespace Kora.Application.Configuration;

public sealed class LocalMaintenancePreferences : IMaintenancePreferences
{
    private const string FileName = "maintenance-settings.txt";
    private const string PendingFileName = "maintenance-settings-unconfirmed.txt";
    private readonly IPreferenceStore store;

    public LocalMaintenancePreferences(IApplicationDataPaths paths)
        : this(new LocalPreferenceStore(paths))
    {
    }

    internal LocalMaintenancePreferences(IPreferenceStore store)
    {
        this.store = store;
    }

    public MaintenanceSettings? Load()
    {
        if (store.ReadText(PendingFileName, MaintenanceSettings.MaximumBytes) is not null)
        {
            throw new InvalidDataException("Saved maintenance settings are unconfirmed. Review and save the settings again before enabling checks.");
        }
        return store.ReadText(FileName, MaintenanceSettings.MaximumBytes) is { } text
            ? MaintenanceSettings.Parse(text) : null;
    }

    public void BeginWrite() => store.WriteText(PendingFileName, "1");

    public void Save(MaintenanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        store.WriteText(FileName, settings.Serialize());
    }

    public void ConfirmWrite() => store.Delete(PendingFileName);
}
