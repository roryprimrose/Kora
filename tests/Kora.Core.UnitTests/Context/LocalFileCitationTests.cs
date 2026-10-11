using System.Text;
using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Context;

public sealed class LocalFileCitationTests
{
    [Theory]
    [InlineData(LocalFileRetrievalMode.LexicalLines, ".md")]
    [InlineData(LocalFileRetrievalMode.MarkdownStructure, ".md")]
    [InlineData(LocalFileRetrievalMode.MarkdownStructure, ".txt")]
    public void ResolveCitationUsesOriginalBomDigestAndExactUnicodeCrLfSourceForEveryReturnedChunk(
        LocalFileRetrievalMode mode, string extension)
    {
        var text = "intro e\u0301 🙂\r\n\r\n## Authored ###\r\nalpha café\r\n\r\n"
            + "Setext title\r\n===\r\n```cs\r\n# code heading\r\nalpha\r\n```\r\n"
            + "![alt alpha](https://invalid/image)\r\n\r\n"
            + string.Join("\r\n\r\n", Enumerable.Repeat("alpha " + new string('x', 2100), 10));
        var file = Revision(text, extension);
        var result = new LocalFileLexicalRetrieval().Search(file, file.Reference, "alpha",
            mode, DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);
        result.Truncated.Should().BeTrue();
        result.Citations.Should().HaveCount(LocalFileRetrievalPolicy.MaximumCitations);
        foreach (var citation in result.Citations)
        {
            var resolved = LocalFileLexicalRetrieval.ResolveCitation(file, citation, mode);
            resolved.File.Should().BeSameAs(file);
            resolved.Citation.Should().BeSameAs(citation);
            resolved.Text.Should().Be(text.Substring(citation.Start, citation.Length)).And.Be(citation.Excerpt);
        }
        file.Digest.Should().Be(LocalFilePolicy.Digest([0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(text)]));
        if (mode == LocalFileRetrievalMode.MarkdownStructure && string.Equals(extension, ".md", StringComparison.Ordinal))
        {
            result.Citations.Should().Contain(citation => citation.HeadingContext != null);
            result.Citations.Should().Contain(citation => citation.IsStructuralFragment);
        }
    }

    [Theory]
    [InlineData("source")]
    [InlineData("revision")]
    [InlineData("item")]
    [InlineData("digest")]
    [InlineData("negative")]
    [InlineData("zero")]
    [InlineData("length")]
    [InlineData("start")]
    [InlineData("overflow")]
    [InlineData("past-end")]
    [InlineData("excerpt")]
    [InlineData("start-line")]
    [InlineData("start-column")]
    [InlineData("end-line")]
    [InlineData("end-column")]
    [InlineData("surrogate-start")]
    [InlineData("surrogate-end")]
    [InlineData("crlf-start")]
    [InlineData("crlf-end")]
    [InlineData("heading")]
    [InlineData("heading-context")]
    [InlineData("fragment")]
    [InlineData("subspan")]
    [InlineData("mode")]
    public void ResolveCitationRefusesForgedFieldsWithoutOverflowOrAnotherSourceFallback(string change)
    {
        var file = Revision("# Authored\r\nalpha 🙂\r\n");
        var citation = new LocalFileLexicalRetrieval().Search(file, file.Reference, "alpha",
            LocalFileRetrievalMode.MarkdownStructure, DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken).Citations.Single();
        var mode = LocalFileRetrievalMode.MarkdownStructure;
        var emoji = file.Text.IndexOf("🙂", StringComparison.Ordinal);
        citation = change switch
        {
            "source" => citation with { Source = citation.Source with { SourceId = Guid.NewGuid() } },
            "revision" => citation with { Source = citation.Source with { RevisionId = Guid.NewGuid() } },
            "item" => citation with { Source = citation.Source with { ItemId = Guid.NewGuid() } },
            "digest" => citation with { Source = citation.Source with { Digest = new string('0', 64) } },
            "negative" => citation with { Start = -1 },
            "zero" => citation with { Length = 0 },
            "length" => citation with { Length = -1 },
            "start" => citation with { Start = file.Text.Length + 1 },
            "overflow" => citation with { Start = int.MaxValue, Length = int.MaxValue },
            "past-end" => citation with { Length = int.MaxValue },
            "excerpt" => citation with { Excerpt = "forged" },
            "start-line" => citation with { StartLine = 0 },
            "start-column" => citation with { StartColumn = 0 },
            "end-line" => citation with { EndLine = int.MaxValue },
            "end-column" => citation with { EndColumn = int.MaxValue },
            "surrogate-start" => citation with { Start = emoji + 1, Length = 1, Excerpt = file.Text.Substring(emoji + 1, 1) },
            "surrogate-end" => citation with { Start = emoji, Length = 1, Excerpt = file.Text.Substring(emoji, 1) },
            "crlf-start" => citation with { Start = 11, Length = 1, Excerpt = "\n" },
            "crlf-end" => citation with { Start = 10, Length = 1, Excerpt = "\r" },
            "heading" => citation with { Heading = "# invented" },
            "heading-context" => citation with { HeadingContext = new(int.MaxValue, int.MaxValue, -1, -1, "forged") },
            "fragment" => citation with { IsStructuralFragment = true },
            "subspan" => citation with { Length = 1, Excerpt = "#", EndLine = 1, EndColumn = 2 },
            _ => citation,
        };
        if (string.Equals(change, "mode", StringComparison.Ordinal)) { mode = (LocalFileRetrievalMode)999; }
        var resolve = () => LocalFileLexicalRetrieval.ResolveCitation(file, citation, mode);
        resolve.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ResolveCitationRequiresExactReferenceNotDisplayNameOrIdenticalTextAndRejectsEmptySource()
    {
        var file = Revision("alpha");
        var other = Revision(file.Text);
        var citation = new LocalFileLexicalRetrieval().Search(file, file.Reference, "alpha",
            DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken).Citations.Single();
        var foreign = () => LocalFileLexicalRetrieval.ResolveCitation(other, citation, LocalFileRetrievalMode.LexicalLines);
        foreign.Should().Throw<InvalidDataException>();
        LocalFileLexicalRetrieval.ResolveCitation(file, citation with { DisplayIdentity = "arbitrary other file" },
            LocalFileRetrievalMode.LexicalLines).File.Should().BeSameAs(file);
        var empty = Revision(string.Empty);
        var resolve = () => LocalFileLexicalRetrieval.ResolveCitation(empty, citation with { Source = empty.Reference, Length = 0 },
            LocalFileRetrievalMode.LexicalLines);
        resolve.Should().Throw<InvalidDataException>();
        var missingFile = () => LocalFileLexicalRetrieval.ResolveCitation(null!, citation, LocalFileRetrievalMode.LexicalLines);
        missingFile.Should().Throw<ArgumentNullException>();
        var missingCitation = () => LocalFileLexicalRetrieval.ResolveCitation(file, null!, LocalFileRetrievalMode.LexicalLines);
        missingCitation.Should().Throw<ArgumentNullException>();
    }

    private static LocalFileRevision Revision(string text, string extension = ".md")
    {
        byte[] bytes = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(text)];
        return new(new(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi),
            new(@"C:\Synthetic\duplicate" + extension, "exact", bytes.Length, DateTimeOffset.UnixEpoch)), bytes, DateTimeOffset.UnixEpoch);
    }
}
