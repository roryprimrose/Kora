namespace Kora.Core.Voice;

public interface IAudioOutputDeviceCatalog
{
    IReadOnlyList<AudioOutputDevice> GetOutputDevices();

    AudioOutputDevice? GetDefaultOutputDevice();
}