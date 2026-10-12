using System.Text;

using AwesomeAssertions;

using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Context;

public sealed class LocalFileLiteralFindTests
{
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
            "length" => span with { Length = int.MaxValue },
            "negative" => span with { Start = -1 },
            "outside" => span with { Start = int.MaxValue },
            _ => span with { Start = 1 },
        };
        foreach (var direction in new[] { LocalFileFindDirection.Next, LocalFileFindDirection.Previous })
        {
            var find = () => LocalFileLiteralFind.Find(file, "abc", direction, selected);
            find.Should().Throw<InvalidDataException>();
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