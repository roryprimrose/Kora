using System.Globalization;

using AwesomeAssertions;

using Kora.Core.Context;

namespace Kora.Core.UnitTests.Context;

public sealed partial class LocalFileLiteralFindTests
{
    [Theory]
    [InlineData("a A a", "A", 1, 3)]
    [InlineData("é É e E e\u0301", "É", 1, 2)]
    [InlineData("é e\u0301", "e\u0301", 1, 1)]
    [InlineData("I i \u0130 \u0131", "i", 1, 2)]
    [InlineData("\U00010400 \U00010428 🙂", "\U00010428", 1, 2)]
    [InlineData("ß SS", "ss", 0, 1)]
    [InlineData("aaaaAXaa", "AA", 0, 3)]
    [InlineData("é", "e", 0, 0)]
    public void ExplicitOrdinalComparisonPreservesOriginalRangesAndCanonicalCountedSequence(
        string text, string query, int exactCount, int ignoreCaseCount)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var file = File(text);
            foreach (var comparison in new[] { StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase })
            {
                var expected = comparison == StringComparison.Ordinal ? exactCount : ignoreCaseCount;
                var result = LocalFileLiteralFind.FindCounted(file, query, LocalFileFindDirection.First, comparison: comparison);
                result.Count.Should().Be(expected);
                var matches = new List<LocalFileLiteralMatch>();
                while (result.Match is { } match)
                {
                    matches.Add(match);
                    match.Source.Should().Be(file.Reference);
                    match.Length.Should().Be(query.Length);
                    file.Text.Substring(match.Start, match.Length).Equals(query, comparison).Should().BeTrue();
                    result.Ordinal.Should().Be(matches.Count);
                    result = LocalFileLiteralFind.FindCounted(file, query, LocalFileFindDirection.Next, match, comparison);
                    result.Count.Should().Be(expected);
                }
                result.Ordinal.Should().Be(expected);
                for (var index = matches.Count - 1; index >= 0; index--)
                {
                    result = LocalFileLiteralFind.FindCounted(file, query, LocalFileFindDirection.Previous, matches[index], comparison);
                    result.Match.Should().Be(index == 0 ? null : matches[index - 1]);
                    result.Ordinal.Should().Be(index == 0 ? 1 : index);
                    result.Count.Should().Be(expected);
                }
                LocalFileLiteralFind.Find(file, query, LocalFileFindDirection.First, comparison: comparison)
                    .Should().Be(matches.FirstOrDefault());
            }
            file.Text.Should().Be(text);
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    [Fact]
    public void ComparisonCursorsCannotCrossModeExactQueryOrSourceEvenWhenSubstringStillMatches()
    {
        var file = File("AA aa");
        var insensitive = LocalFileLiteralFind.Find(file, "aa", LocalFileFindDirection.First,
            comparison: StringComparison.OrdinalIgnoreCase)!;
        var exact = LocalFileLiteralFind.Find(file, "aa", LocalFileFindDirection.First)!;
        var portable = SnapshotLiteralFind.Find(file.Text, "aa", SnapshotFindDirection.First,
            comparison: StringComparison.OrdinalIgnoreCase)!;
        foreach (var direction in new[] { LocalFileFindDirection.Next, LocalFileFindDirection.Previous })
        {
            foreach (var (source, query, cursor, comparison) in new[]
            {
                (file, "aa", insensitive, StringComparison.Ordinal),
                (file, "AA", insensitive, StringComparison.OrdinalIgnoreCase),
                (file, "aa", exact, StringComparison.OrdinalIgnoreCase),
                (File(file.Text), "aa", insensitive, StringComparison.OrdinalIgnoreCase),
                (file, "aa", insensitive with { Length = 1 }, StringComparison.OrdinalIgnoreCase),
            })
            {
                var find = () => LocalFileLiteralFind.Find(source, query, direction, cursor, comparison);
                find.Should().Throw<InvalidDataException>();
                var counted = () => LocalFileLiteralFind.FindCounted(source, query, direction, cursor, comparison);
                counted.Should().Throw<InvalidDataException>();
            }
        }
        foreach (var (text, query, cursor, comparison) in new[]
        {
            ("AA aa!", "aa", portable, StringComparison.OrdinalIgnoreCase),
            (file.Text, "AA", portable, StringComparison.OrdinalIgnoreCase),
            (file.Text, "aa", portable, StringComparison.Ordinal),
            (file.Text, "aa", new SnapshotLiteralSpan(0, 2) { Comparison = StringComparison.OrdinalIgnoreCase }, StringComparison.OrdinalIgnoreCase),
        })
        {
            var find = () => SnapshotLiteralFind.Find(text, query, SnapshotFindDirection.Next, cursor, comparison);
            find.Should().Throw<InvalidDataException>();
        }
        var first = SnapshotLiteralFind.Find("aaaaAXaa", "aa", SnapshotFindDirection.First,
            comparison: StringComparison.OrdinalIgnoreCase)!;
        var overlapping = first with { Start = 3 };
        var invalid = () => SnapshotLiteralFind.FindCounted("aaaaAXaa", "aa", SnapshotFindDirection.Previous,
            overlapping, StringComparison.OrdinalIgnoreCase);
        invalid.Should().Throw<InvalidDataException>();
        var last = first with { Start = 6 };
        SnapshotLiteralFind.Find("aaaaAXaa", "aa", SnapshotFindDirection.Previous, last,
            StringComparison.OrdinalIgnoreCase)!.Start.Should().Be(3);
        SnapshotLiteralFind.FindCounted("aaaaAXaa", "aa", SnapshotFindDirection.Previous, last,
            StringComparison.OrdinalIgnoreCase).Match!.Start.Should().Be(2);
    }

    [Theory]
    [InlineData(StringComparison.CurrentCulture)]
    [InlineData(StringComparison.CurrentCultureIgnoreCase)]
    [InlineData(StringComparison.InvariantCulture)]
    [InlineData(StringComparison.InvariantCultureIgnoreCase)]
    [InlineData((StringComparison)99)]
    public void UnsupportedComparisonsFailClosed(StringComparison comparison)
    {
        var file = File("a");
        var find = () => LocalFileLiteralFind.Find(file, "a", LocalFileFindDirection.First, comparison: comparison);
        find.Should().Throw<ArgumentOutOfRangeException>();
        var counted = () => LocalFileLiteralFind.FindCounted(file, "a", LocalFileFindDirection.First, comparison: comparison);
        counted.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void OptInDoesNotRelaxQueryBoundsOrSurrogatePolicyAndKeepsExactOriginalMatchText()
    {
        var file = File("É é 🙂");
        var match = LocalFileLiteralFind.Find(file, "é", LocalFileFindDirection.First,
            comparison: StringComparison.OrdinalIgnoreCase)!;
        file.Text.Substring(match.Start, match.Length).Should().Be("É");
        foreach (var query in new[] { "", "\n", new string('x', 257), new string('界', 171), "\ud800", "\udc00" })
        {
            var find = () => LocalFileLiteralFind.Find(file, query, LocalFileFindDirection.First,
                comparison: StringComparison.OrdinalIgnoreCase);
            find.Should().Throw<InvalidDataException>();
        }
        var maximum = new string('é', 256);
        LocalFileLiteralFind.FindCounted(File(maximum + maximum), maximum, LocalFileFindDirection.First,
            comparison: StringComparison.OrdinalIgnoreCase).Count.Should().Be(2);
        LocalFileLiteralFind.Find(file, "🙂", LocalFileFindDirection.First,
            comparison: StringComparison.OrdinalIgnoreCase)!.Length.Should().Be(2);
    }
}
