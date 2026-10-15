using System.Globalization;
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
        DateTimeOffset? createdFromUtc = null, DateTimeOffset? createdUntilUtc = null)
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
    public MemoryScopeKind? Scope { get; }
    public MemoryReviewState? Review { get; }
    public MemoryRetentionState? Retention { get; }
    public long? MinimumRevision { get; }
    public long? MaximumRevision { get; }
    public DateTimeOffset? CreatedFromUtc { get; }
    public DateTimeOffset? CreatedUntilUtc { get; }

    public static MemoryMetadataFilter Parse(string id, MemoryScopeKind? scope, MemoryReviewState? review,
        MemoryRetentionState? retention, string minimumRevision, string maximumRevision,
        string createdFromUtc, string createdUntilUtc) =>
        new(id, scope, review, retention, OptionalUnsignedDecimal.Parse(minimumRevision),
            OptionalUnsignedDecimal.Parse(maximumRevision), ParseUtc(createdFromUtc), ParseUtc(createdUntilUtc));

    private static long? ValidateRevision(long? value)
    {
        if (value is null) { return null; }
        try { return new HostRevision(value.Value).Value; }
        catch (ArgumentOutOfRangeException exception)
        { throw new InvalidDataException("Revision bounds must be positive decimal revisions. Empty means Any.", exception); }
    }

    private static DateTimeOffset? ParseUtc(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) { return null; }
        if (!DateTimeOffset.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            || value.Offset != TimeSpan.Zero
            || !text.Equals(value.ToString("O", CultureInfo.InvariantCulture), StringComparison.Ordinal))
        { throw new InvalidDataException("Enter canonical UTC Created time: yyyy-MM-ddTHH:mm:ss.fffffff+00:00. Empty means Any."); }
        return value;
    }

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
            && (Retention is not { } retention || row.Retention == retention)
            && (MinimumRevision is not { } minimum || row.Revision >= minimum)
            && (MaximumRevision is not { } maximum || row.Revision <= maximum)
            && (CreatedFromUtc is not { } from || row.CreatedAt >= from)
            && (CreatedUntilUtc is not { } until || row.CreatedAt <= until);
    }
}
