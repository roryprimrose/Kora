using AwesomeAssertions;

using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Context;

public sealed class ClipboardLiteralFindTests
{
    [Theory]
    [InlineData("alpha", "missing", 0)]
    [InlineData("alpha", "alpha", 1)]
    [InlineData("alpha ALPHA alpha", "alpha", 2)]
    [InlineData("aaaaaaa", "aaa", 2)]
    [InlineData("aaaaaXaa", "aa", 3)]
    [InlineData("e\u0301 é é", "é", 2)]
    [InlineData("\r\n🙂\r\n🙂", "🙂", 2)]
    [InlineData(" .*?  .*? ", " .*? ", 2)]
    public void CountedFindTraversesOneCompleteNonoverlappingSequenceBothWays(string text, string query, int expected)
    {
        var snapshot = Snapshot(text);
        var result = ClipboardLiteralFind.FindCounted(snapshot, query, SnapshotFindDirection.First);
        result.Count.Should().Be(expected);
        result.Ordinal.Should().Be(expected == 0 ? 0 : 1);
        if (expected == 0) { result.Match.Should().BeNull(); return; }
        var matches = new List<ClipboardLiteralMatch>();
        while (result.Match is not null)
        {
            matches.Add(result.Match);
            result.Match.Source.Should().BeSameAs(snapshot);
            snapshot.Text.Substring(result.Match.Span.Start, result.Match.Span.Length).Should().Be(query);
            result.Count.Should().Be(expected);
            result.Ordinal.Should().Be(matches.Count);
            result = ClipboardLiteralFind.FindCounted(snapshot, query, SnapshotFindDirection.Next, matches[^1]);
        }
        matches.Should().HaveCount(expected);
        result.Ordinal.Should().Be(expected);
        result.Count.Should().Be(expected);
        for (var index = matches.Count - 1; index >= 0; index--)
        {
            result = ClipboardLiteralFind.FindCounted(snapshot, query, SnapshotFindDirection.Previous, matches[index]);
            result.Count.Should().Be(expected);
            result.Ordinal.Should().Be(index == 0 ? 1 : index);
            result.Match.Should().Be(index == 0 ? null : matches[index - 1]);
        }
        ClipboardLiteralFind.FindCounted(snapshot, query, SnapshotFindDirection.First, matches[^1]).Ordinal.Should().Be(1);
    }

