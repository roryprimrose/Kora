namespace Kora.Core.Voice;

public sealed record MicrophoneDevice(
    string Id,
    string Name,
    bool IsSystemDefault = false);