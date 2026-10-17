using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionHistoryRangeFilterTests
{
    private static SessionHistoryEvent Record(long value, long? audit) =>
        new(Guid.NewGuid(), new(Guid.NewGuid()), value, new(value), SessionHistoryKind.Question,
            SessionHistoryAvailability.Available, null, null, null, value, new('a', 64), audit,
            false, "needle", [], null, [], null, null, null, null);

    [Fact]
    public void Inclusive_bounds_compose_and_missing_audit_is_not_fabricated()
    {
        var exact = new SessionHistoryRangeFilter(2, 2, 2, 2, 2, 2, 2, 2);
        exact.Validate();
        exact.Matches(Record(2, 2)).Should().BeTrue();
        default(SessionHistoryRangeFilter).Matches(Record(1, null)).Should().BeTrue();
        new SessionHistoryRangeFilter(MaximumSourceRevision: 0).Matches(Record(1, null) with { SourceRevision = 0 }).Should().BeTrue();
        var filters = new SessionHistoryRangeFilter[]
        {
            new(MinimumSequence: 2), new(MaximumSequence: 2),
            new(MinimumGeneration: 2), new(MaximumGeneration: 2),
            new(MinimumSourceRevision: 2), new(MaximumSourceRevision: 2),
            new(MinimumAuditSequence: 2), new(MaximumAuditSequence: 2),
        };
        foreach (var filter in filters)
        {
            filter.Matches(Record(2, 2)).Should().BeTrue();
            filter.Matches(Record(1, 1)).Should().Be(!filter.Matches(Record(3, 3)));
        }
        foreach (var filter in filters.Skip(6))
        { filter.Matches(Record(2, null)).Should().BeFalse(); }
        exact.Matches(Record(2, 1)).Should().BeFalse();
        exact.Matches(Record(2, 3)).Should().BeFalse();
        new SessionHistoryRangeFilter(1, long.MaxValue, 1, long.MaxValue, 0, long.MaxValue, 1, long.MaxValue).Validate();
    }

    [Fact]
    public void Invalid_and_inverted_bounds_reuse_receipt_domain_constraints()
    {
        foreach (var filter in new SessionHistoryRangeFilter[]
        {
            new(MinimumSequence: 0), new(MaximumSequence: -1),
            new(MinimumGeneration: 0), new(MaximumGeneration: -1),
            new(MinimumSourceRevision: -1), new(MaximumSourceRevision: -1),
            new(MinimumAuditSequence: 0), new(MaximumAuditSequence: -1),
            new(2, 1), new(MinimumGeneration: 2, MaximumGeneration: 1),
            new(MinimumSourceRevision: 2, MaximumSourceRevision: 1),
            new(MinimumAuditSequence: 2, MaximumAuditSequence: 1),
        })
        {
            var act = filter.Validate;
            act.Should().Throw<InvalidDataException>();
            var search = () => new SessionHistorySearch("needle", range: filter);
            search.Should().Throw<InvalidDataException>();
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("1,000")]
    [InlineData("1_000")]
    [InlineData("1.0")]
    [InlineData("9223372036854775808")]
    public void Every_native_input_rejects_non_decimal_or_overflow(string invalid)
    {
        for (var index = 0; index < 8; index++)
        {
            var values = Enumerable.Repeat("", 8).ToArray();
            values[index] = invalid;
            var act = () => Parse(values);
            act.Should().Throw<InvalidDataException>();
        }
    }

    [Fact]
    public void Parsing_is_canonical_and_every_active_bound_binds_complete_query_identity()
    {
        Parse(Enumerable.Repeat("", 8).ToArray()).Should().Be(default(SessionHistoryRangeFilter));
        Parse(["01", "1", "1", "1", "0", "0", "1", "1"])
            .Should().Be(new SessionHistoryRangeFilter(1, 1, 1, 1, 0, 0, 1, 1));
        var lexical = new SessionHistorySearch("needle absent");
        new SessionHistorySearch("absent NEEDLE", default, default).Digest.Should().Be(lexical.Digest);
        var digests = new List<string> { lexical.Digest };
        for (var index = 0; index < 8; index++)
        {
            var values = Enumerable.Repeat("", 8).ToArray();
            values[index] = "2";
            var range = Parse(values);
            var search = new SessionHistorySearch("absent needle", range: range);
            digests.Add(search.Digest);
            search.Digest.Should().Be(new SessionHistorySearch("NEEDLE absent", range: range).Digest);
            search.Matches(Record(2, 2), TestContext.Current.CancellationToken).Should().BeTrue();
            var metadata = new SessionHistoryMetadataFilter(Kind: SessionHistoryKind.Answer);
            var combined = new SessionHistorySearch("needle absent", metadata, range);
            combined.Digest.Should().NotBe(search.Digest);
            combined.Matches(Record(2, 2), TestContext.Current.CancellationToken).Should().BeFalse();
            combined.Matches(Record(2, 2) with { Kind = SessionHistoryKind.Answer }, TestContext.Current.CancellationToken).Should().BeTrue();
            new SessionHistorySearch("nonmatching", range: range).Matches(Record(2, 2), TestContext.Current.CancellationToken).Should().BeFalse();
        }
        digests.Should().OnlyHaveUniqueItems();
    }

    private static SessionHistoryRangeFilter Parse(string[] values) => SessionHistoryRangeFilter.Parse(
        values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7]);
}
