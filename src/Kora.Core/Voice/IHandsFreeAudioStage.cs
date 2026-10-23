namespace Kora.Core.Voice;

/// <summary>
/// Host-owned local wake processing, not a general transcription or provider interface.
/// Availability requires the exact configured name/profile and playback rejection to be qualified.
/// </summary>
public interface IHandsFreeAudioStage
{
    bool IsAvailable { get; }

    Task<IHandsFreeAudioSession> OpenAsync(
        HandsFreeAudioBinding binding, CancellationToken cancellationToken);
}
