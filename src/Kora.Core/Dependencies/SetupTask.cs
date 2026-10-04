namespace Kora.Core.Dependencies;

public sealed record SetupTask(
    string Id,
    string Name,
    SetupTaskState State,
    string Detail,
    int? ProgressPercentage = null);
