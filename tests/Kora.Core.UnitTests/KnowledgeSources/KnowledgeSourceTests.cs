using System.Text;
using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;

namespace Kora.Core.UnitTests.KnowledgeSources;

public sealed class KnowledgeSourceTests
{
    private const string Root = @"C:\Team";
    private static readonly KnowledgeSourceDirectory RootDirectory = new(Root, "root");
    private static LocalFileMetadata File(string path = @"C:\Team\notes.md", long bytes = 0, string identity = "file") =>
        new(path, identity, bytes, DateTimeOffset.UnixEpoch);

    [Fact]
    public void CompleteTreeRetainsEmptyDirectoriesCanonicalOrderAndExactBounds()
    {
        var directories = new[] { RootDirectory }.Concat(Enumerable.Range(0, 30)
            .Select(index => new KnowledgeSourceDirectory($@"{Root}\empty{index:D2}", $"directory{index}")))
            .Append(new($@"{Root}\nested", "nested")).Reverse();
        var files = Enumerable.Range(0, 32).Select(index => File($@"{Root}\nested\{index:D2}.md",
            index < 4 ? LocalFilePolicy.MaximumBytes : 0, $"file{index}")).Reverse();
        var metadata = new KnowledgeSourceMetadata(Root, "root", KnowledgeSourceScope.BoundedDescendants, directories, files);
        metadata.Directories.Count.Should().Be(32);
        metadata.Files.Count.Should().Be(32);
        metadata.CombinedBytes.Should().Be(1024 * 1024);
        metadata.Files[0].CanonicalPath.Should().EndWith(@"nested\00.md");
        metadata.Directories[0].Should().Be(RootDirectory);
        metadata.Matches(new(Root, "root", metadata.Scope, metadata.Directories.Reverse(), metadata.Files.Reverse())).Should().BeTrue();
        KnowledgeSourcePolicy.MaximumOperationTime.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(InvalidTree.NullDirectory)]
    [InlineData(InvalidTree.EmptyDirectoryIdentity)]
    [InlineData(InvalidTree.DuplicateDirectoryIdentity)]
    [InlineData(InvalidTree.DuplicateDirectoryPath)]
    [InlineData(InvalidTree.EscapeDirectory)]
    [InlineData(InvalidTree.Depth)]
    [InlineData(InvalidTree.DirectoryCount)]
    [InlineData(InvalidTree.MissingRoot)]
    [InlineData(InvalidTree.MissingParent)]
    [InlineData(InvalidTree.ImmediateDirectory)]
    [InlineData(InvalidTree.NullFile)]
    [InlineData(InvalidTree.MissingFileParent)]
    [InlineData(InvalidTree.EscapeFile)]
    [InlineData(InvalidTree.EmptyFileIdentity)]
    [InlineData(InvalidTree.DuplicateFileIdentity)]
    [InlineData(InvalidTree.DuplicateFilePath)]
    [InlineData(InvalidTree.DirectoryFileIdentity)]
    [InlineData(InvalidTree.DirectoryFilePath)]
    [InlineData(InvalidTree.NegativeBytes)]
    [InlineData(InvalidTree.FileBytes)]
    [InlineData(InvalidTree.FileCount)]
    [InlineData(InvalidTree.AggregateBytes)]
    [InlineData(InvalidTree.Empty)]
    public void InvalidOrIncompleteTreesCannotBecomeSnapshots(InvalidTree invalid)
    {
        var directories = new List<KnowledgeSourceDirectory> { RootDirectory };
        var files = new List<LocalFileMetadata> { File() };
        var scope = KnowledgeSourceScope.BoundedDescendants;
        switch (invalid)
        {
            case InvalidTree.NullDirectory: directories.Add(null!); break;
            case InvalidTree.EmptyDirectoryIdentity: directories.Add(new(Root + @"\child", " ")); break;
            case InvalidTree.DuplicateDirectoryIdentity: directories.Add(new(Root + @"\child", "root")); break;
            case InvalidTree.DuplicateDirectoryPath: directories.Add(new(Root.ToUpperInvariant(), "other")); break;
            case InvalidTree.EscapeDirectory: directories.Add(new(Root + @"Else\child", "child")); break;
            case InvalidTree.Depth: directories.Add(new(Root + @"\a\b\c\d\e", "deep")); break;
            case InvalidTree.DirectoryCount:
                directories.AddRange(Enumerable.Range(0, 32).Select(index => new KnowledgeSourceDirectory($@"{Root}\d{index}", $"d{index}"))); break;
            case InvalidTree.MissingRoot: directories.Clear(); break;
            case InvalidTree.MissingParent: directories.Add(new(Root + @"\missing\child", "child")); break;
            case InvalidTree.ImmediateDirectory: directories.Add(new(Root + @"\child", "child")); scope = KnowledgeSourceScope.ImmediateFiles; break;
            case InvalidTree.NullFile: files.Add(null!); break;
            case InvalidTree.MissingFileParent: files.Add(File(Root + @"\missing\child.md", identity: "child")); break;
            case InvalidTree.EscapeFile: files.Add(File(Root + @"Else\child.md", identity: "child")); break;
            case InvalidTree.EmptyFileIdentity: files[0] = File(identity: " "); break;
            case InvalidTree.DuplicateFileIdentity: files.Add(File(Root + @"\other.md")); break;
            case InvalidTree.DuplicateFilePath: files.Add(File(Root.ToUpperInvariant() + @"\NOTES.md", identity: "other")); break;
            case InvalidTree.DirectoryFileIdentity: files[0] = File(identity: "root"); break;
            case InvalidTree.DirectoryFilePath: directories.Add(new(Root + @"\notes.md", "child")); break;
            case InvalidTree.NegativeBytes: files[0] = File(bytes: -1); break;
            case InvalidTree.FileBytes: files[0] = File(bytes: LocalFilePolicy.MaximumBytes + 1); break;
            case InvalidTree.FileCount:
                files.AddRange(Enumerable.Range(0, 32).Select(index => File($@"{Root}\f{index}.md", identity: $"f{index}"))); break;
            case InvalidTree.AggregateBytes:
                files = Enumerable.Range(0, 5).Select(index => File($@"{Root}\f{index}.md",
                    index < 4 ? LocalFilePolicy.MaximumBytes : 1, $"f{index}")).ToList(); break;
            case InvalidTree.Empty: files.Clear(); break;
        }
        FluentActions.Invoking(() => new KnowledgeSourceMetadata(Root, "root", scope, directories, files))
            .Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void DepthFourIsAdmittedAndLegacyImmediatePolicyIsNotWidened()
    {
        var directories = new[] { RootDirectory }.Concat(Enumerable.Range(1, 4)
            .Select(depth => new KnowledgeSourceDirectory(Root + string.Concat(Enumerable.Repeat(@"\d", depth)), $"d{depth}")));
        var file = File(Root + @"\d\d\d\d\notes.md");
        var metadata = new KnowledgeSourceMetadata(Root, "root", KnowledgeSourceScope.BoundedDescendants, directories, [file]);
        metadata.Files.Should().ContainSingle();
        FluentActions.Invoking(() => new LocalFolderMetadata(Root, "root", [file])).Should().Throw<InvalidDataException>();
        var legacy = new LocalFolderMetadata(Root, "root", [File()]);
        var restored = KnowledgeSourceMetadata.FromImmediate(legacy);
        restored.Scope.Should().Be(KnowledgeSourceScope.ImmediateFiles);
        restored.Files.Should().Equal(legacy.Files);
        restored.DirectoryIdentity.Should().Be("root");
        restored.Matches(metadata).Should().BeFalse();
    }

    [Fact]
    public void NullCollectionsAndUnknownPolicyFailClosed()
    {
        FluentActions.Invoking(() => new KnowledgeSourceMetadata(Root, "root", (KnowledgeSourceScope)99, [], []))
            .Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => new KnowledgeSourceMetadata(Root, "root", KnowledgeSourceScope.ImmediateFiles, null!, []))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new KnowledgeSourceMetadata(Root, "root", KnowledgeSourceScope.ImmediateFiles, [], null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void NestedRetrievalUsesOneGlobalRankingAndExactRootRelativeSavedCitations()
    {
        var snapshot = Revision(12);
        var retrieval = new LocalFileLexicalRetrieval();
        var found = retrieval.Search(snapshot, snapshot.Reference, "alpha", DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);
        found.Citations.Count.Should().Be(LocalFileRetrievalPolicy.MaximumCitations);
        found.Truncated.Should().BeTrue();
        found.MatchingChunks.Should().Be(12);
        found.Citations[0].DisplayIdentity.Should().Be(@"d00\notes.md");
        found.Citations[1].DisplayIdentity.Should().Be(@"d01\notes.md");
        Encoding.UTF8.GetByteCount(string.Concat(found.Citations.Select(citation => citation.Excerpt)))
            .Should().BeLessThanOrEqualTo(LocalFileRetrievalPolicy.MaximumExcerptUtf8Bytes);
        foreach (var citation in found.Citations)
        {
            var span = snapshot.ResolveCitation(citation);
            span.Text.Should().Be(citation.Excerpt);
            span.File.Reference.Should().Be(citation.Source);
            span.File.Digest.Should().Be(citation.Source.Digest);
        }
        retrieval.Search(snapshot, new(snapshot.Reference.SourceId, Guid.NewGuid()), "alpha", DateTimeOffset.UnixEpoch,
            TestContext.Current.CancellationToken).Outcome.Should().Be(LocalFileSearchOutcome.Stale);
        FluentActions.Invoking(() => snapshot.ResolveCitation(found.Citations[0] with { DisplayIdentity = "notes.md" }))
            .Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => snapshot.ResolveCitation(found.Citations[0] with { Start = 1 }))
            .Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => Revision(1).ResolveCitation(found.Citations[0])).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => snapshot.ResolveCitation(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => retrieval.Search((KnowledgeSourceRevision)null!, snapshot.Reference, "", default, default))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => retrieval.Search(snapshot, null!, "", default, default)).Should().Throw<ArgumentNullException>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        FluentActions.Invoking(() => retrieval.Search(snapshot, snapshot.Reference, "", default, cancelled.Token))
            .Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void RevisionRequiresCompleteExactLineageAndRetainedReference()
    {
        var snapshot = Revision(1);
        var review = snapshot.Review;
        var files = snapshot.Files;
        var restored = new KnowledgeSourceRevision(review, files, snapshot.Reference);
        restored.Reference.Should().Be(snapshot.Reference);
        restored.Files.Should().Equal(files);
        FluentActions.Invoking(() => new KnowledgeSourceRevision(null!, files)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new KnowledgeSourceRevision(review, null!)).Should().Throw<ArgumentNullException>();
        foreach (var invalid in new[]
        {
            review with { ReviewId = Guid.Empty }, review with { SourceId = Guid.Empty },
            review with { ReviewId = Guid.NewGuid() }, review with { SourceId = Guid.NewGuid() },
            review with { Request = HostRequest.Create(RequestOrigin.LocalUi) },
            review with { Cause = new(System.Diagnostics.ActivityTraceId.CreateRandom(), System.Diagnostics.ActivitySpanId.CreateRandom(), default) },
        })
        { FluentActions.Invoking(() => new KnowledgeSourceRevision(invalid, files)).Should().Throw<InvalidDataException>(); }
        FluentActions.Invoking(() => new KnowledgeSourceRevision(review, [])).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => new KnowledgeSourceRevision(review, [null!])).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => new KnowledgeSourceRevision(review, files, new(Guid.NewGuid(), Guid.NewGuid())))
            .Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => new KnowledgeSourceRevision(review, files, new(review.SourceId, Guid.Empty)))
            .Should().Throw<InvalidDataException>();
        var other = Revision(1);
        FluentActions.Invoking(() => new KnowledgeSourceRevision(review, other.Files)).Should().Throw<InvalidDataException>();
        var two = Revision(2);
        var duplicate = two.Files.Select(file => LocalFileRevision.Restore(file.Review,
            Encoding.UTF8.GetBytes(file.Text), file.AdmittedAt, two.Files[0].Reference)).ToArray();
        FluentActions.Invoking(() => new KnowledgeSourceRevision(two.Review, duplicate)).Should().Throw<InvalidDataException>();
    }

    private static KnowledgeSourceRevision Revision(int count)
    {
        var bytes = Encoding.UTF8.GetBytes("alpha synthetic\r\n");
        var directories = Enumerable.Range(0, count).Select(index =>
            new KnowledgeSourceDirectory($@"{Root}\d{index:D2}", $"d{index}")).Prepend(RootDirectory).ToArray();
        var metadata = new KnowledgeSourceMetadata(Root, "root", KnowledgeSourceScope.BoundedDescendants, directories,
            Enumerable.Range(0, count).Select(index => File($@"{Root}\d{index:D2}\notes.md", bytes.Length, $"f{index}")));
        var review = new KnowledgeSourceReview(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi), metadata);
        return new(review, metadata.Files.Select(item => new LocalFileRevision(
            new(review.ReviewId, review.SourceId, review.Request, item), bytes, DateTimeOffset.UnixEpoch)));
    }

    public enum InvalidTree
    {
        NullDirectory, EmptyDirectoryIdentity, DuplicateDirectoryIdentity, DuplicateDirectoryPath, EscapeDirectory, Depth,
        DirectoryCount, MissingRoot, MissingParent, ImmediateDirectory, NullFile, MissingFileParent, EscapeFile, EmptyFileIdentity,
        DuplicateFileIdentity, DuplicateFilePath, DirectoryFileIdentity, DirectoryFilePath, NegativeBytes, FileBytes, FileCount,
        AggregateBytes, Empty,
    }
}
