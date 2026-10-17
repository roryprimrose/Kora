using System.Globalization;

using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Memory;

namespace Kora.Core.UnitTests.Memory;

public sealed partial class MemoryMetadataFilterTests
{
    [Theory]
    [InlineData("")]
    [InlineData("tr-TR")]
    [InlineData("sv-SE")]
    public void OrderingHasExactStablePrimaryAndCanonicalOrdinalTieResultsWithoutChangingSource(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var a = Row with { Id = Guid.Parse("00000001-0000-0000-0000-000000000001"), CreatedAt = DateTimeOffset.MinValue };
            var b = Row with { Id = Guid.Parse("00000100-0000-0000-0000-000000000001"), CreatedAt = DateTimeOffset.MaxValue };
            var c = Row with { Id = Guid.Parse("80000000-0000-0000-0000-000000000001"),
                CreatedAt = DateTimeOffset.MaxValue, Revision = long.MaxValue };
            var d = Row with { Id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                CreatedAt = DateTimeOffset.MinValue, Revision = long.MaxValue };
            MemorySummary[] source = [d, b, a, c];
            MemorySummary[][] expected = [source, [a, d, b, c], [b, c, a, d], [a, b, c, d], [c, d, a, b]];
            var filter = new MemoryMetadataFilter();
            filter.Apply(source).Should().Equal(source);
            foreach (var order in Enum.GetValues<MemorySnapshotOrder>())
            {
                for (var repeat = 0; repeat < 3; repeat++)
                {
                    var result = filter.Apply(source, order);
                    result.Should().Equal(expected[(int)order]);
                    result.Should().NotBeSameAs(source);
                    filter.Apply([], order).Should().BeEmpty();
                    filter.Apply([a], order).Should().Equal(a);
                    new MemoryMetadataFilter(minimumRevision: long.MaxValue).Apply(source, order)
                        .Should().Equal(expected[(int)order].Where(row => row.Revision == long.MaxValue));
                    new MemoryMetadataFilter(a.Id.ToString("D"), a.Scope.Kind, a.Review, a.Retention, 1, 1,
                        a.CreatedAt, a.CreatedAt, a.Scope.Identity.ToString("D")).Apply(source, order).Should().Equal(a);
                    new MemoryMetadataFilter(scope: MemoryScopeKind.Unknown).Apply(source, order).Should().BeEmpty();
                }
            }
            source.Should().Equal(d, b, a, c);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void SnapshotAdmissionRejectsUndefinedOrderDuplicatesAndCapacityBeforeExclusion()
    {
        var filter = new MemoryMetadataFilter(scope: MemoryScopeKind.Unknown);
        var missing = () => filter.Apply(null!);
        missing.Should().Throw<ArgumentNullException>();
        foreach (var order in new[] { (MemorySnapshotOrder)(-1), (MemorySnapshotOrder)5, (MemorySnapshotOrder)int.MaxValue })
        {
            foreach (var rows in new MemorySummary[][] { [], [Row] })
            {
                var act = () => filter.Apply(rows, order);
                act.Should().Throw<InvalidDataException>().WithMessage("*Unknown*Source order*");
            }
        }
        var rowsAtLimit = Enumerable.Range(1, MemoryPolicy.MaximumEntries)
            .Select(value => Row with { Id = Guid.Parse($"{value:x8}-0000-0000-0000-000000000001") }).ToArray();
        foreach (var order in Enum.GetValues<MemorySnapshotOrder>())
        {
            foreach (var duplicate in new[] { Row, Row with { Revision = 2 } })
            {
                var act = () => filter.Apply([Row, duplicate], order);
                act.Should().Throw<InvalidDataException>().WithMessage("*Duplicate*no results*List explicitly*");
            }
            new MemoryMetadataFilter().Apply(rowsAtLimit, order).Should().HaveCount(MemoryPolicy.MaximumEntries);
            var oversized = () => filter.Apply([.. rowsAtLimit, Row], order);
            oversized.Should().Throw<InvalidDataException>().WithMessage("*limit*no results*List explicitly*");
        }
    }
}
