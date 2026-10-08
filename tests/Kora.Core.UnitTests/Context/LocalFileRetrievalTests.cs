using System.Globalization;
using System.Text;
using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Context;

public sealed class LocalFileRetrievalTests
{
    private readonly LocalFileLexicalRetrieval retrieval = new();
    private static LocalFileRevision Revision(string text, bool bom = false)
    {
        byte[] bytes = [.. bom ? new byte[] { 0xef, 0xbb, 0xbf } : Array.Empty<byte>(), .. Encoding.UTF8.GetBytes(text)];
        return new(new(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi),
            new(@"C:\Team\guide.md", "exact-file", bytes.Length, DateTimeOffset.UnixEpoch)), bytes, DateTimeOffset.UnixEpoch);
    }
    private LocalFileSearchResult Search(LocalFileRevision revision, string query) =>
        retrieval.Search(revision, revision.Reference, query, DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void Heading_paragraph_locations_and_BOM_digest_are_exact(string newline)
    {
        var source = Revision("# First" + newline + "alpha café 日本語" + newline + newline
            + "## Second" + newline + "alpha beta 🙂" + newline, bom: true);
        var result = Search(source, "ALPHA beta café");
        result.Outcome.Should().Be(LocalFileSearchOutcome.Matched);
        result.ObservedAt.Should().Be(DateTimeOffset.UnixEpoch);
        result.PolicyVersion.Should().Be("lexical-lines-v1");
        result.MatchingChunks.Should().Be(2);
        result.Citations.Count.Should().Be(2);
        result.Citations[0].Heading.Should().Be("# First");
        result.Citations[0].StartLine.Should().Be(1);
        result.Citations[0].EndLine.Should().Be(4);
        result.Citations[1].StartLine.Should().Be(4);
        result.Citations[1].EndLine.Should().Be(6);
        result.Citations.Should().OnlyContain(citation => citation.StartColumn == 1 && citation.EndColumn == 1
            && citation.Source == source.Reference && citation.DisplayIdentity == "guide.md"
            && citation.Excerpt == source.Text.Substring(citation.Start, citation.Length));
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public void Ranking_deduplicates_query_terms_and_is_stable_across_culture_and_canonical_Unicode()
    {
        var source = Revision("alpha alpha\n\nalpha beta\n\nalpha\n\nCAFÉ I\n");
        var once = Search(source, "alpha beta");
        Search(source, "beta alpha alpha ALPHA").Citations.Should().Equal(once.Citations);
        once.Citations[0].MatchedTerms.Should().Be(2);
        once.Citations[1].BoundedFrequency.Should().Be(2);
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Search(source, "cafe\u0301 i").Citations.Should().Equal(Search(source, "CAFÉ I").Citations);
        }
        finally { CultureInfo.CurrentCulture = old; }
        Search(Revision(string.Join(' ', Enumerable.Repeat("alpha", 200))), "alpha").Citations.Single()
            .BoundedFrequency.Should().Be(LocalFileRetrievalPolicy.MaximumTermFrequency);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    [InlineData("💥💥")]
    [InlineData("\ud800")]
    public void Degenerate_and_malformed_query_is_explicitly_invalid(string query) =>
        Search(Revision("alpha"), query).Outcome.Should().Be(LocalFileSearchOutcome.InvalidQuery);

    [Fact]
    public void Input_bounds_do_not_truncate_and_empty_or_absent_terms_are_no_match()
    {
        var source = Revision("alpha");
        Search(source, new string('a', 257)).Outcome.Should().Be(LocalFileSearchOutcome.InvalidQuery);
        Search(source, new string('日', 171)).Outcome.Should().Be(LocalFileSearchOutcome.InvalidQuery);
        Search(source, new string('\ud800', 1)).Outcome.Should().Be(LocalFileSearchOutcome.InvalidQuery);
        Search(source, new string('a', 65)).Outcome.Should().Be(LocalFileSearchOutcome.InvalidQuery);
        Search(source, string.Join(' ', Enumerable.Range(0, 33))).Outcome.Should().Be(LocalFileSearchOutcome.InvalidQuery);
        Search(source, string.Join(' ', Enumerable.Range(0, 32))).Outcome.Should().Be(LocalFileSearchOutcome.NoMatch);
        Search(source, new string('a', 64)).Outcome.Should().Be(LocalFileSearchOutcome.NoMatch);
        Search(Revision(""), "alpha").Outcome.Should().Be(LocalFileSearchOutcome.NoMatch);
        Search(source, "bet").Citations.Should().BeEmpty();
        Search(source, "bet").Truncated.Should().BeFalse();
    }

    [Fact]
    public void Heading_without_blank_line_flushes_previous_chunk_and_plain_end_has_exclusive_column()
    {
        var source = Revision("intro\n### Third\nalpha");
        var result = Search(source, "intro alpha");
        result.Citations.Count.Should().Be(2);
        result.Citations[0].Heading.Should().BeNull();
        result.Citations[1].Heading.Should().Be("### Third");
        result.Citations[1].EndLine.Should().Be(3);
        result.Citations[1].EndColumn.Should().Be(6);
        Search(Revision("####### not heading\nalpha"), "alpha").Citations.Single().Heading.Should().BeNull();
        Search(Revision("#\nalpha"), "alpha").Citations.Single().Heading.Should().BeNull();
        Search(Revision("#x\nalpha"), "alpha").Citations.Single().Heading.Should().BeNull();
        Search(Revision("##\tHeading\nalpha"), "alpha").Citations.Single().Heading.Should().Be("##\tHeading");
    }

    [Fact]
    public void All_output_limits_are_deterministic_without_claiming_complete_results()
    {
        var source = Revision(string.Concat(Enumerable.Repeat("alpha " + new string('é', 1900) + "\n\n", 20)));
        var result = Search(source, "alpha");
        result.Truncated.Should().BeTrue();
        result.MatchingChunks.Should().Be(20);
        result.Citations.Count.Should().BeLessThanOrEqualTo(8);
        result.Citations.Sum(citation => Encoding.UTF8.GetByteCount(citation.Excerpt)).Should().BeLessThanOrEqualTo(16 * 1024);
        result.Citations.Should().OnlyContain(citation => citation.Length <= 2048);
        Search(source, "alpha").Citations.Should().Equal(result.Citations);
        var small = Search(Revision(string.Concat(Enumerable.Repeat("alpha\n\n", 20))), "alpha");
        small.Citations.Count.Should().Be(8);
        small.MatchingChunks.Should().Be(20);
        small.Truncated.Should().BeTrue();
        Search(Revision(string.Concat(Enumerable.Repeat("alpha\n", 129))), "alpha").Citations.Count.Should().Be(2);
    }

    [Fact]
    public void Long_lines_split_at_safe_boundaries_and_never_match_partial_words()
    {
        var texts = new[]
        {
            new string('a', 2040) + " alpha\nbeta",
            new string('a', 2047) + "\r\nalpha",
            new string('a', 2047) + "🙂 alpha",
            new string('a', 2048) + "alpha",
            string.Concat(Enumerable.Repeat("alpha ", 700)),
            "intro\n" + string.Concat(Enumerable.Repeat("alpha ", 700)),
            new string('a', 2047) + "\u0301 alpha",
            new string('a', 2047) + "\u0903 alpha",
            new string('a', 2047) + "\u20dd alpha",
        };
        foreach (var text in texts)
        {
            var source = Revision(text);
            var result = Search(source, "alpha");
            foreach (var citation in result.Citations)
            {
                citation.Excerpt.Should().Be(text.Substring(citation.Start, citation.Length));
                _ = new UTF8Encoding(false, true).GetByteCount(citation.Excerpt);
                citation.Length.Should().BeLessThanOrEqualTo(2048);
            }
        }
        Search(Revision(new string('a', 2048) + "alpha"), "alpha").Outcome.Should().Be(LocalFileSearchOutcome.NoMatch);
        Search(Revision("alpha" + new string('a', 2048)), "alpha").Outcome.Should().Be(LocalFileSearchOutcome.NoMatch);
        var heading = "# " + new string('x', 61) + "🙂";
        Search(Revision(heading + "\nalpha"), "alpha").Citations.Single().Heading.Should().Be("# " + new string('x', 61));
        Search(Revision("# " + new string('x', 80) + "\nalpha"), "alpha").Citations.Single().Heading!.Length.Should().Be(64);
    }

    [Fact]
    public void Exact_source_revision_item_and_digest_cannot_widen_search_and_cancellation_throws()
    {
        var source = Revision("alpha");
        foreach (var reference in new[]
        {
            source.Reference with { SourceId = Guid.NewGuid() }, source.Reference with { RevisionId = Guid.NewGuid() },
            source.Reference with { ItemId = Guid.NewGuid() }, source.Reference with { Digest = new string('0', 64) },
        })
        {
            retrieval.Search(source, reference, "alpha", DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken)
                .Outcome.Should().Be(LocalFileSearchOutcome.Stale);
        }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var search = () => retrieval.Search(source, source.Reference, "alpha", DateTimeOffset.UnixEpoch, cancelled.Token);
        search.Should().Throw<OperationCanceledException>();
    }
}
