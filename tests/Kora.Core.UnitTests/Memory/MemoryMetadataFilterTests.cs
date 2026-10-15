using System.Reflection;

using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Memory;

namespace Kora.Core.UnitTests.Memory;

public sealed class MemoryMetadataFilterTests
{
    private static readonly Guid Identity = Guid.Parse("12345678-1234-1234-1234-123456789abc");
    private static MemorySummary Row => new(Identity, 1, MemoryScope.Session(new(Identity)),
        MemoryReviewState.Proposed, MemoryRetentionState.Pending, DateTimeOffset.UnixEpoch);

    [Fact]
    public void AnyAndEveryDefinedScopeComposeWithEveryGuidReviewAndRetentionPredicate()
    {
        MemoryScopeKind?[] scopes = [null, .. Enum.GetValues<MemoryScopeKind>().Select(value => (MemoryScopeKind?)value)];
        MemoryReviewState?[] reviews = [null, .. Enum.GetValues<MemoryReviewState>().Select(value => (MemoryReviewState?)value)];
        MemoryRetentionState?[] retentions = [null, .. Enum.GetValues<MemoryRetentionState>().Select(value => (MemoryRetentionState?)value)];
        MemoryScope[] rowScopes = [MemoryScope.Session(new(Identity)), MemoryScope.DeviceProfile(new(Identity)),
            MemoryScope.Project(new(Identity)), MemoryScope.Source(new(Identity))];
        foreach (var scope in scopes)
        foreach (var review in reviews)
        foreach (var retention in retentions)
        foreach (var id in new[] { "", Identity.ToString("D").ToUpperInvariant(), Guid.NewGuid().ToString("D") })
        {
            var filter = new MemoryMetadataFilter(id, scope, review, retention);
            filter.Id.Should().Be(id.Length == 0 ? null : Guid.Parse(id));
            filter.Scope.Should().Be(scope);
            filter.Review.Should().Be(review);
            filter.Retention.Should().Be(retention);
            foreach (var rowScope in rowScopes)
            foreach (var rowReview in Enum.GetValues<MemoryReviewState>())
            foreach (var rowRetention in Enum.GetValues<MemoryRetentionState>())
            {
                var row = Row with { Scope = rowScope, Review = rowReview, Retention = rowRetention };
                filter.Matches(row).Should().Be((id.Length == 0 || Guid.Parse(id) == row.Id)
                    && (scope is null || scope == rowScope.Kind)
                    && (review is null || review == rowReview)
                    && (retention is null || retention == rowRetention));
            }
        }
    }

    [Fact]
    public void InvalidSelectorsFailClosedWithExplicitRecovery()
    {
        var missing = () => new MemoryMetadataFilter(null!);
        missing.Should().Throw<ArgumentNullException>();
        foreach (var id in new[] { "bad", " ", Guid.Empty.ToString("D"), Identity.ToString("N"),
            Identity.ToString("B"), " " + Identity.ToString("D"), Identity.ToString("D") + "\n" })
        {
            var act = () => new MemoryMetadataFilter(id);
            act.Should().Throw<InvalidDataException>().WithMessage("*canonical D*Any*");
        }
        foreach (var value in new[] { -1, 999 })
        {
            Action[] actions = [
                () => _ = new MemoryMetadataFilter(scope: (MemoryScopeKind)value),
                () => _ = new MemoryMetadataFilter(review: (MemoryReviewState)value),
                () => _ = new MemoryMetadataFilter(retention: (MemoryRetentionState)value)];
            foreach (var act in actions)
            { act.Should().Throw<InvalidDataException>().WithMessage("*Unknown*Clear filters*"); }
        }
    }

    [Fact]
    public void EveryMalformedMetadataFieldFailsBeforeEvenAnExcludingPredicate()
    {
        MemorySummary[] malformed = [null!, Row with { Id = Guid.Empty }, Row with { Revision = 0 },
            Row with { Revision = -1 }, Row with { Scope = null! },
            Row with { Scope = MalformedScope((MemoryScopeKind)999, Identity) },
            Row with { Scope = MalformedScope(MemoryScopeKind.Unknown, Identity) },
            Row with { Scope = MalformedScope(MemoryScopeKind.Session, Guid.Empty) },
            Row with { Review = (MemoryReviewState)999 }, Row with { Retention = (MemoryRetentionState)(-1) },
            Row with { CreatedAt = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(1)) }];
        MemoryMetadataFilter[] filters = [new(), new(Guid.NewGuid().ToString("D")),
            new(scope: MemoryScopeKind.Source), new(review: MemoryReviewState.Rejected),
            new(retention: MemoryRetentionState.Forgotten)];
        foreach (var filter in filters)
        foreach (var row in malformed)
        {
            var act = () => filter.Matches(row);
            act.Should().Throw<InvalidDataException>().WithMessage("*no results*List explicitly*");
        }
        var complete = Row with { Revision = long.MaxValue, CreatedAt = DateTimeOffset.MaxValue };
        new MemoryMetadataFilter().Matches(complete).Should().BeTrue();
        new MemoryMetadataFilter(scope: MemoryScopeKind.Unknown).Matches(Row).Should().BeFalse();
    }

    private static MemoryScope MalformedScope(MemoryScopeKind kind, Guid identity) =>
        (MemoryScope)typeof(MemoryScope).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(MemoryScopeKind), typeof(Guid)])!.Invoke([kind, identity]);
}
