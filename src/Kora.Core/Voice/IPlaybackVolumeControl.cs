using Kora.Core.Configuration;

namespace Kora.Core.Voice;

/// <summary>Gain on owned speech only; null holds output unavailable. Changes retire output, never replay it.</summary>
public interface IPlaybackVolumeControl
{
    void SetPlaybackVolume(PlaybackVolume? volume);
}
