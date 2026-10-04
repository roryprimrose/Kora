namespace Kora.Core.Dependencies;

public sealed record LocalModelContext(
    string AssistantName,
    bool IsListening,
    string? PendingPowerAction,
    IReadOnlyList<LocalModelDependency> Dependencies,
    IReadOnlyList<LocalModelTask> Tasks);
