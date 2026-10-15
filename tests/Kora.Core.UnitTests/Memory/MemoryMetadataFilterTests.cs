using System.Reflection;
using System.Globalization;

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
            new(retention: MemoryRetentionState.Forgotten), new(minimumRevision: 2),
            new(maximumRevision: 1), new(createdFromUtc: DateTimeOffset.UnixEpoch.AddTicks(1)),
            new(createdUntilUtc: DateTimeOffset.UnixEpoch.AddTicks(-1))];
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

    [Fact]
    public void InclusiveRangesComposeWithAllExistingPredicatesAndBothOpenBounds()
    {
        long?[] revisions = [null, 1, 2, 3, long.MaxValue];
        DateTimeOffset?[] times = [null, DateTimeOffset.MinValue, DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddTicks(1), DateTimeOffset.MaxValue];
        foreach (var minimum in revisions)
        foreach (var maximum in revisions)
        foreach (var from in times)
        foreach (var until in times)
        {
            if (minimum > maximum || from > until) { continue; }
            foreach (var rowRevision in new[] { 1L, 2L, 3L, long.MaxValue })
            foreach (var created in times.Skip(1).Select(value => value!.Value))
            {
                var row = Row with { Revision = rowRevision, CreatedAt = created };
                var filter = new MemoryMetadataFilter(Identity.ToString("D"), row.Scope.Kind, row.Review, row.Retention,
                    minimum, maximum, from, until);
                filter.MinimumRevision.Should().Be(minimum);
                filter.MaximumRevision.Should().Be(maximum);
                filter.CreatedFromUtc.Should().Be(from);
                filter.CreatedUntilUtc.Should().Be(until);
                filter.Matches(row).Should().Be((minimum is null || rowRevision >= minimum)
                    && (maximum is null || rowRevision <= maximum) && (from is null || created >= from)
                    && (until is null || created <= until));
            }
        }
        var exact = new MemoryMetadataFilter(Identity.ToString("D"), Row.Scope.Kind, Row.Review, Row.Retention,
            1, 1, Row.CreatedAt, Row.CreatedAt);
        exact.Matches(Row).Should().BeTrue();
        exact.Matches(Row with { Id = Guid.NewGuid() }).Should().BeFalse();
        exact.Matches(Row with { Scope = MemoryScope.Source(new(Identity)) }).Should().BeFalse();
        exact.Matches(Row with { Review = MemoryReviewState.Rejected }).Should().BeFalse();
        exact.Matches(Row with { Retention = MemoryRetentionState.Forgotten }).Should().BeFalse();
    }

    [Fact]
    public void NativeRangeParsingIsStrictAndUsesPositiveRevisionAndUtcDomainRules()
    {
        static MemoryMetadataFilter Parse(string minimum = "", string maximum = "", string from = "", string until = "") =>
            MemoryMetadataFilter.Parse("", null, null, null, minimum, maximum, from, until);
        Parse().Should().Be(new MemoryMetadataFilter());
        var epoch = DateTimeOffset.UnixEpoch.ToString("O", CultureInfo.InvariantCulture);
        var parsed = Parse("0001", long.MaxValue.ToString(CultureInfo.InvariantCulture), epoch, epoch);
        parsed.MinimumRevision.Should().Be(1);
        parsed.MaximumRevision.Should().Be(long.MaxValue);
        parsed.CreatedFromUtc.Should().Be(DateTimeOffset.UnixEpoch);
        parsed.CreatedUntilUtc.Should().Be(DateTimeOffset.UnixEpoch);
        parsed.Matches(Row).Should().BeTrue();
        foreach (var input in new[] { "0", "-1", "+1", " 1", "1 ", "1\n", "1.0", "1,000", "1e1", "١", "9223372036854775808" })
        {
            Action[] actions = [() => _ = Parse(minimum: input), () => _ = Parse(maximum: input)];
            foreach (var act in actions) { act.Should().Throw<InvalidDataException>(); }
        }
        foreach (var input in new[] { " ", "bad", epoch + "\n", " " + epoch, "1970-01-01",
            "1970-01-01T00:00:00Z", "1970-01-01T00:00:00.0000000Z",
            "1970-01-01T00:00:00.0000000+01:00", "1970-01-01T00:00:00.0000000-01:00",
            "1970-01-01T00:00:00.0000000-00:00", "1970-02-30T00:00:00.0000000+00:00" })
        {
            Action[] actions = [() => _ = Parse(from: input), () => _ = Parse(until: input)];
            foreach (var act in actions) { act.Should().Throw<InvalidDataException>().WithMessage("*canonical UTC*Any*"); }
        }
        Action[] invalid = [
            () => _ = new MemoryMetadataFilter(minimumRevision: 0),
            () => _ = new MemoryMetadataFilter(maximumRevision: -1),
            () => _ = Parse("2", "1"),
            () => _ = Parse(from: epoch, until: DateTimeOffset.MinValue.ToString("O", CultureInfo.InvariantCulture)),
            () => _ = new MemoryMetadataFilter(createdFromUtc: DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(1))),
            () => _ = new MemoryMetadataFilter(createdUntilUtc: DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(-1)))];
        foreach (var act in invalid) { act.Should().Throw<InvalidDataException>(); }
        Action[] missing = [() => _ = Parse(minimum: null!), () => _ = Parse(maximum: null!),
            () => _ = Parse(from: null!), () => _ = Parse(until: null!)];
        foreach (var act in missing) { act.Should().Throw<ArgumentNullException>(); }
        Parse(from: DateTimeOffset.MinValue.ToString("O", CultureInfo.InvariantCulture),
            until: DateTimeOffset.MaxValue.ToString("O", CultureInfo.InvariantCulture))
            .Matches(Row).Should().BeTrue();
    }

    private static MemoryScope MalformedScope(MemoryScopeKind kind, Guid identity) =>
        (MemoryScope)typeof(MemoryScope).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(MemoryScopeKind), typeof(Guid)])!.Invoke([kind, identity]);
}
