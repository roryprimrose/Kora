namespace Kora.Core.Storage;

/// <summary>Private passive metadata observation and exact saved-composer navigation verification.</summary>
public interface ISavedDraftCatalogueStore
{
    /// <summary>Reads one bounded observation under <paramref name="filter"/>, <paramref name="cursor"/>,
    /// <paramref name="controlRevision"/>, <paramref name="admitted"/> and <paramref name="token"/>.
    /// Optional <paramref name="query"/> reuses the session metadata policy; <c>null</c> browses.
    /// Optional <paramref name="metadataFilter"/> restricts validated revision/byte/Unicode scalar metadata only.</summary>
    ValueTask<SavedDraftPage> ReadSavedDraftPage(SessionListFilter filter, string? cursor,
        long controlRevision, Func<bool> admitted, CancellationToken token, SessionListSearch? query = null,
        SavedDraftMetadataFilter? metadataFilter = null);

    /// <summary>Re-resolves <paramref name="exact"/> under <paramref name="admitted"/> and
    /// <paramref name="token"/> without reading or restoring its body.</summary>
    ValueTask<SessionWorkspaceEntry> ResolveSavedDraft(SavedDraftSummary exact,
        Func<bool> admitted, CancellationToken token);
}
