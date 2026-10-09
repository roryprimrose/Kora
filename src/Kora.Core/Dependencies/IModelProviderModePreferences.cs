namespace Kora.Core.Dependencies;

/// <summary>Confirmed device-local provider mode storage, independent of session and egress authority.</summary>
public interface IModelProviderModePreferences
{
    /// <summary>Loads confirmed state, or null for the unsaved LocalOnly default.</summary>
    ModelProviderMode? Load();
    /// <summary>Reads stored state during an admitted write, without clearing its unconfirmed marker.</summary>
    ModelProviderMode? ReadBack();
    /// <summary>Marks a consequential write as unconfirmed before replacement.</summary>
    void BeginWrite();
    /// <summary>Atomically saves a validated mode.</summary>
    void Save(ModelProviderMode mode);
    /// <summary>Confirms a write after required audit, receipt and exact readback.</summary>
    void ConfirmWrite();
}
