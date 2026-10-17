using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.Memory;

/// <summary>Exact metadata predicates over an already-admitted snapshot; never scope authority.</summary>
public sealed record MemoryMetadataFilter
{
    public MemoryMetadataFilter(string id = "", MemoryScopeKind? scope = null,
        MemoryReviewState? review = null, MemoryRetentionState? retention = null,
        long? minimumRevision = null, long? maximumRevision = null,
        DateTimeOffset? createdFromUtc = null, DateTimeOffset? createdUntilUtc = null,
        string scopeIdentity = "")
    {
        Id = ParseIdentity(id, "memory");
        ScopeIdentity = ParseIdentity(scopeIdentity, "memory scope identity");
        if (scope is { } scopeValue && !Enum.IsDefined(scopeValue)
            || review is { } reviewValue && !Enum.IsDefined(reviewValue)
            || retention is { } retentionValue && !Enum.IsDefined(retentionValue))
        { throw new InvalidDataException("Unknown Scope, Review or Retention filter; no results. Clear filters to recover."); }
        Scope = scope;
        Review = review;
        Retention = retention;
        MinimumRevision = ValidateRevision(minimumRevision);
        MaximumRevision = ValidateRevision(maximumRevision);
        if (minimumRevision > maximumRevision)
        { throw new InvalidDataException("Revision minimum must be <= maximum. Clear either bound for Any."); }
        if (createdFromUtc is { Offset: var fromOffset } && fromOffset != TimeSpan.Zero
            || createdUntilUtc is { Offset: var untilOffset } && untilOffset != TimeSpan.Zero
            || createdFromUtc > createdUntilUtc)
        { throw new InvalidDataException("Created bounds must be UTC with From <= Until. Clear either bound for Any."); }
        CreatedFromUtc = createdFromUtc;
        CreatedUntilUtc = createdUntilUtc;
    }

    public Guid? Id { get; }
    public Guid? ScopeIdentity { get; }
    public MemoryScopeKind? Scope { get; }
    public MemoryReviewState? Review { get; }
    public MemoryRetentionState? Retention { get; }
    public long? MinimumRevision { get; }
    public long? MaximumRevision { get; }
    public DateTimeOffset? CreatedFromUtc { get; }
    public DateTimeOffset? CreatedUntilUtc { get; }

    public static MemoryMetadataFilter Parse(string id, MemoryScopeKind? scope, MemoryReviewState? review,
        MemoryRetentionState? retention, string minimumRevision, string maximumRevision,
        string createdFromUtc, string createdUntilUtc, string scopeIdentity = "") =>
        new(id, scope, review, retention, OptionalUnsignedDecimal.Parse(minimumRevision),
            OptionalUnsignedDecimal.Parse(maximumRevision), OptionalCanonicalUtc.Parse(createdFromUtc), OptionalCanonicalUtc.Parse(createdUntilUtc),
            scopeIdentity);

    private static Guid? ParseIdentity(string text, string field)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) { return null; }
        if (!Guid.TryParseExact(text, "D", out var exact) || exact == Guid.Empty
            || !text.Equals(exact.ToString("D"), StringComparison.OrdinalIgnoreCase))
        { throw new InvalidDataException($"Enter a complete nonempty canonical D {field} GUID, or clear it for Any."); }
        return exact;
    }

    private static long? ValidateRevision(long? value)
    {
        if (value is null) { return null; }
        try { return new HostRevision(value.Value).Value; }
        catch (ArgumentOutOfRangeException exception)
        { throw new InvalidDataException("Revision bounds must be positive decimal revisions. Empty means Any.", exception); }
    }

    public bool Matches(MemorySummary row)
    {
        // Validate the complete row before any predicate can exclude it.
        ValidateRow(row);
        return (Id is not { } id || row.Id == id)
            && (ScopeIdentity is not { } identity || row.Scope.Identity == identity)
            && (Scope is not { } scope || row.Scope.Kind == scope)
            && (Review is not { } review || row.Review == review)
            && (Retention is not { } retention || row.Retention == retention)
            && (MinimumRevision is not { } minimum || row.Revision >= minimum)
            && (MaximumRevision is not { } maximum || row.Revision <= maximum)
            && (CreatedFromUtc is not { } from || row.CreatedAt >= from)
            && (CreatedUntilUtc is not { } until || row.CreatedAt <= until);
    }

    public IReadOnlyList<MemorySummary> Apply(IReadOnlyList<MemorySummary> snapshot,
        MemorySnapshotOrder order = MemorySnapshotOrder.Source)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Count > MemoryPolicy.MaximumEntries)
        { throw new InvalidDataException("Memory snapshot exceeds the identity limit; no results. List explicitly to refresh."); }
        var identities = new HashSet<Guid>();
        foreach (var row in snapshot)
        {
            ValidateRow(row);
            if (!identities.Add(row.Id))
            { throw new InvalidDataException("Duplicate memory snapshot identity; no results. List explicitly to refresh."); }
        }
        // Finish validating the entire candidate snapshot before filtering or ordering.
        var matches = snapshot.Where(Matches);
        return order switch
        {
            MemorySnapshotOrder.Source => [.. matches],
            MemorySnapshotOrder.CreatedOldest => [.. matches.OrderBy(row => row.CreatedAt)
                .ThenBy(row => row.Id.ToString("D"), StringComparer.Ordinal)],
            MemorySnapshotOrder.CreatedNewest => [.. matches.OrderByDescending(row => row.CreatedAt)
                .ThenBy(row => row.Id.ToString("D"), StringComparer.Ordinal)],
            MemorySnapshotOrder.RevisionLowest => [.. matches.OrderBy(row => row.Revision)
                .ThenBy(row => row.Id.ToString("D"), StringComparer.Ordinal)],
            MemorySnapshotOrder.RevisionHighest => [.. matches.OrderByDescending(row => row.Revision)
                .ThenBy(row => row.Id.ToString("D"), StringComparer.Ordinal)],
            _ => throw new InvalidDataException("Unknown memory snapshot order; no results. Clear filters to restore Source order."),
        };
    }

    private static void ValidateRow(MemorySummary row)
    {
        if (row is null || row.Id == Guid.Empty || row.Revision <= 0
            || row.Scope is null || !Enum.IsDefined(row.Scope.Kind) || row.Scope.Kind == MemoryScopeKind.Unknown
            || row.Scope.Identity == Guid.Empty || !Enum.IsDefined(row.Review)
            || !Enum.IsDefined(row.Retention) || row.CreatedAt.Offset != TimeSpan.Zero)
        { throw new InvalidDataException("Invalid memory snapshot metadata; no results. List explicitly to refresh under current session admission."); }
    }
}
