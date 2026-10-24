using System.Text;

using AwesomeAssertions;

using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Context;

public sealed partial class LocalFileLiteralFindTests
{
    [Theory]
    [InlineData("", "a", 0)]
    [InlineData("alpha", "missing", 0)]
    [InlineData("alpha", "alpha", 1)]
    [InlineData("alpha ALPHA alpha", "alpha", 2)]
    [InlineData("aaaaaaa", "aaa", 2)]
    [InlineData("aaaaaXaa", "aa", 3)]
    [InlineData("e\u0301 é é", "é", 2)]
    [InlineData("\r\n🙂\r\n🙂", "🙂", 2)]
    [InlineData(" .*?  .*? ", " .*? ", 2)]
    public void CountedFindTraversesExactForwardSequenceAndItsReverse(string text, string query, int expected)
    {
        var file = File(text);
        var result = LocalFileLiteralFind.FindCounted(file, query, LocalFileFindDirection.First);
        result.Count.Should().Be(expected);
        result.Ordinal.Should().Be(expected == 0 ? 0 : 1);
        if (expected == 0) { result.Match.Should().BeNull(); return; }
        var matches = new List<LocalFileLiteralMatch>();
        while (result.Match is not null)
        {
            matches.Add(result.Match);
            result.Match.Source.Should().Be(file.Reference);
            file.Text.Substring(result.Match.Start, result.Match.Length).Should().Be(query);
            result.Count.Should().Be(expected);
            result.Ordinal.Should().Be(matches.Count);
            result = LocalFileLiteralFind.FindCounted(file, query, LocalFileFindDirection.Next, matches[^1]);
        }
        result.Count.Should().Be(expected);
        result.Ordinal.Should().Be(expected);
        matches.Should().HaveCount(expected);
        for (var index = matches.Count - 1; index >= 0; index--)
        {
            result = LocalFileLiteralFind.FindCounted(file, query, LocalFileFindDirection.Previous, matches[index]);
            result.Count.Should().Be(expected);
            result.Ordinal.Should().Be(index == 0 ? 1 : index);
            result.Match.Should().Be(index == 0 ? null : matches[index - 1]);
        }
        LocalFileLiteralFind.FindCounted(file, query, LocalFileFindDirection.First, matches[^1]).Ordinal.Should().Be(1);
    }

    [Fact]
    public void CountedFindReversesCanonicalSequenceWithoutChangingUncountedNavigation()
    {
        var file = File("aaaaaXaa");
        var last = new LocalFileLiteralMatch(file.Reference, 6, 2);
        LocalFileLiteralFind.Find(file, "aa", LocalFileFindDirection.Previous, last)!.Start.Should().Be(3);
        LocalFileLiteralFind.FindCounted(file, "aa", LocalFileFindDirection.Previous, last)
            .Match!.Start.Should().Be(2);
        var overlapping = new LocalFileLiteralMatch(file.Reference, 3, 2);
        foreach (var direction in new[] { LocalFileFindDirection.Next, LocalFileFindDirection.Previous })
        {
            var find = () => LocalFileLiteralFind.FindCounted(file, "aa", direction, overlapping);
            find.Should().Throw<InvalidDataException>();
        }
    }

    [Fact]
    public void CountedFindCountsCompleteMaximumFileAndSharedMaximumQueryWithoutTruncation()
    {
        var file = File(new('a', LocalFilePolicy.MaximumBytes));
        LocalFileLiteralFind.FindCounted(file, "a", LocalFileFindDirection.First).Count.Should().Be(LocalFilePolicy.MaximumBytes);
        var query = new string('é', 256);
        LocalFileLiteralFind.FindCounted(File(query + query), query, LocalFileFindDirection.First).Count.Should().Be(2);
        var supplementary = string.Concat(Enumerable.Repeat("🙂", 128));
        LocalFileLiteralFind.FindCounted(File(supplementary), supplementary, LocalFileFindDirection.First)
            .Match!.Length.Should().Be(256);
        var nullFile = () => LocalFileLiteralFind.FindCounted(null!, "a", LocalFileFindDirection.First);
        nullFile.Should().Throw<ArgumentNullException>();
        var invalidDirection = () => LocalFileLiteralFind.FindCounted(file, "a", (LocalFileFindDirection)99);
        invalidDirection.Should().Throw<ArgumentOutOfRangeException>();
        foreach (var invalidQuery in new[] { new string('x', 257), new string('界', 171), new string('\ud800', 1), new string('\udc00', 1) })
        {
            var find = () => LocalFileLiteralFind.FindCounted(file, invalidQuery, LocalFileFindDirection.First);
            find.Should().Throw<InvalidDataException>();
        }
        var nullQuery = () => LocalFileLiteralFind.FindCounted(file, null!, LocalFileFindDirection.First);
        nullQuery.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("a A a", "A", 2)]
    [InlineData(" a  a ", " a ", 0)]
    [InlineData("x .*?[a] x", ".*?[a]", 2)]
    [InlineData("e\u0301 é", "é", 3)]
    [InlineData("\r\n🙂\r\n🙂", "🙂", 2)]
    [InlineData(" abc", " ", 0)]
    public void FindPreservesOrdinalCaseSpacesAndOriginalUtf16Offsets(string text, string query, int start)
    {
        var file = File(text);
        var result = LocalFileLiteralFind.Find(file, query, LocalFileFindDirection.First)!;
        result.Should().Be(new LocalFileLiteralMatch(file.Reference, start, query.Length));
        file.Text.Substring(result.Start, result.Length).Should().Be(query);
        file.Text.Should().Be(text);
    }

