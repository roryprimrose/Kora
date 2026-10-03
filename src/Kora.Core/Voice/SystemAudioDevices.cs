namespace Kora.Core.Voice;

public static class SystemAudioDevices
{
    private const string SystemDefaultId = "system-default";

    public static MicrophoneDevice Microphone { get; } =
        new(SystemDefaultId, "System", IsSystemDefault: true);

    public static AudioOutputDevice Output { get; } =
        new(SystemDefaultId, "System", IsSystemDefault: true);
}
