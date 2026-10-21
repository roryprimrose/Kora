using System.Collections.Immutable;

namespace Kora.Core.Storage;

/// <summary>Read-free projection of a complete admitted page; retains the original row objects.</summary>
public static class SessionListPageOrdering
{
    public static ImmutableArray<SessionWorkspaceEntry> Apply(
        ImmutableArray<SessionWorkspaceEntry> records, SessionListPageOrder order)
    {
        if (records.IsDefault || records.Length > SessionPage<SessionWorkspaceEntry>.MaximumRecords)
        { throw new InvalidDataException("Session metadata ordering requires a complete bounded loaded page."); }
        var identities = new HashSet<Guid>();
        foreach (var row in records)
        {
            SessionListSearch.ValidateEntry(row);
            if (!identities.Add(row.Authority.SessionId.Value))
            { throw new InvalidDataException("Duplicate session identity in the loaded metadata page."); }
        }
        // Validate every row before selecting an order, including Source and unnamed rows.
        if (order == SessionListPageOrder.Source) { return records; }
        var ordered = order switch
        {
            SessionListPageOrder.NameAscending => records.OrderBy(row => row.Metadata is null)
                .ThenBy(row => row.Metadata?.Name.Value, StringComparer.Ordinal),
            SessionListPageOrder.NameDescending => records.OrderBy(row => row.Metadata is null)
                .ThenByDescending(row => row.Metadata?.Name.Value, StringComparer.Ordinal),
            SessionListPageOrder.GenerationAscending => records.OrderBy(row => row.Authority.Generation.Value),
            SessionListPageOrder.GenerationDescending => records.OrderByDescending(row => row.Authority.Generation.Value),
            _ => throw new InvalidDataException("Unknown loaded session page order. Clear order to restore Source order."),
        };
        return [.. ordered.ThenBy(row => row.Authority.SessionId.Value.ToString("D"), StringComparer.Ordinal)];
    }
}
