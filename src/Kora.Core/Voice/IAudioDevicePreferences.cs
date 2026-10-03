namespace Kora.Core.Voice;

public interface IAudioDevicePreferences
{
    string? LoadMicrophoneId();

    string? LoadOutputDeviceId();

    void SaveMicrophoneId(string microphoneId);

    void SaveOutputDeviceId(string outputDeviceId);

    void ClearMicrophoneId();

    void ClearOutputDeviceId();
}
