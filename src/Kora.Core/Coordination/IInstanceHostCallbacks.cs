namespace Kora.Core.Coordination;

public interface IInstanceHostCallbacks
{
    bool IsReady { get; }

    // Recheck lock/session privacy on the UI thread immediately before revealing.
    Task ActivateExistingAsync(CancellationToken cancellationToken);

    // Refuse active work/setup or uncertain effects; approval does not silently cancel work.
    // True means admission, capture, playback and mutable stores can safely stop.
    // Honor cancellation before requesting desktop exit; false/cancellation leaves the owner active.
    // Failed/cancelled preparation must restore only its own admission hold after actual completion,
    // never microphone enablement, playback, ephemeral approvals or an unrelated lifecycle hold.
    // The coordinator still waits for the desktop lifetime and service disposal.
    Task<bool> QuiesceForHandoffAsync(CancellationToken cancellationToken);
}
