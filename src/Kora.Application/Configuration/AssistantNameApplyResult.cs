namespace Kora.Application.Configuration;

public sealed record AssistantNameApplyResult(
    bool Succeeded, bool Changed, AssistantNameConfigurationState State,
    string? Error = null, string? Reason = null);
