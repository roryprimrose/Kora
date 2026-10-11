using System.Globalization;
using System.Text;
using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Context;

public sealed class LocalFileMarkdownRetrievalTests
{
    private readonly LocalFileLexicalRetrieval engine = new();
    private static LocalFileRevision Revision(string text, string extension = ".md", bool bom = false)
    {
        byte[] bytes = [.. bom ? new byte[] { 0xef, 0xbb, 0xbf } : [], .. Encoding.UTF8.GetBytes(text)];
        return new(new(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi),
            new(@"C:\Team\guide" + extension, "exact", bytes.Length, DateTimeOffset.UnixEpoch)), bytes, DateTimeOffset.UnixEpoch);
    }
    private LocalFileSearchResult Search(LocalFileRevision source, string query) =>
        engine.Search(source, source.Reference, query, LocalFileRetrievalMode.MarkdownStructure,
            DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public void SearchReturnsCompleteGoldenSectionsWithOriginalHeadingAndCitationPositions(string newline)
    {
        var first = "intro café 🙂" + newline + newline;
        var section = "   ## Authored" + newline + "- alpha" + newline + "| beta |" + newline
            + "```cs" + newline + "# false heading" + newline + "alpha" + newline + "```" + newline
            + "![authored alt](https://invalid/image)" + newline + newline;
        var last = "Setext title" + newline + "===" + newline + "[alpha](https://invalid/link)";
        var source = Revision(first + section + last, bom: true);
        var result = Search(source, "alpha beta");
        var expected = new LocalFileSearchResult(LocalFileSearchOutcome.Matched, DateTimeOffset.UnixEpoch,
            [
                new(source.Reference, "guide.md", first.Length, section.Length, 3, 1, 12, 1,
                    "## Authored", section, 2, 3)
                { HeadingContext = new(first.Length + 3, 11, 3, 4, "## Authored") },
                new(source.Reference, "guide.md", first.Length + section.Length, last.Length, 12, 1, 14, 30,
                    "Setext title", last, 1, 1)
                { HeadingContext = new(first.Length + section.Length, 12, 12, 1, "Setext title") },
            ], 2, false) { Mode = LocalFileRetrievalMode.MarkdownStructure, PolicyVersion = "markdown-structure-v2" };
        result.Should().BeEquivalentTo(expected, options => options.WithStrictOrdering());
        source.Digest.Should().Be(LocalFilePolicy.Digest([0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(source.Text)]));
        Search(source, "authored alt false").Citations.Should().ContainSingle().Which.Excerpt.Should().Be(section);
    }

    [Theory]
    [InlineData("```lang", "```")]
    [InlineData("````lang", "````` \t")]
    [InlineData("~~~lang`inert", "~~~~")]
    [InlineData("   ~~~~", "  ~~~~")]
    public void SearchKeepsWholeSmallFenceAndNeverPromotesItsHeadingsOrShortWrongClosers(string opener, string closer)
    {
        var text = "# Real\n" + opener + "\n\n# fake\nSetext fake\n===\n``\n~~\n~~~x\nalpha\n"
            + closer + "\n\n## Next\nbeta";
        var source = Revision(text);
        var result = Search(source, "alpha beta");
        result.Citations.Should().HaveCount(2);
        result.Citations[0].Heading.Should().Be("# Real");
        result.Citations[0].Excerpt.Should().Be(text[..text.IndexOf("## Next", StringComparison.Ordinal)]);
        result.Citations[0].IsStructuralFragment.Should().BeFalse();
        result.Citations[1].Heading.Should().Be("## Next");
        result.Citations[1].StartLine.Should().Be(13);
    }

    [Theory]
    [InlineData("````\nalpha\n```\n# fake\n", "# Real")]
    [InlineData("~~~\nalpha\n~~~notclose\n## fake\n", "# Real")]
    [InlineData("```lang`invalid\nalpha\n", "# Real")]
    [InlineData("    ```\nalpha\n", "# Real")]
    [InlineData("`unclosed\nalpha\n# fake\n", "# Real")]
    [InlineData("<div>\nalpha\n# fake\n", "# Real")]
    public void SearchConservativelyBoundsUnclosedFormatsAndDoesNotInventSections(string body, string heading)
    {
        var source = Revision("# Real\n" + body);
        var result = Search(source, "alpha");
        result.Citations.Should().ContainSingle().Which.Heading.Should().Be(heading);
        result.Citations[0].Excerpt.Should().Be(source.Text);
        result.Citations[0].IsStructuralFragment.Should().BeTrue();
    }

    [Theory]
    [InlineData("#", "#")]
    [InlineData("######", "######")]
    [InlineData("##\tLabel ###", "##\tLabel ###")]
    [InlineData("####### no", null)]
    [InlineData("#no", null)]
    [InlineData("    # no", null)]
    [InlineData("\t# no", null)]
    [InlineData("- list\n  # no", null)]
    [InlineData("1. list\n  # no", null)]
    [InlineData("1) list\n  # no", null)]
    [InlineData("> # no", null)]
    public void SearchRecognizesOnlyDocumentedTopLevelAtxSubset(string heading, string? label)
    {
        var result = Search(Revision(heading + "\nalpha"), "alpha");
        result.Citations.Should().ContainSingle().Which.Heading.Should().Be(label);
    }

    [Theory]
    [InlineData("Title\n---\nalpha", "Title")]
    [InlineData("   Title\n  === \t\nalpha", "Title")]
    [InlineData("Title\n- - -\nalpha", null)]
    [InlineData("Title\n====x\nalpha", null)]
    [InlineData("Title\n    ===\nalpha", null)]
    [InlineData("Title\n\n===\nalpha", null)]
    [InlineData("paragraph\nTitle\n===\nalpha", null)]
    [InlineData("- list\n===\nalpha", null)]
    [InlineData("+ list\n===\nalpha", null)]
    [InlineData("* list\n===\nalpha", null)]
    [InlineData("Title | table\n---\nalpha", null)]
    [InlineData("`inline`\n===\nalpha", null)]
    [InlineData("---\n---\nalpha", null)]
    public void SearchRecognizesOnlyStandaloneSetextOutsideOpaqueFormatting(string text, string? heading) =>
        Search(Revision(text), "alpha").Citations.Single().Heading.Should().Be(heading);

    [Fact]
    public void SearchRetainsSmallSectionsButFragmentsOversizeCodeAndParagraphsWithoutFalseWholeBlockClaims()
    {
        var smallCode = "~~~\nalpha\n\n# inert\n~~~\n";
        var source = Revision("# real\n\n" + smallCode + "\n" + new string('x', 2050) + "\n\nalpha tail");
        var result = Search(source, "alpha");
        result.MatchingChunks.Should().Be(2);
        result.Citations.Should().HaveCount(2);
        result.Citations[0].Excerpt.Should().Be(smallCode);
        result.Citations[1].Excerpt.Should().Be("alpha tail");
        result.Citations.Should().OnlyContain(citation => citation.IsStructuralFragment && citation.Heading == "# real");
        var code = "```\n" + string.Concat(Enumerable.Repeat("alpha\n", 128)) + "```\n";
        var fragmented = Search(Revision(code), "alpha");
        fragmented.MatchingChunks.Should().Be(2);
        fragmented.Citations.Select(citation => citation.Excerpt).Should().Equal(
            "```\n" + string.Concat(Enumerable.Repeat("alpha\n", 127)), "alpha\n```\n");
        fragmented.Citations.Should().OnlyContain(citation => citation.IsStructuralFragment && citation.Heading == null);
        var paragraph = string.Concat(Enumerable.Repeat("alpha\n", 100)) + new string('x', 1447) + "\n";
        paragraph.Length.Should().Be(2048);
        Search(Revision("# real\n\n" + paragraph + "\n"), "alpha").Citations.Single().Excerpt.Should().Be(paragraph);
        var exactCode = "```\nalpha " + new string('x', 2033) + "\n```\n";
        exactCode.Length.Should().Be(2048);
        Search(Revision("# real\n\n" + exactCode + "\n"), "alpha").Citations.Single().Excerpt.Should().Be(exactCode);
    }

    [Fact]
    public void SearchEnforcesExactChunkLineAndGlobalByteThresholdsWithoutOverlapOrDuplicateCounts()
    {
        var exact = "# a\nalpha " + new string('x', 2038);
        exact.Length.Should().Be(2048);
        var result = Search(Revision(exact), "alpha");
        result.Citations.Single().Length.Should().Be(2048);
        result.Citations.Single().IsStructuralFragment.Should().BeFalse();
        Search(Revision(exact + "x"), "alpha").Citations.Single().IsStructuralFragment.Should().BeTrue();
        Search(Revision(string.Concat(Enumerable.Repeat("alpha\n", 128))), "alpha").Citations.Should().ContainSingle();
        Search(Revision(string.Concat(Enumerable.Repeat("alpha\n", 129))), "alpha").Citations.Should().HaveCount(2);
        var chunk = "# a\nalpha " + new string('x', 2037) + "\n";
        chunk.Length.Should().Be(2048);
        var eight = Search(Revision(string.Concat(Enumerable.Repeat(chunk, 8))), "alpha");
        eight.MatchingChunks.Should().Be(8);
        eight.Citations.Should().HaveCount(8);
        eight.Citations.Sum(citation => Encoding.UTF8.GetByteCount(citation.Excerpt)).Should().Be(16384);
        eight.Truncated.Should().BeFalse();
        var ninth = Search(Revision(string.Concat(Enumerable.Repeat(chunk, 9))), "alpha");
        ninth.MatchingChunks.Should().Be(9);
        ninth.Citations.Should().Equal(eight.Citations.Select(citation => citation with { Source = ninth.Citations[0].Source }));
        ninth.Truncated.Should().BeTrue();
        var multibyte = "# a\nalpha " + new string('日', 1900) + "\n";
        var bytes = Search(Revision(string.Concat(Enumerable.Repeat(multibyte, 8))), "alpha");
        bytes.MatchingChunks.Should().Be(8);
        bytes.Citations.Should().HaveCount(2);
        bytes.Citations.Sum(citation => Encoding.UTF8.GetByteCount(citation.Excerpt)).Should().Be(11422);
        bytes.Truncated.Should().BeTrue();
    }

    [Theory]
    [InlineData("alpha", "a")]
    [InlineData("alpha", "alpha")]
    [InlineData("alpha", "\U00010400alpha")]
    [InlineData("alpha", "\u0301alpha")]
    [InlineData("alpha", "\u0903alpha")]
    [InlineData("alpha", "\u20ddalpha")]
    public void SearchNeverInventsTokenPrefixesOrSuffixesAcrossForcedCuts(string query, string suffix)
    {
        var source = Revision("```\n" + new string('a', 2048) + suffix + "\n```");
        Search(source, query).Outcome.Should().Be(LocalFileSearchOutcome.NoMatch);
        Search(Revision("alpha" + new string('a', 3000)), query).Outcome.Should().Be(LocalFileSearchOutcome.NoMatch);
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("🙂")]
    [InlineData("🙂alpha")]
    [InlineData("\U00010400")]
    [InlineData(" cafe\u0301")]
    public void SearchPreservesScalarCrLfAndExactOriginalSlicesAtLongLineCuts(string middle)
    {
        var text = "```\n" + new string('.', 2047) + middle + " alpha\n```\n";
        var source = Revision(text);
        var result = Search(source, "alpha");
        result.Citations.Should().NotBeEmpty();
        foreach (var citation in result.Citations)
        {
            citation.Length.Should().BeLessThanOrEqualTo(2048);
            citation.Excerpt.Should().Be(text.Substring(citation.Start, citation.Length));
            _ = new UTF8Encoding(false, true).GetByteCount(citation.Excerpt);
            if (citation.Start > 0) { (text[citation.Start - 1] == '\r' && text[citation.Start] == '\n').Should().BeFalse(); }
            citation.IsStructuralFragment.Should().BeTrue();
        }
    }

    [Fact]
    public void SearchUsesCanonicalTermsCultureFrequencyAndCompleteQueryLimitsInBothPolicies()
    {
        var source = Revision("# first\nCAFÉ I alpha alpha\n\n## second\nalpha beta");
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Search(source, "cafe\u0301 i").Citations.Should().Equal(Search(source, "CAFÉ I I").Citations);
        }
        finally { CultureInfo.CurrentCulture = old; }
        var rank = Search(source, "beta alpha alpha");
        rank.Citations.Select(citation => citation.Heading).Should().Equal("## second", "# first");
        rank.Citations.Select(citation => citation.BoundedFrequency).Should().Equal(2, 2);
        Search(Revision("alpha " + string.Concat(Enumerable.Repeat("alpha ", 100))), "alpha").Citations.Single()
            .BoundedFrequency.Should().Be(16);
        var atChars = string.Join(' ', new string('a', 64), new string('b', 64), new string('c', 64), new string('d', 61));
        atChars.Length.Should().Be(256);
        Search(Revision(new string('a', 64)), atChars).Outcome.Should().Be(LocalFileSearchOutcome.Matched);
        var atBytes = new string('日', 64) + " " + new string('日', 64) + " " + new string('日', 42);
        Encoding.UTF8.GetByteCount(atBytes).Should().Be(512);
        Search(Revision(new string('日', 64)), atBytes).Outcome.Should().Be(LocalFileSearchOutcome.Matched);
        foreach (var invalid in new[] { atChars + "x", atBytes + "x", "", " ", "\ud800", new string('a', 65), string.Join(' ', Enumerable.Range(0, 33)) })
        {
            Search(source, invalid).Outcome.Should().Be(LocalFileSearchOutcome.InvalidQuery);
        }
        Search(source, string.Join(' ', Enumerable.Range(0, 32))).Outcome.Should().Be(LocalFileSearchOutcome.NoMatch);
        Search(Revision(""), "alpha").Should().BeEquivalentTo(LocalFileSearchResult.Empty(
            LocalFileSearchOutcome.NoMatch, DateTimeOffset.UnixEpoch, LocalFileRetrievalMode.MarkdownStructure));
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".md")]
    [InlineData(".MARKDOWN")]
    public void SearchUsesExplicitFormatPolicyWhileDefaultRemainsByteForByteOriginal(string extension)
    {
        var source = Revision("Title\n===\nalpha\n\n```\n# fake\nalpha\n```", extension);
        var legacy = engine.Search(source, source.Reference, "alpha", DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);
        legacy.Citations.Should().HaveCount(2);
        legacy.Citations.Select(citation => citation.Heading).Should().Equal(null, "# fake");
        engine.Search(source, source.Reference, "alpha", LocalFileRetrievalMode.LexicalLines, DateTimeOffset.UnixEpoch,
            TestContext.Current.CancellationToken).Should().BeEquivalentTo(legacy);
        var selected = Search(source, "alpha");
        selected.PolicyVersion.Should().Be("markdown-structure-v2");
        if (string.Equals(extension, ".txt", StringComparison.Ordinal)) { selected.Citations.Should().Equal(legacy.Citations); }
        else { selected.Citations.Should().ContainSingle().Which.Heading.Should().Be("Title"); }
        var invalid = () => Revision("alpha", ".html");
        invalid.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void SearchRejectsUnknownModesExactReferenceMismatchAndCancellationWithoutFallback()
    {
        var source = Revision("alpha");
        engine.Search(source, source.Reference, "alpha", (LocalFileRetrievalMode)99, DateTimeOffset.UnixEpoch,
            TestContext.Current.CancellationToken).Should().BeEquivalentTo(
                LocalFileSearchResult.Empty(LocalFileSearchOutcome.Denied, DateTimeOffset.UnixEpoch, (LocalFileRetrievalMode)99));
        engine.Search(source, source.Reference with { Digest = "other" }, "alpha", LocalFileRetrievalMode.MarkdownStructure,
            DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken).Outcome.Should().Be(LocalFileSearchOutcome.Stale);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var run = () => engine.Search(source, source.Reference, "alpha", LocalFileRetrievalMode.MarkdownStructure,
            DateTimeOffset.UnixEpoch, cancellation.Token);
        run.Should().Throw<OperationCanceledException>();
        var folder = LocalFolderTests.Revision("# a\nalpha", "# b\nalpha beta", "# c\nalpha");
        var ranked = engine.Search(folder, folder.Reference, "alpha beta", LocalFileRetrievalMode.MarkdownStructure,
            DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);
        ranked.MatchingChunks.Should().Be(3);
        ranked.Citations.Select(citation => citation.DisplayIdentity).Should().Equal("01.md", "00.md", "02.md");
        var many = LocalFolderTests.Revision(Enumerable.Repeat("# a\nalpha\n# b\nalpha\n", 10).ToArray());
        var global = engine.Search(many, many.Reference, "alpha", LocalFileRetrievalMode.MarkdownStructure,
            DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);
        global.MatchingChunks.Should().Be(20);
        global.Citations.Select(citation => citation.DisplayIdentity).Should().Equal("00.md", "00.md", "01.md", "01.md", "02.md", "02.md", "03.md", "03.md");
        global.Truncated.Should().BeTrue();
        engine.Search(folder, folder.Reference, "alpha", (LocalFileRetrievalMode)99, DateTimeOffset.UnixEpoch,
            TestContext.Current.CancellationToken).Outcome.Should().Be(LocalFileSearchOutcome.Denied);
        engine.Search(folder, folder.Reference with { RevisionId = Guid.NewGuid() }, "alpha", LocalFileRetrievalMode.MarkdownStructure,
            DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken).Outcome.Should().Be(LocalFileSearchOutcome.Stale);
    }

    [Fact]
    public void LegacyImplementationsSupportOnlyExplicitV1AndDenySelectedOrUnknownModesWithoutFallback()
    {
        ILocalFileRetrieval legacy = new LegacyRetrieval();
        var file = Revision("alpha");
        var folder = LocalFolderTests.Revision("alpha");
        legacy.Search(file, file.Reference, "alpha", LocalFileRetrievalMode.LexicalLines, DateTimeOffset.UnixEpoch,
            TestContext.Current.CancellationToken).PolicyVersion.Should().Be("lexical-lines-v1");
        legacy.Search(folder, folder.Reference, "alpha", LocalFileRetrievalMode.LexicalLines, DateTimeOffset.UnixEpoch,
            TestContext.Current.CancellationToken).PolicyVersion.Should().Be("lexical-lines-v1");
        foreach (var mode in new[] { LocalFileRetrievalMode.MarkdownStructure, (LocalFileRetrievalMode)99 })
        {
            legacy.Search(file, file.Reference, "alpha", mode, DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken)
                .Outcome.Should().Be(LocalFileSearchOutcome.Denied);
            legacy.Search(folder, folder.Reference, "alpha", mode, DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken)
                .Outcome.Should().Be(LocalFileSearchOutcome.Denied);
        }
    }

    [Theory]
    [InlineData("-")]
    [InlineData("+")]
    [InlineData("*")]
    [InlineData("1")]
    [InlineData("1? text")]
    [InlineData("1.x")]
    [InlineData("1)x")]
    [InlineData("1234567890. text")]
    [InlineData("1.\tlist")]
    [InlineData("-\tlist")]
    [InlineData("*x")]
    [InlineData("+x")]
    [InlineData("`closed`")]
    [InlineData("\\`escaped")]
    public void SearchKeepsMalformedContainerAndEscapedInlineMarkersAsExactInertText(string body)
    {
        var source = Revision("# Real\n" + body + "\nalpha");
        Search(source, "alpha").Citations.Single().Excerpt.Should().Be(source.Text);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void SearchBoundsLongFencesAcrossEveryNewlineAndTerminalCarriageReturn(string newline)
    {
        var source = Revision("```" + newline + string.Concat(Enumerable.Repeat("alpha" + newline, 128))
            + "    ```" + newline + "```" + newline);
        var result = Search(source, "alpha");
        result.MatchingChunks.Should().Be(2);
        result.Citations.Should().OnlyContain(citation => citation.IsStructuralFragment
            && citation.Length <= 2048 && citation.Excerpt == source.Text.Substring(citation.Start, citation.Length));
        Search(Revision("alpha\r"), "alpha").Citations.Single().EndLine.Should().Be(2);
        Search(Revision("alpha\rnext"), "alpha").Citations.Single().EndLine.Should().Be(2);
    }

    [Theory]
    [InlineData("# ")]
    [InlineData("Title ")]
    public void SearchBoundsAuthoredHeadingLabelsWithoutSplittingSurrogates(string prefix)
    {
        var heading = prefix + new string('x', 63 - prefix.Length) + "🙂tail";
        var source = Revision(heading + (prefix[0] == '#' ? "\n" : "\n===\n") + "alpha");
        var context = Search(source, "alpha").Citations.Single().HeadingContext!;
        context.Should().Be(new LocalFileHeadingContext(0, 63, 1, 1, heading[..63]));
        var longHeading = prefix + new string('x', 100);
        Search(Revision(longHeading + (prefix[0] == '#' ? "\n" : "\n===\n") + "alpha"), "alpha")
            .Citations.Single().HeadingContext!.Length.Should().Be(64);
    }

    private sealed class LegacyRetrieval : ILocalFileRetrieval
    {
        public LocalFileSearchResult Search(LocalFileRevision revision, LocalFileReference exactSource, string query,
            DateTimeOffset observedAt, CancellationToken cancellationToken) =>
            new LocalFileLexicalRetrieval().Search(revision, exactSource, query, observedAt, cancellationToken);
        public LocalFileSearchResult Search(LocalFolderRevision revision, LocalFolderReference exactSource, string query,
            DateTimeOffset observedAt, CancellationToken cancellationToken) =>
            new LocalFileLexicalRetrieval().Search(revision, exactSource, query, observedAt, cancellationToken);
    }

    [Fact]
    public void SearchMixedFolderFormatsUsesOneCanonicalRankingAndExactPlainVersusMarkdownPolicy()
    {
        var texts = new[] { "# plain\nTitle\n===\nalpha\n\nalpha", "# Real\n~~~\n# fake\nalpha beta\n~~~\n" };
        var bytes = texts.Select(Encoding.UTF8.GetBytes).ToArray();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var metadata = new LocalFolderMetadata(@"C:\Team", "folder",
            [
                new(@"C:\Team\00.txt", "plain", bytes[0].Length, DateTimeOffset.UnixEpoch),
                new(@"C:\Team\01.MARKDOWN", "markdown", bytes[1].Length, DateTimeOffset.UnixEpoch),
            ]);
        var review = new LocalFolderReview(Guid.NewGuid(), Guid.NewGuid(), request, metadata);
        var files = bytes.Select((content, index) => new LocalFileRevision(
            new(review.ReviewId, review.SourceId, request, metadata.Files[index]), content, DateTimeOffset.UnixEpoch)).ToArray();
        var folder = new LocalFolderRevision(review, files);
        var result = engine.Search(folder, folder.Reference, "alpha beta", LocalFileRetrievalMode.MarkdownStructure,
            DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);
        result.MatchingChunks.Should().Be(3);
        result.Citations.Select(citation => citation.DisplayIdentity).Should().Equal("01.MARKDOWN", "00.txt", "00.txt");
        result.Citations[0].Should().BeEquivalentTo(new LocalFileCitation(files[1].Reference, "01.MARKDOWN",
            0, texts[1].Length, 1, 1, 6, 1, "# Real", texts[1], 2, 2)
            { HeadingContext = new(0, 6, 1, 1, "# Real") });
        var legacy = engine.Search(files[0], files[0].Reference, "alpha beta", DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);
        result.Citations.Skip(1).Should().Equal(legacy.Citations);
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public void SearchMaximumAdmittedSourceCountsEverySectionButKeepsOnlyGlobalTopEight()
    {
        var text = string.Concat(Enumerable.Repeat("# a\n", LocalFilePolicy.MaximumBytes / 4));
        var result = Search(Revision(text), "a");
        result.MatchingChunks.Should().Be(65536);
        result.Citations.Select(citation => citation.Start).Should().Equal(0, 4, 8, 12, 16, 20, 24, 28);
        result.Citations.Select(citation => citation.StartLine).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
        result.Citations.Should().OnlyContain(citation => citation.Length == 4 && citation.Excerpt == "# a\n" && !citation.IsStructuralFragment);
        result.Truncated.Should().BeTrue();
        var word = new string('a', LocalFilePolicy.MaximumBytes - 5) + "alpha";
        Search(Revision(word), "alpha").Outcome.Should().Be(LocalFileSearchOutcome.NoMatch);
    }
}
