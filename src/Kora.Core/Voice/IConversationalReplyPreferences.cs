namespace Kora.Core.Voice;

/// <summary>Device-local opt-in, independent of microphone consent and recognizer readiness.</summary>
public interface IConversationalReplyPreferences
{
    /// <summary>Loads validated state, rejecting unconfirmed writes.</summary>
    bool? Load();
    /// <summary>Reads persisted state without clearing an evidence hold.</summary>
    bool? ReadBack();
    /// <summary>Holds automatic replies before changing persisted state.</summary>
    void BeginWrite();
    /// <summary>Atomically saves the explicit choice.</summary>
    void Save(bool enabled);
    /// <summary>Clears the hold after exact readback and required audit.</summary>
    void ConfirmWrite();
}
