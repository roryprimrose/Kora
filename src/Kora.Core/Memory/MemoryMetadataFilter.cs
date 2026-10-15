using Kora.Core.Commands;

namespace Kora.Core.Memory;

/// <summary>Exact metadata predicates over an already-admitted snapshot; never scope authority.</summary>
public sealed record MemoryMetadataFilter
{
    public MemoryMetadataFilter(string id = "", MemoryScopeKind? scope = null,
        MemoryReviewState? review = null, MemoryRetentionState? retention = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (id.Length != 0)
        {
            if (!Guid.TryParseExact(id, "D", out var exact) || exact == Guid.Empty
                || !id.Equals(exact.ToString("D"), StringComparison.OrdinalIgnoreCase))
            { throw new InvalidDataException("Enter a complete nonempty canonical D memory GUID, or clear it for Any."); }
            Id = exact;
        }
        if (scope is { } scopeValue && !Enum.IsDefined(scopeValue)
            || review is { } reviewValue && !Enum.IsDefined(reviewValue)
            || retention is { } retentionValue && !Enum.IsDefined(retentionValue))
        { throw new InvalidDataException("Unknown Scope, Review or Retention filter; no results. Clear filters to recover."); }
        Scope = scope;
        Review = review;
        Retention = retention;
    }

    public Guid? Id { get; }
    public MemoryScopeKind? Scope { get; }
    public MemoryReviewState? Review { get; }
    public MemoryRetentionState? Retention { get; }

    public bool Matches(MemorySummary row)
    {
        // Validate the complete row before any predicate can exclude it.
        if (row is null || row.Id == Guid.Empty || row.Revision <= 0
            || row.Scope is null || !Enum.IsDefined(row.Scope.Kind) || row.Scope.Kind == MemoryScopeKind.Unknown
            || row.Scope.Identity == Guid.Empty || !Enum.IsDefined(row.Review)
            || !Enum.IsDefined(row.Retention) || row.CreatedAt.Offset != TimeSpan.Zero)
        { throw new InvalidDataException("Invalid memory snapshot metadata; no results. List explicitly to refresh under current session admission."); }
        return (Id is not { } id || row.Id == id)
            && (Scope is not { } scope || row.Scope.Kind == scope)
            && (Review is not { } review || row.Review == review)
            && (Retention is not { } retention || row.Retention == retention);
    }
}
