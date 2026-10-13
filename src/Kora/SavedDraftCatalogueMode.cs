namespace Kora;

/// <summary>Explicit private saved-composer metadata query modes; no body search.</summary>
internal enum SavedDraftCatalogueMode
{
    /// <summary>Browse all saved nonempty drafts under the lifecycle filter.</summary>
    Browse,
    /// <summary>Use the authoritative ordinal session-name substring policy.</summary>
    NameSubstring,
    /// <summary>Use the authoritative canonical immutable session-ID policy.</summary>
    ExactId,
}
