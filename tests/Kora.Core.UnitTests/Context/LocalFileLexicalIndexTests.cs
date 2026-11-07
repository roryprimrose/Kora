using System.Text;
using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;

namespace Kora.Core.UnitTests.Context;

public sealed class LocalFileLexicalIndexTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static KnowledgeSourceRevision Revision(string text, bool indexed = true, Guid? source = null)
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        var metadata = new LocalFileMetadata(@"C:\Team\notes.md", "physical", bytes.Length, DateTimeOffset.UnixEpoch);
        var review = new KnowledgeSourceReview(Guid.NewGuid(), source ?? Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi),
            new(@"C:\Team", "root", KnowledgeSourceScope.ImmediateFiles, [new(@"C:\Team", "root")], [metadata]));
        var file = new LocalFileRevision(new(review.ReviewId, review.SourceId, review.Request, metadata), bytes, DateTimeOffset.UnixEpoch);
        return new(review, [file], indexes: indexed ? [LocalFileLexicalIndex.Build(file, Token)] : null);
    }

    [Theory]
    [InlineData("# Heading\r\nalpha café 日本語\r\n\r\nalpha beta 🙂")]
    [InlineData("")]
    [InlineData("alpha alpha\n\nbeta\n")]
    [InlineData("absent ")]
    public void IndexedQueriesPreserveLegacyRankingExactCitationsAndNoMatch(string text)
    {
        var revision = Revision(text);
        var legacy = new KnowledgeSourceRevision(revision.Review, revision.Files, revision.Reference);
        var retrieval = new LocalFileLexicalRetrieval();
        var indexed = retrieval.Search(revision, revision.Reference, "ALPHA cafe\u0301 beta", DateTimeOffset.UnixEpoch, Token);
        var original = retrieval.Search(legacy, legacy.Reference, "ALPHA cafe\u0301 beta", DateTimeOffset.UnixEpoch, Token);
        indexed.Should().BeEquivalentTo(original);
        foreach (var citation in indexed.Citations)
        { revision.ResolveCitation(citation).Text.Should().Be(citation.Excerpt); }
        revision.Indexes![0].PolicyVersion.Should().Be(LocalFileRetrievalPolicy.Version);
        var file = revision.Files[0];
        var restored = LocalFileLexicalIndex.Restore(file, LocalFileLexicalIndex.Version, LocalFileRetrievalPolicy.Version,
            file.Digest, revision.Indexes[0].Chunks, Token);
        restored.Chunks.Should().BeEquivalentTo(revision.Indexes[0].Chunks);
    }

    [Fact]
    public void RefreshReusesOnlyUnchangedOriginalByteDigestAndRemapsCitationsToNewLineage()
    {
        var old = Revision("alpha " + string.Join(' ', Enumerable.Repeat("alpha", 30)));
        var fresh = Revision(old.Files[0].Text, source: old.Reference.SourceId);
        var prior = old.Indexes![0];
        var reused = LocalFileLexicalIndex.Build(fresh.Files[0], Token, prior);
        reused.Should().BeSameAs(prior);
        reused.Chunks[0].Frequencies["ALPHA"].Should().Be(LocalFileRetrievalPolicy.MaximumTermFrequency);
        var changed = Revision("beta", source: old.Reference.SourceId);
        LocalFileLexicalIndex.Build(changed.Files[0], Token, prior).Should().NotBeSameAs(prior);
        var bound = new KnowledgeSourceRevision(fresh.Review, fresh.Files, fresh.Reference, [reused]);
        var found = new LocalFileLexicalRetrieval().Search(bound, bound.Reference, "alpha", DateTimeOffset.UnixEpoch, Token);
        found.Citations.Single().Source.Should().Be(fresh.Files[0].Reference).And.NotBe(old.Files[0].Reference);
        FluentActions.Invoking(() => bound.ResolveCitation(found.Citations[0] with { Source = old.Files[0].Reference }))
            .Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("version")]
    [InlineData("policy")]
    [InlineData("digest")]
    [InlineData("count")]
    [InlineData("null")]
    [InlineData("start")]
    [InlineData("length")]
    [InlineData("heading")]
    [InlineData("tokens")]
    public void CorruptDerivedIndexFailsClosed(string defect)
    {
        var revision = Revision("# Heading\nalpha");
        var file = revision.Files[0];
        var original = revision.Indexes![0].Chunks.Single();
        LocalFileLexicalIndex.Chunk[] chunks = defect switch
        {
            "count" => [],
            "null" => [null!],
            _ => [new(string.Equals(defect, "start", StringComparison.Ordinal) ? 1 : original.Start,
                string.Equals(defect, "length", StringComparison.Ordinal) ? 1 : original.Length,
                string.Equals(defect, "heading", StringComparison.Ordinal) ? "wrong" : original.Heading,
                string.Equals(defect, "tokens", StringComparison.Ordinal)
                    ? new Dictionary<string, int>(StringComparer.Ordinal) { ["BETA"] = 1 } : original.Frequencies)],
        };
        FluentActions.Invoking(() => LocalFileLexicalIndex.Restore(file,
            string.Equals(defect, "version", StringComparison.Ordinal) ? "unknown" : LocalFileLexicalIndex.Version,
            string.Equals(defect, "policy", StringComparison.Ordinal) ? "unknown" : LocalFileRetrievalPolicy.Version,
            string.Equals(defect, "digest", StringComparison.Ordinal) ? "wrong" : file.Digest, chunks, Token))
            .Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void NullInputsCancellationAndWrongIndexMembershipCannotReturnUsableState()
    {
        var revision = Revision("alpha");
        var file = revision.Files[0];
        FluentActions.Invoking(() => LocalFileLexicalIndex.Build(null!, Token)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => LocalFileLexicalIndex.Restore(null!, "", "", "", [], Token)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => LocalFileLexicalIndex.Restore(file, "", "", "", null!, Token)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new LocalFileLexicalIndex.Chunk(0, 1, null, null!)).Should().Throw<ArgumentNullException>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        FluentActions.Invoking(() => LocalFileLexicalIndex.Build(file, cancelled.Token)).Should().Throw<OperationCanceledException>();
        FluentActions.Invoking(() => LocalFileLexicalIndex.Restore(file, "", "", "", [], cancelled.Token)).Should().Throw<OperationCanceledException>();
        foreach (var indexes in new IReadOnlyList<LocalFileLexicalIndex>[] { [], [null!], [Revision("beta").Indexes![0]] })
        {
            FluentActions.Invoking(() => new KnowledgeSourceRevision(revision.Review, revision.Files, revision.Reference, indexes))
                .Should().Throw<InvalidDataException>();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedLegacyAndIndexedSourcesPreserveGlobalBudgets(bool secondIndexed)
    {
        var first = Revision(string.Concat(Enumerable.Repeat("alpha\n\n", 10)));
        var second = Revision("alpha beta", secondIndexed);
        var retrieval = new LocalFileLexicalRetrieval();
        var found = retrieval.Search([first, second], "alpha beta", DateTimeOffset.UnixEpoch, Token);
        var legacy = retrieval.Search([new(first.Review, first.Files, first.Reference), new(second.Review, second.Files, second.Reference)],
            "alpha beta", DateTimeOffset.UnixEpoch, Token);
        found.Should().BeEquivalentTo(legacy);
        found.Citations.Should().HaveCount(LocalFileRetrievalPolicy.MaximumCitations);
        found.Truncated.Should().BeTrue();
        retrieval.Search(first, first.Reference, "", DateTimeOffset.UnixEpoch, Token).Outcome.Should().Be(LocalFileSearchOutcome.InvalidQuery);
        retrieval.Search(first, new(first.Reference.SourceId, Guid.NewGuid()), "alpha", DateTimeOffset.UnixEpoch, Token)
            .Outcome.Should().Be(LocalFileSearchOutcome.Stale);
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("digest")]
    [InlineData("fragment")]
    [InlineData("context")]
    [InlineData("chunk")]
    public void CitationCannotChangeIndexedPolicyOrAuthoredChunk(string defect)
    {
        var revision = Revision("alpha\nbeta");
        var file = revision.Files[0];
        var citation = new LocalFileLexicalRetrieval().Search(revision, revision.Reference, "alpha", DateTimeOffset.UnixEpoch, Token).Citations.Single();
        if (string.Equals(defect, "fragment", StringComparison.Ordinal)) { citation = citation with { IsStructuralFragment = true }; }
        if (string.Equals(defect, "context", StringComparison.Ordinal)) { citation = citation with { HeadingContext = new(0, 1, 1, 1, "# heading") }; }
        if (string.Equals(defect, "chunk", StringComparison.Ordinal)) { citation = citation with { Heading = "wrong" }; }
        FluentActions.Invoking(() => LocalFileLexicalRetrieval.ResolveCitation(file, citation,
            string.Equals(defect, "mode", StringComparison.Ordinal) ? LocalFileRetrievalMode.MarkdownStructure : LocalFileRetrievalMode.LexicalLines,
            string.Equals(defect, "digest", StringComparison.Ordinal) ? Revision("beta").Indexes![0] : revision.Indexes![0]))
            .Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void OversizedWordsAndExcerptBudgetsMatchUnindexedPolicy()
    {
        var revision = Revision("alpha " + new string('x', 5000) + "\n\n"
            + string.Concat(Enumerable.Repeat("alpha " + new string('é', 1900) + "\n\n", 20)));
        var legacy = new KnowledgeSourceRevision(revision.Review, revision.Files, revision.Reference);
        var retrieval = new LocalFileLexicalRetrieval();
        retrieval.Search(revision, revision.Reference, "alpha", DateTimeOffset.UnixEpoch, Token).Should().BeEquivalentTo(
            retrieval.Search(legacy, legacy.Reference, "alpha", DateTimeOffset.UnixEpoch, Token));
    }
}
