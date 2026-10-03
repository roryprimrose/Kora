namespace Kora.Core.Voice;

public sealed record SpeechProvider(
    string Id,
    string Name,
    string Description,
    bool IsInstalled,
    bool IsBuiltIn,
    long? DownloadSizeBytes,
    string? DefaultVoiceId);
