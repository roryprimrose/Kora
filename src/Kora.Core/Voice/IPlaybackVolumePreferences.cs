using Kora.Core.Configuration;

namespace Kora.Core.Voice;

public interface IPlaybackVolumePreferences
{
    PlaybackVolume? Load();
    void BeginWrite();
    PlaybackVolume? ReadBack();
    void ConfirmWrite();
    void Save(PlaybackVolume volume);
    void Reset();
}
