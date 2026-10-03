namespace Kora.Core.Voice;

public sealed record AudioOutputDevice(
    string Id,
    string Name,
    bool IsMuted = false,
    bool IsSystemDefault = false);