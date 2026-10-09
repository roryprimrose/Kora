namespace Kora.Application.Configuration;

public sealed record SpeechApplyResult(bool Succeeded, SpeechConfigurationState State, string? Error = null);
