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

    public static bool Matches(SessionWorkOverviewAvailability? availability, SessionWorkOverviewRow row)
    {
        Validate(availability);
        if (!Enum.IsDefined(row.Availability))
        {
            throw new InvalidDataException("The observation row has undefined availability.");
        }
        return availability is null || availability == row.Availability;
    }
}
