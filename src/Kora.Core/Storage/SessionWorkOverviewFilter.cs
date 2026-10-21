using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Matches an ordinary task state without interpreting unavailable observations as empty work.</summary>
public static class SessionWorkOverviewFilter
{
    public static void ValidateTaskState(HostTaskState? taskState)
    {
        if (taskState is { } value && !Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(taskState), "Select a defined observed task state or Any.");
        }
    }

    public static bool Matches(SessionWorkOverviewRow row, HostTaskState? taskState = null)
    {
        ValidateTaskState(taskState);
        if (!Enum.IsDefined(row.Availability))
        {
            throw new InvalidDataException("The observation row has undefined availability.");
        }
        row.Work?.Validate(row.Session.Authority.SessionId);
        return taskState is null || row.Availability == SessionWorkOverviewAvailability.Observed
            && row.Work is { } observation && observation.Tasks.Any(task => task.State == taskState);
    }
}
