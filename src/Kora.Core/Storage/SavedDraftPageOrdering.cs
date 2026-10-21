using System.Collections.Immutable;

namespace Kora.Core.Storage;

/// <summary>Pure projection of complete current-page metadata; preserves the source page and cursor.</summary>
public static class SavedDraftPageOrdering
{
    public static ImmutableArray<SavedDraftSummary> Apply(SavedDraftPage page,
        SavedDraftPageOrder order = SavedDraftPageOrder.Source)
    {
        ArgumentNullException.ThrowIfNull(page);
        // Validate every candidate and the complete source scope before any ordering.
        page.Validate();
        return order switch
        {
            SavedDraftPageOrder.Source => page.Records,
            SavedDraftPageOrder.SessionNameAscending => [.. page.Records
                .OrderBy(row => row.Metadata is null)
                .ThenBy(row => row.Metadata?.Name.Value, StringComparer.Ordinal)
                .ThenBy(row => row.Session.SessionId.Value.ToString("D"), StringComparer.Ordinal)],
            SavedDraftPageOrder.SessionNameDescending => [.. page.Records
                .OrderBy(row => row.Metadata is null)
                .ThenByDescending(row => row.Metadata?.Name.Value, StringComparer.Ordinal)
                .ThenBy(row => row.Session.SessionId.Value.ToString("D"), StringComparer.Ordinal)],
            SavedDraftPageOrder.SavedRevisionAscending => [.. page.Records.OrderBy(row => row.DraftRevision)
                .ThenBy(row => row.Session.SessionId.Value.ToString("D"), StringComparer.Ordinal)],
            SavedDraftPageOrder.SavedRevisionDescending => [.. page.Records.OrderByDescending(row => row.DraftRevision)
                .ThenBy(row => row.Session.SessionId.Value.ToString("D"), StringComparer.Ordinal)],
            _ => throw new InvalidDataException("Unknown loaded-page order; no results. Clear query to restore Source order."),
        };
    }
}