    [Fact]
    public void CountedFindUsesCompleteMaximumSnapshotAndAuthoritativeMaximumQuery()
    {
        var snapshot = Snapshot(new('a', ClipboardSnapshot.MaximumUtf8Bytes));
        var result = ClipboardLiteralFind.FindCounted(snapshot, "a", SnapshotFindDirection.First);
        result.Count.Should().Be(ClipboardSnapshot.MaximumUtf8Bytes);
        var query = new string('é', 256);
        ClipboardLiteralFind.FindCounted(Snapshot(query + query), query, SnapshotFindDirection.First).Count.Should().Be(2);
        var supplementary = string.Concat(Enumerable.Repeat("🙂", 128));
        ClipboardLiteralFind.FindCounted(Snapshot(supplementary), supplementary, SnapshotFindDirection.First)
            .Match!.Span.Length.Should().Be(256);
        var nullSnapshot = () => ClipboardLiteralFind.FindCounted(null!, "a", SnapshotFindDirection.First);
        nullSnapshot.Should().Throw<ArgumentNullException>();
        var invalidDirection = () => ClipboardLiteralFind.FindCounted(snapshot, "a", (SnapshotFindDirection)99);
        invalidDirection.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CountedFindRejectsOverlappingAlternativeOutsideCanonicalSequence()
    {
        var snapshot = Snapshot("aaaaaXaa");
        var selected = new ClipboardLiteralMatch(snapshot, new(3, 2));
        foreach (var direction in new[] { SnapshotFindDirection.Next, SnapshotFindDirection.Previous })
        {
            var find = () => ClipboardLiteralFind.FindCounted(snapshot, "aa", direction, selected);
            find.Should().Throw<InvalidDataException>();
        }
    }

    [Fact]
    public void SharedCountedExtractionPreservesClipboardReverseAndFirstCursorSemantics()
    {
        var snapshot = Snapshot("aaaaaXaa");
        var last = new ClipboardLiteralMatch(snapshot, new(6, 2));
        ClipboardLiteralFind.Find(snapshot, "aa", SnapshotFindDirection.Previous, last)!.Span.Start.Should().Be(3);
        ClipboardLiteralFind.FindCounted(snapshot, "aa", SnapshotFindDirection.Previous, last)
            .Match!.Span.Start.Should().Be(2);
        var foreign = new ClipboardLiteralMatch(Snapshot("different"), new(int.MaxValue, int.MaxValue));
        var result = ClipboardLiteralFind.FindCounted(snapshot, "aa", SnapshotFindDirection.First, foreign);
        result.Match!.Source.Should().BeSameAs(snapshot);
        result.Match.Span.Start.Should().Be(0);
        result.Count.Should().Be(3);
        result.Ordinal.Should().Be(1);
    }

    [Theory]
    [InlineData("a A a", "A", 2)]
    [InlineData(" a  a ", " a ", 0)]
    [InlineData("x .*?[a] x", ".*?[a]", 2)]
    [InlineData("e\u0301 é", "é", 3)]
    [InlineData("\r\n🙂\r\n🙂", "🙂", 2)]
    [InlineData(" abc", " ", 0)]
    public void FindPreservesExactCaseSpacesUnicodeAndOriginalOffsets(string text, string query, int start)
    {
        var snapshot = Snapshot(text);
        var match = ClipboardLiteralFind.Find(snapshot, query, SnapshotFindDirection.First)!;
        match.Source.Should().BeSameAs(snapshot);
        match.Span.Should().Be(new SnapshotLiteralSpan(start, query.Length));
        snapshot.Text.Substring(match.Span.Start, match.Span.Length).Should().Be(query);
    }

    [Fact]
    public void FindMovesOneNonoverlappingSpanWithoutWrapOrAllOccurrenceMaterialization()
    {
        var snapshot = Snapshot("aaaaaaa");
        var first = ClipboardLiteralFind.Find(snapshot, "aaa", SnapshotFindDirection.First)!;
        first.Span.Start.Should().Be(0);
        ClipboardLiteralFind.Find(snapshot, "aaa", SnapshotFindDirection.Previous, first).Should().BeNull();
        var next = ClipboardLiteralFind.Find(snapshot, "aaa", SnapshotFindDirection.Next, first)!;
        next.Span.Start.Should().Be(3);
        ClipboardLiteralFind.Find(snapshot, "aaa", SnapshotFindDirection.Next, next).Should().BeNull();
        ClipboardLiteralFind.Find(snapshot, "aaa", SnapshotFindDirection.Previous, next).Should().Be(first);
        ClipboardLiteralFind.Find(snapshot, "aaa", SnapshotFindDirection.First, next).Should().Be(first);
        ClipboardLiteralFind.Find(snapshot, "missing", SnapshotFindDirection.First).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("\0")]
    [InlineData("\t")]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\u0085")]
    [InlineData("\u202e")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void FindRejectsUnsafeQueryWithoutFallback(string query)
    {
        var find = () => ClipboardLiteralFind.Find(Snapshot("source"), query, SnapshotFindDirection.First);
        find.Should().Throw<InvalidDataException>();
        var counted = () => ClipboardLiteralFind.FindCounted(Snapshot("source"), query, SnapshotFindDirection.First);
        counted.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void FindUsesAuthoritativeBoundsAndStrictUnicodeWithoutNormalization()
    {
        SnapshotLiteralFind.IsValidQuery(new('x', 256)).Should().BeTrue();
        SnapshotLiteralFind.IsValidQuery(new('x', 257)).Should().BeFalse();
        SnapshotLiteralFind.IsValidQuery(new('é', 256)).Should().BeTrue();
        SnapshotLiteralFind.IsValidQuery(new string('界', 170) + "é").Should().BeTrue();
        SnapshotLiteralFind.IsValidQuery(new('界', 171)).Should().BeFalse();
        SnapshotLiteralFind.IsValidQuery(string.Concat(Enumerable.Repeat("🙂", 128))).Should().BeTrue();
        SnapshotLiteralFind.IsValidQuery(new string('\ud800', 1)).Should().BeFalse();
        SnapshotLiteralFind.IsValidQuery(new string('\udc00', 1)).Should().BeFalse();
        foreach (var invalidQuery in new[] { new string('x', 257), new string('界', 171), new string('\ud800', 1), new string('\udc00', 1) })
        {
            var counted = () => ClipboardLiteralFind.FindCounted(Snapshot("source"), invalidQuery, SnapshotFindDirection.First);
            counted.Should().Throw<InvalidDataException>();
        }
        var nullText = () => SnapshotLiteralFind.Find(null!, "a", SnapshotFindDirection.First);
        nullText.Should().Throw<ArgumentNullException>();
        var nullSnapshot = () => ClipboardLiteralFind.Find(null!, "a", SnapshotFindDirection.First);
        nullSnapshot.Should().Throw<ArgumentNullException>();
        var invalidDirection = () => ClipboardLiteralFind.Find(Snapshot("a"), "a", (SnapshotFindDirection)99);
        invalidDirection.Should().Throw<ArgumentOutOfRangeException>();
        var query = new string('é', 256);
        ClipboardLiteralFind.Find(Snapshot(query), query, SnapshotFindDirection.First)!.Span.Length.Should().Be(256);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("same-ids")]
    [InlineData("version")]
    [InlineData("length")]
    [InlineData("negative")]
    [InlineData("outside")]
    [InlineData("text")]
    public void FindRejectsForgedOffsetsOrDifferentCaptureEvenWithReusedIds(string invalid)
    {
        var snapshot = Snapshot("abc abc");
        var match = new ClipboardLiteralMatch(snapshot, new(0, 3));
        var selected = invalid switch
        {
            "missing" => null,
            "foreign" => match with { Source = Snapshot("abc abc") },
            "same-ids" => match with { Source = new(snapshot.SourceId, snapshot.SnapshotId, snapshot.Request,
                snapshot.Text, snapshot.Version, snapshot.CapturedAt) },
            "version" => match with { Source = new(snapshot.SourceId, snapshot.SnapshotId, snapshot.Request,
                snapshot.Text, snapshot.Version + 1, snapshot.CapturedAt) },
            "length" => match with { Span = new(0, int.MaxValue) },
            "negative" => match with { Span = new(-1, 3) },
            "outside" => match with { Span = new(int.MaxValue, 3) },
            _ => match with { Span = new(1, 3) },
        };
        foreach (var direction in new[] { SnapshotFindDirection.Next, SnapshotFindDirection.Previous })
        {
            var find = () => ClipboardLiteralFind.Find(snapshot, "abc", direction, selected);
            find.Should().Throw<InvalidDataException>();
            var counted = () => ClipboardLiteralFind.FindCounted(snapshot, "abc", direction, selected);
            counted.Should().Throw<InvalidDataException>();
        }
    }

    private static ClipboardSnapshot Snapshot(string text) => new(Guid.NewGuid(), Guid.NewGuid(),
        HostRequest.Create(RequestOrigin.LocalUi), text, 1, DateTimeOffset.UnixEpoch);
}
