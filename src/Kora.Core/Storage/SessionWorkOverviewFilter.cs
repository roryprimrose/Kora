using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Exact, content-free observation matching; absent predicates mean Any, never unknown availability.</summary>
public static class SessionWorkOverviewFilter
{
    public static void Validate(SessionWorkOverviewAvailability? availability)
    {
        if (availability is { } value && !Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(availability), "Select a defined observation availability or Any.");
        }
    }

    public static void Validate(SessionWorkOverviewContent content)
    {
        if (!Enum.IsDefined(content))
        {
            throw new ArgumentOutOfRangeException(nameof(content), "Select a defined positive observed-content predicate or Any.");
        }
    }

    public static void ValidateTaskState(HostTaskState? taskState)
    {
        if (taskState is { } value && !Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(taskState), "Select a defined observed task state or Any.");
        }
    }

    public static bool Matches(SessionWorkOverviewAvailability? availability, SessionWorkOverviewRow row,
        SessionWorkOverviewContent content = SessionWorkOverviewContent.Any, HostTaskState? taskState = null,
        SessionWorkOverviewTaskQuery taskQuery = default)
    {
        Validate(availability);
        Validate(content);
        ValidateTaskState(taskState);
        taskQuery.Validate();
        if (!Enum.IsDefined(row.Availability))
        {
            throw new InvalidDataException("The observation row has undefined availability.");
        }
        row.Work?.Validate(row.Session.Authority.SessionId);
        if (availability is not null && availability != row.Availability) { return false; }
        if ((taskState is not null || taskQuery.TaskId is not null || taskQuery.RequestId is not null)
            && (row.Availability != SessionWorkOverviewAvailability.Observed
                || row.Work is not { } observation || !observation.Tasks.Any(task =>
                    (taskState is null || task.State == taskState)
                    && (taskQuery.TaskId is null || task.Request.TaskId.Value == taskQuery.TaskId)
                    && (taskQuery.RequestId is null || task.Request.RequestId.Value == taskQuery.RequestId)))) { return false; }
        if (content == SessionWorkOverviewContent.Any) { return true; }
        if (row.Availability != SessionWorkOverviewAvailability.Observed || row.Work is not { } work) { return false; }
        return content switch
        {
            SessionWorkOverviewContent.QueueReceipt => work.QueueRecords.Length > 0,
            SessionWorkOverviewContent.Task => work.Tasks.Length > 0,
            _ => work.PendingQuestions.Length > 0
        };
    }
}
