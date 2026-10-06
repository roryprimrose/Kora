namespace Kora.Core.Authorization;

public enum HostOperationEffect
{
    Unknown,
    BoundedRead,
    BoundedWrite,
    VoiceOrCallSettings,
    Prohibited,
}
