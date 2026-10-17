namespace Kora.Core.Storage;

/// <summary>Exact, content-free availability matching; null means Any, never unknown availability.</summary>
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

    public static bool Matches(SessionWorkOverviewAvailability? availability, SessionWorkOverviewRow row,
        SessionWorkOverviewContent content = SessionWorkOverviewContent.Any)
    {
        Validate(availability);
        Validate(content);
        if (!Enum.IsDefined(row.Availability))
        {
            throw new InvalidDataException("The observation row has undefined availability.");
        }
        if (availability is not null && availability != row.Availability) { return false; }
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
