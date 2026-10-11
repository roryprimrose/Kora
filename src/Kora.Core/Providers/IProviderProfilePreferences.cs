namespace Kora.Core.Providers;

/// <summary>Device-local selections with an explicit unconfirmed-write fence.</summary>
public interface IProviderProfilePreferences
{
    /// <summary>Loads confirmed selections or fails on unknown saved state.</summary>
    ProviderProfilePreferences? Load();
    /// <summary>Reads exact saved selections while retaining the confirmation fence.</summary>
    ProviderProfilePreferences? ReadBack();
    /// <summary>Durably marks a requested write unconfirmed.</summary>
    void BeginWrite();
    /// <summary>Atomically saves validated desired selections, without execution authority.</summary>
    void Save(ProviderProfilePreferences preferences);
    /// <summary>Clears the fence only after authoritative audit, control receipt and readback.</summary>
    void ConfirmWrite();
}
