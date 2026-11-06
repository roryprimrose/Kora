namespace Kora.Core.Voice;

/// <summary>Stores confirmed device-local capture timing with an unconfirmed-write fence.</summary>
public interface IVoiceCaptureTimingPreferences
{
    /// <summary>Loads confirmed timing, or no saved override.</summary>
    VoiceCaptureTiming? Load();
    /// <summary>Reads exact saved bytes during a write.</summary>
    VoiceCaptureTiming? ReadBack();
    /// <summary>Fences a potentially unconfirmed write.</summary>
    void BeginWrite();
    /// <summary>Atomically saves validated timing.</summary>
    void Save(VoiceCaptureTiming timing);
    /// <summary>Removes the fence after required receipts succeed.</summary>
    void ConfirmWrite();
}