    [Fact]
    public void FindMovesOneNonoverlappingSpanWithoutWrappingOrClaimingDirectionalAbsenceIsGlobal()
    {
        var file = File("aaaaaaa");
        var first = LocalFileLiteralFind.Find(file, "aaa", LocalFileFindDirection.First)!;
        first.Start.Should().Be(0);
        LocalFileLiteralFind.Find(file, "aaa", LocalFileFindDirection.Previous, first).Should().BeNull();
        var next = LocalFileLiteralFind.Find(file, "aaa", LocalFileFindDirection.Next, first)!;
        next.Start.Should().Be(3);
        LocalFileLiteralFind.Find(file, "aaa", LocalFileFindDirection.Next, next).Should().BeNull();
        LocalFileLiteralFind.Find(file, "aaa", LocalFileFindDirection.Previous, next).Should().Be(first);
        LocalFileLiteralFind.Find(file, "aaa", LocalFileFindDirection.First, next).Should().Be(first);
        LocalFileLiteralFind.Find(file, "missing", LocalFileFindDirection.First).Should().BeNull();
        LocalFileLiteralFind.Find(File(string.Empty), "aaa", LocalFileFindDirection.First).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("\0")]
    [InlineData("\t")]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\u0001")]
    [InlineData("\u0085")]
    [InlineData("\u202e")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void FindRejectsEmptyUnsafeControlsAndMalformedUnicodeWithoutTransformingQuery(string query)
    {
        LocalFileLiteralFind.IsValidQuery(query).Should().BeFalse();
        var find = () => LocalFileLiteralFind.Find(File("source"), query, LocalFileFindDirection.First);
        find.Should().Throw<InvalidDataException>();
        var counted = () => LocalFileLiteralFind.FindCounted(File("source"), query, LocalFileFindDirection.First);
        counted.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void FindUsesSharedUtf16AndStrictUtf8BoundsNotTermOrNormalizationRules()
    {
        LocalFileLiteralFind.IsValidQuery(new('x', 256)).Should().BeTrue();
        LocalFileLiteralFind.IsValidQuery(new('x', 257)).Should().BeFalse();
        LocalFileLiteralFind.IsValidQuery(new('é', 256)).Should().BeTrue();
        LocalFileLiteralFind.IsValidQuery(new string('界', 170) + "é").Should().BeTrue();
        LocalFileLiteralFind.IsValidQuery(new('界', 171)).Should().BeFalse();
        LocalFileLiteralFind.IsValidQuery(string.Concat(Enumerable.Repeat("🙂", 128))).Should().BeTrue();
        LocalFileLiteralFind.IsValidQuery(string.Join(" ", Enumerable.Repeat("*", 100))).Should().BeTrue();
        LocalFileLiteralFind.IsValidQuery(new string('\ud800', 1)).Should().BeFalse();
        LocalFileLiteralFind.IsValidQuery(new string('\udc00', 1)).Should().BeFalse();
        var query = new string('é', 256);
        LocalFileLiteralFind.Find(File(query), query, LocalFileFindDirection.First)!.Length.Should().Be(256);
        var nullQuery = () => LocalFileLiteralFind.IsValidQuery(null!);
        nullQuery.Should().Throw<ArgumentNullException>();
        var nullFile = () => LocalFileLiteralFind.Find(null!, "a", LocalFileFindDirection.First);
        nullFile.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("revision")]
    [InlineData("item")]
    [InlineData("digest")]
    [InlineData("length")]
    [InlineData("negative")]
    [InlineData("outside")]
    [InlineData("text")]
    public void FindRejectsStaleOrForgedDirectionalCursor(string invalid)
    {
        var file = File("abc abc");
        var span = new LocalFileLiteralMatch(file.Reference, 0, 3);
        var selected = invalid switch
        {
            "missing" => null,
            "foreign" => span with { Source = File("abc abc").Reference },
            "revision" => span with { Source = file.Reference with { RevisionId = Guid.NewGuid() } },
            "item" => span with { Source = file.Reference with { ItemId = Guid.NewGuid() } },
            "digest" => span with { Source = file.Reference with { Digest = new('a', 64) } },
            "length" => span with { Length = int.MaxValue },
            "negative" => span with { Start = -1 },
            "outside" => span with { Start = int.MaxValue },
            _ => span with { Start = 1 },
        };
        foreach (var direction in new[] { LocalFileFindDirection.Next, LocalFileFindDirection.Previous })
        {
            var find = () => LocalFileLiteralFind.Find(file, "abc", direction, selected);
            find.Should().Throw<InvalidDataException>();
            var counted = () => LocalFileLiteralFind.FindCounted(file, "abc", direction, selected);
            counted.Should().Throw<InvalidDataException>();
        }
    }

    [Fact]
    public void FindRefusesUnknownDirectionAndHandlesExactEndCursor()
    {
        var file = File("abc");
        var first = LocalFileLiteralFind.Find(file, "abc", LocalFileFindDirection.First)!;
        LocalFileLiteralFind.Find(file, "abc", LocalFileFindDirection.Next, first).Should().BeNull();
        var find = () => LocalFileLiteralFind.Find(file, "abc", (LocalFileFindDirection)99);
        find.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static LocalFileRevision File(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return new(new(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi),
            new(@"C:\Synthetic\source.md", "synthetic", bytes.Length, DateTimeOffset.UnixEpoch)),
            bytes, DateTimeOffset.UnixEpoch);
    }
}