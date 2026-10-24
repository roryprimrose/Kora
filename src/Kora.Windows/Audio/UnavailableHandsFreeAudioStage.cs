using Kora.Core.Voice;

namespace Kora.Windows.Audio;

/// <summary>D-002 has not selected or qualified a production wake/endpointing/playback profile.</summary>
public sealed class UnavailableHandsFreeAudioStage : IHandsFreeAudioStage
{
    public bool IsAvailable => false;

    public Task<IHandsFreeAudioSession> OpenAsync(
        HandsFreeAudioBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            "Hands-free activation is unavailable: no qualified local wake/name/playback profile is installed. Use push-to-talk or native controls.");
    }
}
