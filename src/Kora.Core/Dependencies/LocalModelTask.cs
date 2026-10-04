namespace Kora.Core.Dependencies;

public sealed record LocalModelTask(string Name, SetupTaskState State, int? ProgressPercentage);
