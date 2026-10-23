namespace Kora.Core.Voice;

/// <summary>Device-local typed selection with explicit recovery for an unconfirmed write.</summary>
public interface ISpeechInputPreferences
{
    /// <summary>Loads the validated selection; rejects unknown formats and unconfirmed evidence.</summary>
    SpeechInputSelection? Load();
    /// <summary>Reads persisted bytes without clearing an unconfirmed-write hold.</summary>
    SpeechInputSelection? ReadBack();
    /// <summary>Persists the evidence hold before replacement.</summary>
    void BeginWrite();
    /// <summary>Atomically replaces the validated preference.</summary>
    void Save(SpeechInputSelection selection);
    /// <summary>Clears only the evidence hold after exact readback and terminal audit.</summary>
    void ConfirmWrite();
}
