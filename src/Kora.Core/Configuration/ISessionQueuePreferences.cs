namespace Kora.Core.Configuration;

/// <summary>Atomic confirmed storage of fixed-profile queue overrides.</summary>
public interface ISessionQueuePreferences
{
    /// <summary>Loads confirmed state, refusing unknown or unconfirmed storage.</summary>
    SessionQueuePreferences Load();
    /// <summary>Reads exact storage during a pending commit without confirming it.</summary>
    SessionQueuePreferences ReadBack();
    /// <summary>Durably fences a write before changing overrides.</summary>
    void BeginWrite();
    /// <summary>Confirms only after required audit, readback and durable control receipt.</summary>
    void ConfirmWrite();
    /// <summary>Saves validated overrides, deleting the override file when both are reset.</summary>
    void Save(SessionQueuePreferences preferences);
}
