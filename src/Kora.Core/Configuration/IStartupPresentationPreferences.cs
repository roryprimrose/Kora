namespace Kora.Core.Configuration;

/// <summary>Device-local future manual-launch intent with explicit uncertain-write recovery.</summary>
public interface IStartupPresentationPreferences
{
    /// <summary>Reads confirmed intent, or absence; rejects malformed or unconfirmed state.</summary>
    StartupPresentation? Load();
    /// <summary>Reads exact saved intent without releasing an unconfirmed-write hold.</summary>
    StartupPresentation? ReadBack();
    /// <summary>Persists an unconfirmed-write hold before any preference change.</summary>
    void BeginWrite();
    /// <summary>Saves the reviewed value; absence resets to the actual baseline.</summary>
    void Save(StartupPresentation? value);
    /// <summary>Releases the hold only after exact readback and committed control receipts.</summary>
    void ConfirmWrite();
}
