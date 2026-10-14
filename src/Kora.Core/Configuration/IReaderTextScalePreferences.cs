namespace Kora.Core.Configuration;

/// <summary>Device-local native content typography, not content access authority.</summary>
public interface IReaderTextScalePreferences
{
    /// <summary>Loads confirmed state, rejecting malformed or unconfirmed evidence.</summary>
    int? LoadReaderTextScalePercent();
    /// <summary>Reads the exact persisted value during a pending commit.</summary>
    int? ReadBackReaderTextScalePercent();
    /// <summary>Marks the preference unconfirmed before mutation.</summary>
    void BeginReaderTextScaleWrite();
    /// <summary>Atomically saves a validated integer percentage.</summary>
    void SaveReaderTextScalePercent(int percent);
    /// <summary>Clears pending evidence after successful readback and audit.</summary>
    void ConfirmReaderTextScaleWrite();
}
