using System.Text;
using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Context;

public sealed class LocalFolderTests
{
    private const string Root = @"C:\Team\Guides";
    private static LocalFileMetadata Metadata(int index, long bytes = 0) =>
        new(Root + $"\\{index:D2}.md", "identity-" + index, bytes, DateTimeOffset.UnixEpoch);

    [Fact]
    public void MetadataAdmitsExactCountAndCombinedByteBoundsInImmutableCanonicalOrder()
    {
        var files = Enumerable.Range(0, LocalFolderPolicy.MaximumFiles)
            .Select(index => Metadata(index, index < 4 ? LocalFilePolicy.MaximumBytes : 0)).Reverse().ToList();
        var metadata = new LocalFolderMetadata(Root, "directory", files);
        metadata.Files.Count.Should().Be(32);
        metadata.CombinedBytes.Should().Be(1024 * 1024);
        metadata.Files.Should().Equal(files.OrderBy(file => file.CanonicalPath, StringComparer.Ordinal));
        files.Clear();
        metadata.Files.Should().HaveCount(32);
        var mutation = () => ((IList<LocalFileMetadata>)metadata.Files).Clear();
        mutation.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task UnavailableInspectorAndRetrievalSeamsFailExplicitlyWithoutFallback()
    {
        ILocalFileInspector inspector = new FileOnlyInspector();
        var inspect = () => inspector.InspectFolderAsync(Root, TestContext.Current.CancellationToken);
        await inspect.Should().ThrowAsync<InvalidOperationException>();
        ILocalFileRetrieval retrieval = new FileOnlyRetrieval();
        var source = Revision("alpha");
        var search = () => retrieval.Search(source, source.Reference, "alpha", DateTimeOffset.UnixEpoch,
            TestContext.Current.CancellationToken);
        search.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MissingDirectoryIdentityAndNullItemsAreRejectedBeforeAdmission()
    {
        var identity = () => new LocalFolderMetadata(Root, "", [Metadata(0)]);
        identity.Should().Throw<InvalidDataException>();
        var nullItem = () => new LocalFolderMetadata(Root, "directory", [null!]);
        nullItem.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FolderRevisionRejectsMissingReviewAndSourceIdentities(bool reviewIdentity)
    {
        var source = Revision("alpha");
        var review = reviewIdentity ? source.Review with { ReviewId = Guid.Empty } : source.Review with { SourceId = Guid.Empty };
        var revision = () => new LocalFolderRevision(review, source.Files);
        revision.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("count")]
    [InlineData("combined")]
    [InlineData("file")]
    [InlineData("negative")]
    [InlineData("empty")]
    [InlineData("duplicate-name")]
    [InlineData("duplicate-identity")]
    [InlineData("subdirectory")]
    [InlineData("escape")]
    [InlineData("non-text")]
    [InlineData("generated")]
    [InlineData("identity")]
    public void MetadataRejectsEntireInvalidInventory(string scenario)
    {
        var files = new List<LocalFileMetadata> { Metadata(0) };
        switch (scenario)
        {
            case "count": files = Enumerable.Range(0, 33).Select(index => Metadata(index)).ToList(); break;
            case "combined":
                files = Enumerable.Range(0, 4).Select(index => Metadata(index, LocalFilePolicy.MaximumBytes)).ToList();
                files.Add(Metadata(4, 1)); break;
            case "file": files[0] = Metadata(0, LocalFilePolicy.MaximumBytes + 1); break;
            case "negative": files[0] = Metadata(0, -1); break;
            case "empty": files.Clear(); break;
            case "duplicate-name": files.Add(files[0] with { FileIdentity = "other" }); break;
            case "duplicate-identity": files.Add(Metadata(1) with { FileIdentity = files[0].FileIdentity }); break;
            case "subdirectory": files[0] = files[0] with { CanonicalPath = Root + @"\nested\guide.md" }; break;
            case "escape": files[0] = files[0] with { CanonicalPath = @"C:\Other\guide.md" }; break;
            case "non-text": files[0] = files[0] with { CanonicalPath = Root + @"\archive.zip" }; break;
            case "generated": files[0] = files[0] with { CanonicalPath = Root + @"\obj\guide.md" }; break;
            default: files[0] = files[0] with { FileIdentity = "" }; break;
        }
        var create = () => new LocalFolderMetadata(Root, "directory", files);
        create.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(@"C:\Team\.git")]
    [InlineData(@"C:\Team\obj")]
    [InlineData(@"C:\Team\..\Guides")]
    [InlineData(@"\\server\share")]
    [InlineData(@"C:\Team\Guides\")]
    public void FolderPathRejectsSameNoncanonicalAndProtectedComponentsAsFilePolicy(string root)
    {
        var validate = () => LocalFilePolicy.ValidateFolderPath(root);
        validate.Should().Throw<InvalidDataException>();
    }

    internal static LocalFolderRevision Revision(params string[] texts)
    {
        var bytes = texts.Select(text => Encoding.UTF8.GetBytes(text)).ToArray();
        var review = new LocalFolderReview(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi),
            new(Root, "directory", bytes.Select((file, index) => Metadata(index, file.Length))));
        return new(review, bytes.Select((file, index) => new LocalFileRevision(
            new(review.ReviewId, review.SourceId, review.Request, review.Metadata.Files[index], review.Cause),
            file, DateTimeOffset.UnixEpoch)));
    }

    [Fact]
    public void SearchRanksAcrossFilesAndCitesExactDistinctItemsAndOriginalBytes()
    {
        var source = Revision("# First\r\nalpha alpha\r\n", "\ufeff# Second\nalpha beta\n", "alpha\n");
        var engine = new LocalFileLexicalRetrieval();
        var result = engine.Search(source, source.Reference, "alpha beta", DateTimeOffset.UnixEpoch,
            TestContext.Current.CancellationToken);
        result.PolicyVersion.Should().Be("lexical-lines-v1");
        result.MatchingChunks.Should().Be(3);
        result.Citations.Select(citation => citation.DisplayIdentity).Should().Equal("01.md", "00.md", "02.md");
        foreach (var citation in result.Citations)
        {
            var file = source.Files.Single(file => file.Reference == citation.Source);
            citation.Excerpt.Should().Be(file.Text.Substring(citation.Start, citation.Length));
            citation.Source.SourceId.Should().Be(source.Reference.SourceId);
            citation.Source.Digest.Should().Be(LocalFilePolicy.Digest(Encoding.UTF8.GetBytes(
                file == source.Files[1] ? "\ufeff" + file.Text : file.Text)));
            citation.StartLine.Should().Be(1);
            citation.StartColumn.Should().Be(1);
            citation.EndLine.Should().Be(file == source.Files[2] ? 2 : 3);
        }
        result.Citations.Select(citation => citation.Source.ItemId).Distinct().Should().HaveCount(3);
        engine.Search(source, source.Reference, "beta alpha alpha", DateTimeOffset.UnixEpoch,
            TestContext.Current.CancellationToken).Citations.Should().Equal(result.Citations);
    }

    [Fact]
    public void SearchUsesOneGlobalCitationAndExcerptBudgetWithCanonicalTieBreaking()
    {
        var source = Revision(Enumerable.Repeat("alpha\n\nalpha\n\n", 10).ToArray());
        var engine = new LocalFileLexicalRetrieval();
        var result = engine.Search(source, source.Reference, "alpha", DateTimeOffset.UnixEpoch,
            TestContext.Current.CancellationToken);
        result.MatchingChunks.Should().Be(20);
        result.Citations.Should().HaveCount(8);
        result.Citations.Select(citation => citation.DisplayIdentity).Should().Equal(
            "00.md", "00.md", "01.md", "01.md", "02.md", "02.md", "03.md", "03.md");
        result.Truncated.Should().BeTrue();
        var large = Revision(Enumerable.Repeat("alpha " + new string('日', 1900), 8).ToArray());
        var bounded = engine.Search(large, large.Reference, "alpha", DateTimeOffset.UnixEpoch,
            TestContext.Current.CancellationToken);
        bounded.Citations.Should().HaveCount(2);
        bounded.Citations.Sum(citation => Encoding.UTF8.GetByteCount(citation.Excerpt)).Should().BeLessThanOrEqualTo(16 * 1024);
        bounded.Truncated.Should().BeTrue();
    }

    [Fact]
    public void SearchRejectsForeignRevisionsAndCancellationAndDoesNotJoinWordsAcrossFiles()
    {
        var source = Revision("al", "pha", "");
        var engine = new LocalFileLexicalRetrieval();
        engine.Search(source, source.Reference, "alpha", DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken)
            .Outcome.Should().Be(LocalFileSearchOutcome.NoMatch);
        engine.Search(source, source.Reference with { RevisionId = Guid.NewGuid() }, "al", DateTimeOffset.UnixEpoch,
            TestContext.Current.CancellationToken).Outcome.Should().Be(LocalFileSearchOutcome.Stale);
        engine.Search(source, source.Reference, " ", DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken)
            .Outcome.Should().Be(LocalFileSearchOutcome.InvalidQuery);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var search = () => engine.Search(source, source.Reference, "al", DateTimeOffset.UnixEpoch, cancellation.Token);
        search.Should().Throw<OperationCanceledException>();
        var partial = () => new LocalFolderRevision(source.Review, source.Files.Take(1));
        partial.Should().Throw<InvalidDataException>();
        var reversed = () => new LocalFolderRevision(source.Review, source.Files.Reverse());
        reversed.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("preview folder", LocalFileOperation.SelectFolder)]
    [InlineData("search folder", LocalFileOperation.Inspect)]
    [InlineData("inspect folder", LocalFileOperation.Inspect)]
    [InlineData("clear folder preview", LocalFileOperation.Clear)]
    [InlineData("search folder private query", LocalFileOperation.Invalid)]
    [InlineData("inspect folder private query", LocalFileOperation.Invalid)]
    [InlineData("preview folder C:\\Team", LocalFileOperation.Invalid)]
    public void CommandsNeverAdmitInlinePathsOrQueries(string text, LocalFileOperation expected) =>
        LocalFileCommand.Parse(text)!.Operation.Should().Be(expected);

    private sealed class FileOnlyInspector : ILocalFileInspector
    {
        public Task<ILocalFileSelection> InspectAsync(string selectedPath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Synthetic single-file-only seam.");
    }

    private sealed class FileOnlyRetrieval : ILocalFileRetrieval
    {
        public LocalFileSearchResult Search(LocalFileRevision revision, LocalFileReference exactSource, string query,
            DateTimeOffset observedAt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Synthetic single-file-only seam.");
    }
}
