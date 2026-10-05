namespace Kora.Core.Platform;

[Flags]
public enum WindowsPrivacyChangeReason
{
    Unknown = 0,
    Session = 1,
    Power = 2,
    MicrophonePermission = 4,
    DeviceTopology = 8,
    DefaultMicrophone = 16,
    DefaultSpeaker = 32,
    Polling = 64,
    InputTopology = 128,
    OutputTopology = 256,
}
