namespace Kora.Core.Memory;

/// <summary>Presentation order of admitted content-free metadata, never freshness or authority.</summary>
public enum MemorySnapshotOrder
{
    /// <summary>Preserve the order returned by the explicit source action.</summary>
    Source,
    /// <summary>Created time ascending, then canonical lowercase D identity ordinal.</summary>
    CreatedOldest,
    /// <summary>Created time descending, then canonical lowercase D identity ordinal.</summary>
    CreatedNewest,
    /// <summary>Revision ascending, then canonical lowercase D identity ordinal.</summary>
    RevisionLowest,
    /// <summary>Revision descending, then canonical lowercase D identity ordinal.</summary>
    RevisionHighest,
}
