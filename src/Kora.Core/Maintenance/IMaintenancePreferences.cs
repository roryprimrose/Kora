namespace Kora.Core.Maintenance;

/// <summary>Stores device-local release check preferences and their audit confirmation state.</summary>
public interface IMaintenancePreferences
{
    /// <summary>Loads confirmed settings, or returns <c>null</c> when no settings have been saved.</summary>
    MaintenanceSettings? Load();

    /// <summary>Marks a preference write as unconfirmed before changing saved settings.</summary>
    void BeginWrite();

    /// <summary>Persists the supplied settings without confirming their audit outcome.</summary>
    /// <param name="settings">The validated settings to save.</param>
    void Save(MaintenanceSettings settings);

    /// <summary>Clears the pending marker after the write and its audit outcome have succeeded.</summary>
    void ConfirmWrite();
}
