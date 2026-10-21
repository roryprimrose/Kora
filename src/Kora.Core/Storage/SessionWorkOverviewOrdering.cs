using System.Collections.Immutable;

namespace Kora.Core.Storage;

/// <summary>Pure ordering of a complete bounded projection. The source page and continuation remain untouched.</summary>
public static class SessionWorkOverviewOrdering
{
    public static ImmutableArray<SessionWorkOverviewRow> Project(SessionWorkOverviewPage page,
        SessionWorkOverviewPageOrder order = SessionWorkOverviewPageOrder.Source)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!Enum.IsDefined(order)) { throw new ArgumentOutOfRangeException(nameof(order)); }
        if (page.Records.IsDefault || page.Records.Length > SessionWorkOverviewPage.MaximumSessions
            || page.Scanned is < 0 or > SessionWorkOverviewPage.MaximumSessions
            || page.Nonmatches < 0 || page.Nonmatches > page.Scanned
            || page.Records.Length != page.Scanned - page.Nonmatches)
        {
            throw new InvalidDataException("Ordering requires a complete bounded loaded work page with consistent counters.");
        }
        var identities = new HashSet<Guid>();
        foreach (var row in page.Records)
        {
            if (row is null) { throw new InvalidDataException("The loaded work page contains a missing row."); }
            SessionListSearch.ValidateEntry(row.Session);
            if (!identities.Add(row.Session.Authority.SessionId.Value)
                || !Enum.IsDefined(row.Availability)
                || (row.Availability == SessionWorkOverviewAvailability.Observed) != (row.Work is not null))
            {
                throw new InvalidDataException("Work rows require unique exact sessions and explicit consistent observation availability.");
            }
            row.Work?.Validate(row.Session.Authority.SessionId);
        }
        _ = SessionWorkOverviewPage.Serialize(page);
        static string Id(SessionWorkOverviewRow row) => row.Session.Authority.SessionId.Value.ToString("D");
        return order switch
        {
            SessionWorkOverviewPageOrder.Source => page.Records,
            SessionWorkOverviewPageOrder.SessionIdAscending => [.. page.Records.OrderBy(Id, StringComparer.Ordinal)],
            SessionWorkOverviewPageOrder.SessionIdDescending => [.. page.Records.OrderByDescending(Id, StringComparer.Ordinal)],
            SessionWorkOverviewPageOrder.ObservedFirst => [.. page.Records
                .OrderBy(row => row.Availability != SessionWorkOverviewAvailability.Observed)
                .ThenBy(Id, StringComparer.Ordinal)],
            SessionWorkOverviewPageOrder.SessionNameAscending => [.. page.Records
                .OrderBy(row => row.Session.Metadata is null)
                .ThenBy(row => row.Session.Metadata?.Name.Value, StringComparer.Ordinal).ThenBy(Id, StringComparer.Ordinal)],
            _ => [.. page.Records.OrderBy(row => row.Session.Metadata is null)
                .ThenByDescending(row => row.Session.Metadata?.Name.Value, StringComparer.Ordinal).ThenBy(Id, StringComparer.Ordinal)]
        };
    }
}
