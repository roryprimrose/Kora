namespace Kora.Core.Configuration;

/// <summary>Device-local proactive speech quieting, independent of listening and visual notices.</summary>
public interface IQuietHoursPreferences
{
    /// <summary>Reads confirmed intent; missing intent means Off, not new speech consent.</summary>
    QuietHoursSchedule? Load();
    /// <summary>Reads saved bytes without clearing an unconfirmed-write marker.</summary>
    QuietHoursSchedule? ReadBack();
    /// <summary>Durably marks an attempted write unconfirmed.</summary>
    void BeginWrite();
    /// <summary>Clears the marker only after authoritative receipts and exact readback.</summary>
    void ConfirmWrite();
    /// <summary>Saves one explicit schedule, or resets to the default Off when null.</summary>
    void Save(QuietHoursSchedule? schedule);
}
