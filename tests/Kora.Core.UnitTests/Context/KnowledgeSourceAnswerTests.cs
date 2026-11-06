using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;

namespace Kora.Core.UnitTests.Context;

public sealed class KnowledgeSourceAnswerTests
{
    [Fact]
    public void TwoSelectedRevisionsShareOneBudgetAndKeepSourceBoundCitationsAndUnchangedQuestion()
    {
        var first = new Fixture(6);
        var second = new Fixture(6, root: @"C:\Other", name: "Other");
        var retrieval = new LocalFileLexicalRetrieval();
        var found = retrieval.Search([first.Revision, second.Revision], "alpha", DateTimeOffset.UnixEpoch, CancellationToken.None);
        found.Citations.Should().HaveCount(8);
        found.MatchingChunks.Should().Be(12);
        found.Truncated.Should().BeTrue();
        var inputs = new[] { new KnowledgeSourceAnswerSource(first.Source, first.Revision),
            new KnowledgeSourceAnswerSource(second.Source, second.Revision) };
        var review = new KnowledgeSourceAnswerReview(inputs, "alpha?", found, first.Target);
        inputs[0] = inputs[1];
        review.Sources[0].Source.Should().BeSameAs(first.Source);
        review.ExcerptUtf8Bytes.Should().BeLessThanOrEqualTo(LocalFileRetrievalPolicy.MaximumExcerptUtf8Bytes);
        var context = new KnowledgeSourceAnswerContext(review, review.Target.Request, new(1), DateTimeOffset.UnixEpoch);
        context.Envelope.UserRequest.Should().Be("alpha?");
        context.Envelope.Tools.Should().BeEmpty();
        foreach (var pair in context.Envelope.Evidence.Zip(review.Search.Citations))
        {
            using var json = JsonDocument.Parse(pair.First.Content);
            json.RootElement.GetProperty("Source").GetString().Should().Be(review.DescribeCitation(pair.Second));
            json.RootElement.GetProperty("Excerpt").GetString().Should().Be(pair.Second.Excerpt);
            review.ResolveCitation(pair.Second).Text.Should().Be(pair.Second.Excerpt);
        }
        var result = context.Resolve(Success(context));
        result.Claims.Single().Citations.Should().Equal(found.Citations);
        review.SourceFor(found.Citations[^1]).Source.Should().BeSameAs(second.Source);
        var selected = review.SelectEvidence([found.Citations[0], found.Citations[^1]]);
        selected.Sources.Should().Equal(review.Sources);
        selected.ExpiresAt.Should().Be(review.ExpiresAt);
        review.IsCurrent(DateTimeOffset.UnixEpoch).Should().BeFalse();
        selected.TryConsume().Should().BeTrue();
        selected.TryConsume().Should().BeFalse();
        FluentActions.Invoking(() => review.SourceFor(null!)).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("too-many")]
    [InlineData("null-item")]
    [InlineData("duplicate")]
    [InlineData("disabled-second")]
    [InlineData("historical-second")]
    [InlineData("foreign-citation")]
    public void CombinedReviewRejectsInvalidSourceSetsBeforeContext(string defect)
    {
        var first = new Fixture();
        var second = new Fixture(root: @"C:\Other");
        var a = new KnowledgeSourceAnswerSource(first.Source, first.Revision);
        var b = new KnowledgeSourceAnswerSource(second.Source, second.Revision);
        IReadOnlyList<KnowledgeSourceAnswerSource> sources = defect switch
        {
            "null" => null!,
            "empty" => [],
            "too-many" => [a, b, a],
            "null-item" => [a, null!],
            "duplicate" => [a, a],
            "disabled-second" => [a, b with { Source = b.Source with { State = KnowledgeSourceState.Disabled } }],
            _ => [a, b],
        };
        var target = defect is "historical-second"
            ? LocalContextSessionTarget.ReviewedFresh(second.Revision.Review.Request, HostRequest.Create(RequestOrigin.LocalUi))
            : first.Target;
        var found = defect is "foreign-citation" ? new Fixture().Search
            : new LocalFileLexicalRetrieval().Search([first.Revision, second.Revision], "alpha", DateTimeOffset.UnixEpoch, CancellationToken.None);
        var create = () => new KnowledgeSourceAnswerReview(sources, "alpha", found, target);
        if (defect is "null") { create.Should().Throw<ArgumentNullException>(); }
        else { create.Should().Throw<InvalidDataException>(); }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("too-many")]
    [InlineData("duplicate")]
    [InlineData("null-item")]
    [InlineData("cancelled")]
    [InlineData("unavailable")]
    public void CombinedRetrievalRejectsInvalidSetsWithoutFallback(string defect)
    {
        var first = new Fixture();
        IReadOnlyList<KnowledgeSourceRevision> revisions = defect switch
        {
            "null" => null!,
            "empty" => [],
            "too-many" => [first.Revision, first.Revision, first.Revision],
            "null-item" => [null!],
            "duplicate" => [first.Revision, first.Revision],
            _ => [first.Revision],
        };
        using var cancellation = new CancellationTokenSource();
        if (defect is "cancelled") { cancellation.Cancel(); }
        ILocalFileRetrieval retrieval = defect is "unavailable" ? new MissingRetrieval() : new LocalFileLexicalRetrieval();
        var search = () => retrieval.Search(revisions, "alpha", DateTimeOffset.UnixEpoch, cancellation.Token);
        if (defect is "null") { search.Should().Throw<ArgumentNullException>(); }
        else if (defect is "cancelled") { search.Should().Throw<OperationCanceledException>(); }
        else if (defect is "unavailable") { search.Should().Throw<InvalidOperationException>(); }
        else { search.Should().Throw<InvalidDataException>(); }
    }

    [Fact]
    public void CombinedRetrievalRetainsGlobalExcerptByteCeilingAndSingleSetSupport()
    {
        var first = new Fixture(8, "alpha " + new string('x', 2042));
        var second = new Fixture(8, "alpha " + new string('x', 2042), @"C:\Other");
        var retrieval = new LocalFileLexicalRetrieval();
        var found = retrieval.Search([first.Revision, second.Revision], "alpha", DateTimeOffset.UnixEpoch, CancellationToken.None);
        found.Citations.Sum(item => Encoding.UTF8.GetByteCount(item.Excerpt)).Should().Be(16 * 1024);
        found.Citations.Should().HaveCount(8);
        found.MatchingChunks.Should().Be(16);
        var single = retrieval.Search([first.Revision], "alpha", DateTimeOffset.UnixEpoch, CancellationToken.None);
        single.Citations.Should().Equal(first.Search.Citations);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(12, true)]
    public void DeliberateSelectionKeepsOriginalAuthorityDeadlineAndOnlyChosenCitationBindings(int files, bool retrievalTruncated)
    {
        var f = new Fixture(files);
        var retrieved = f.Review();
        var input = new[] { retrieved.Search.Citations[1] };
        var chosen = retrieved.SelectEvidence(input);
        input[0] = retrieved.Search.Citations[0];
        chosen.Search.Citations.Should().Equal(retrieved.Search.Citations[1]);
        chosen.RetrievedSearch.Should().BeSameAs(retrieved.Search);
        chosen.Source.Should().BeSameAs(retrieved.Source);
        chosen.Revision.Should().BeSameAs(retrieved.Revision);
        chosen.Target.Should().BeSameAs(retrieved.Target);
        chosen.Question.Should().Be(retrieved.Question);
        chosen.ExpiresAt.Should().Be(retrieved.ExpiresAt);
        chosen.ExcerptUtf8Bytes.Should().Be(Encoding.UTF8.GetByteCount(chosen.Search.Citations[0].Excerpt));
        chosen.IsFromSameRetrieval(retrieved).Should().BeTrue();
        chosen.IsFromSameRetrieval(f.Review()).Should().BeFalse();
        chosen.IsCurrent(DateTimeOffset.UnixEpoch.AddTicks(-1)).Should().BeFalse();
        chosen.IsCurrent(chosen.ExpiresAt).Should().BeFalse();
        retrieved.IsCurrent(DateTimeOffset.UnixEpoch).Should().BeFalse();
        retrieved.TryConsume().Should().BeFalse();
        var context = new KnowledgeSourceAnswerContext(chosen, chosen.Target.Request, new(1), DateTimeOffset.UnixEpoch);
        context.Envelope.Evidence.Should().ContainSingle();
        var result = context.Resolve(Success(context));
        result.Claims.Single().Citations.Should().Equal(chosen.Search.Citations);
        result.RetrievalTruncated.Should().Be(retrievalTruncated);
        chosen.TryConsume().Should().BeTrue();
        chosen.TryConsume().Should().BeFalse();
        chosen.Revoke();
        retrieved.IsRevoked.Should().BeTrue();
        context.Resolve(Success(context)).Outcome.Should().Be(LocalFileAnswerOutcome.Stale);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("over-limit")]
    [InlineData("foreign")]
    [InlineData("cloned")]
    [InlineData("modified")]
    [InlineData("duplicate")]
    [InlineData("revoked")]
    [InlineData("consumed")]
    public void InvalidSelectionRetiresEarlierReviewWithoutSubstitutionOrPartialSelection(string defect)
    {
        var f = new Fixture();
        var retrieved = f.Review();
        var chosen = retrieved.SelectEvidence([retrieved.Search.Citations[0]]);
        if (defect is "revoked") { retrieved.Revoke(); }
        if (defect is "consumed") { chosen.TryConsume().Should().BeTrue(); }
        LocalFileCitation[] selection = defect switch
        {
            "empty" => [],
            "over-limit" => Enumerable.Repeat(retrieved.Search.Citations[0], 9).ToArray(),
            "foreign" => [new Fixture().Search.Citations[0]],
            "cloned" => [retrieved.Search.Citations[0] with { }],
            "modified" => [retrieved.Search.Citations[0] with { Excerpt = "changed" }],
            "duplicate" => [retrieved.Search.Citations[0], retrieved.Search.Citations[0]],
            _ => [retrieved.Search.Citations[0]],
        };
        var select = () => retrieved.SelectEvidence(selection);
        select.Should().Throw<InvalidDataException>();
        chosen.IsCurrent(DateTimeOffset.UnixEpoch).Should().BeFalse();
        chosen.TryConsume().Should().BeFalse();
    }

    [Fact]
    public void SelectionChangeRetiresContextAndReplacementUsesOriginalRetrievalWithoutRenewal()
    {
        var retrieved = new Fixture().Review();
        var first = retrieved.SelectEvidence([retrieved.Search.Citations[0]]);
        var context = new KnowledgeSourceAnswerContext(first, first.Target.Request, new(1), DateTimeOffset.UnixEpoch);
        retrieved.InvalidateSelection();
        context.Resolve(Success(context)).Outcome.Should().Be(LocalFileAnswerOutcome.Stale);
        var second = first.SelectEvidence([retrieved.Search.Citations[1]]);
        second.Search.Citations.Should().Equal(retrieved.Search.Citations[1]);
        second.ExpiresAt.Should().Be(first.ExpiresAt);
        second.IsCurrent(DateTimeOffset.UnixEpoch).Should().BeTrue();
        retrieved.Revoke();
        second.IsRevoked.Should().BeTrue();
        second.IsCurrent(DateTimeOffset.UnixEpoch).Should().BeFalse();
        var select = () => retrieved.SelectEvidence(null!);
        select.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void NamedContextBindsOnlyRetainedRootRelativeSpansToFreshHostTargetWithoutRewritingLineage()
    {
        var f = new Fixture();
        var review = f.Review();
        var context = new KnowledgeSourceAnswerContext(review, review.Target.Request, new(7), DateTimeOffset.UnixEpoch);
        context.Envelope.UserRequest.Should().Be("alpha?");
        context.Envelope.Request.Should().BeSameAs(f.Target.Request);
        context.Envelope.Evidence.Should().OnlyContain(item => item.Disclosure == ModelEvidenceDisclosure.LocalOnly
            && ReferenceEquals(item.Request, f.Target.Request) && item.Revision.Value == 7);
        context.Envelope.ToolMode.Should().Be(ModelToolMode.None);
        context.Envelope.Tools.Should().BeEmpty();
        var model = Success(context);
        var result = context.Resolve(model);
        result.Outcome.Should().Be(LocalFileAnswerOutcome.Succeeded);
        result.Claims.Single().Citations.Should().Equal(f.Search.Citations);
        f.Revision.Review.Request.Should().NotBeSameAs(f.Target.SourceRequest);
        review.ExcerptUtf8Bytes.Should().Be(f.Search.Citations.Sum(item => Encoding.UTF8.GetByteCount(item.Excerpt)));
        review.TryConsume().Should().BeTrue();
        review.TryConsume().Should().BeFalse();
        review.IsCurrent(DateTimeOffset.UnixEpoch.AddTicks(-1)).Should().BeFalse();
        review.IsCurrent(review.ExpiresAt.AddTicks(-1)).Should().BeTrue();
        review.IsCurrent(review.ExpiresAt).Should().BeFalse();
        review.Revoke();
        review.IsRevoked.Should().BeTrue();
        review.TryConsume().Should().BeFalse();
        context.Resolve(model).Outcome.Should().Be(LocalFileAnswerOutcome.Stale);
    }

    [Theory]
    [InlineData("state")]
    [InlineData("source")]
    [InlineData("metadata")]
    [InlineData("question")]
    [InlineData("outcome")]
    [InlineData("mode")]
    [InlineData("version")]
    [InlineData("empty")]
    [InlineData("count")]
    [InlineData("matching")]
    [InlineData("truncation")]
    [InlineData("invocation")]
    [InlineData("target-invocation")]
    [InlineData("historical-request")]
    [InlineData("historical-task")]
    [InlineData("foreign-citation")]
    [InlineData("relative-location")]
    [InlineData("span")]
    [InlineData("duplicate")]
    public void MalformedSourceEvidenceOrHistoricalAuthorityIsNeverAdmitted(string defect)
    {
        var f = new Fixture();
        var source = defect switch
        {
            "state" => f.Source with { State = KnowledgeSourceState.Disabled },
            "source" => f.Source with { Id = Guid.NewGuid() },
            "metadata" => f.Source with { Folder = new(@"C:\Team", "foreign", KnowledgeSourceScope.ImmediateFiles,
                [new(@"C:\Team", "foreign")], f.Source.Folder.Files) },
            _ => f.Source,
        };
        var first = f.Search.Citations[0];
        var search = defect switch
        {
            "outcome" => f.Search with { Outcome = LocalFileSearchOutcome.NoMatch },
            "mode" => f.Search with { Mode = LocalFileRetrievalMode.MarkdownStructure },
            "version" => f.Search with { PolicyVersion = "foreign" },
            "empty" => f.Search with { Citations = [] },
            "count" => f.Search with { Citations = Enumerable.Repeat(first, 9).ToArray() },
            "matching" => f.Search with { MatchingChunks = 0 },
            "truncation" => f.Search with { Truncated = true },
            "foreign-citation" => f.Search with { Citations = [first with { Source = first.Source with { RevisionId = Guid.NewGuid() } }, f.Search.Citations[1]] },
            "relative-location" => f.Search with { Citations = [first with { DisplayIdentity = @"Other\notes.md" }, f.Search.Citations[1]] },
            "span" => f.Search with { Citations = [first with { Start = 1 }, f.Search.Citations[1]] },
            "duplicate" => f.Search with { Citations = [first, first] },
            _ => f.Search,
        };
        var input = f.Target.SourceRequest;
        input = defect switch
        {
            "invocation" => new(input.RequestId, input.SessionId, input.TaskId, input.Origin, new(Guid.NewGuid())),
            "historical-request" => f.Revision.Review.Request,
            "historical-task" => new(input.RequestId, input.SessionId, f.Revision.Review.Request.TaskId, input.Origin),
            _ => input,
        };
        var target = f.Target.Request;
        if (defect is "target-invocation") { target = new(target.RequestId, target.SessionId, target.TaskId, target.Origin, new(Guid.NewGuid())); }
        var create = () => new KnowledgeSourceAnswerReview(source, f.Revision, defect is "question" ? "!!!" : "alpha?",
            search, LocalContextSessionTarget.ReviewedFresh(input, target));
        create.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ExactSharedBudgetsAreEightCitationsSixteenKiBAndThirtyTwoKiBCompleteInputWithoutTruncation()
    {
        var f = new Fixture(8, "alpha " + new string('"', 2042));
        var review = f.Review();
        review.Search.Citations.Should().HaveCount(8);
        review.ExcerptUtf8Bytes.Should().Be(16 * 1024);
        var context = new KnowledgeSourceAnswerContext(review, review.Target.Request, new(1), DateTimeOffset.UnixEpoch);
        context.Envelope.Evidence.Should().HaveCount(8);
        context.Envelope.Serialize().Length.Should().BeGreaterThan(32 * 1024);
        var validate = () => context.Envelope.RequireCompleteInputLimit();
        validate.Should().Throw<InvalidDataException>();
        var bounded = new Fixture(12).Review();
        bounded.Search.Citations.Should().HaveCount(8);
        bounded.Search.Truncated.Should().BeTrue();
        bounded.Search.MatchingChunks.Should().Be(12);
        var wide = new Fixture(8, "alpha " + new string('\u4e00', 2042));
        var citations = wide.Revision.Files.SelectMany(file => new LocalFileLexicalRetrieval()
            .Search(file, file.Reference, "alpha", DateTimeOffset.UnixEpoch, CancellationToken.None).Citations).ToArray();
        var excessive = () => new KnowledgeSourceAnswerReview(wide.Source, wide.Revision, "alpha?",
            wide.Search with { Citations = citations, MatchingChunks = 8, Truncated = false }, wide.Target);
        excessive.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("request")]
    [InlineData("generation")]
    [InlineData("future")]
    [InlineData("expired")]
    [InlineData("revoked")]
    public void ContextRequiresExactLiveHostAdmission(string defect)
    {
        var f = new Fixture();
        var review = f.Review();
        if (defect is "revoked") { review.Revoke(); }
        var now = defect switch { "future" => DateTimeOffset.UnixEpoch.AddTicks(-1), "expired" => review.ExpiresAt, _ => DateTimeOffset.UnixEpoch };
        var create = () => new KnowledgeSourceAnswerContext(review,
            defect is "request" ? HostRequest.Create(RequestOrigin.LocalUi) : review.Target.Request,
            defect is "generation" ? default : new(1), now);
        create.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void NullArgumentsAreExplicitContractFailuresAndUnavailableRetrievalNeverDefaultsToAnotherSource()
    {
        var f = new Fixture();
        var source = () => new KnowledgeSourceAnswerReview(null!, f.Revision, "alpha", f.Search, f.Target);
        var revision = () => new KnowledgeSourceAnswerReview(f.Source, null!, "alpha", f.Search, f.Target);
        var search = () => new KnowledgeSourceAnswerReview(f.Source, f.Revision, "alpha", null!, f.Target);
        var target = () => new KnowledgeSourceAnswerReview(f.Source, f.Revision, "alpha", f.Search, null!);
        var context = () => new KnowledgeSourceAnswerContext(null!, f.Target.Request, new(1), DateTimeOffset.UnixEpoch);
        source.Should().Throw<ArgumentNullException>();
        revision.Should().Throw<ArgumentNullException>();
        search.Should().Throw<ArgumentNullException>();
        target.Should().Throw<ArgumentNullException>();
        context.Should().Throw<ArgumentNullException>();
        ILocalFileRetrieval missing = new MissingRetrieval();
        var retrieve = () => missing.Search(f.Revision, f.Revision.Reference, "alpha", DateTimeOffset.UnixEpoch, CancellationToken.None);
        retrieve.Should().Throw<InvalidOperationException>();
    }

    private sealed class MissingRetrieval : ILocalFileRetrieval
    {
        public LocalFileSearchResult Search(LocalFileRevision revision, LocalFileReference exactSource, string query,
            DateTimeOffset observedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static ModelTurnResult Success(KnowledgeSourceAnswerContext context) => new(ModelTurnOutcome.Succeeded, ModelTurnReason.None,
        new(new(Guid.NewGuid()), context.Envelope.Request, ModelProviderSelection.OllamaCandidate),
        new(JsonSerializer.Serialize(new { unsupported = false, claims = new[] { new { text = "synthetic",
            evidenceIds = context.Envelope.Evidence.Select(item => item.Id.Value) } } }), null));

    private sealed class Fixture
    {
        internal KnowledgeSource Source { get; }
        internal KnowledgeSourceRevision Revision { get; }
        internal LocalFileSearchResult Search { get; }
        internal LocalContextSessionTarget Target { get; } =
            LocalContextSessionTarget.ReviewedFresh(HostRequest.Create(RequestOrigin.LocalUi), HostRequest.Create(RequestOrigin.LocalUi));
        internal Fixture(int files = 2, string text = "alpha marker", string root = @"C:\Team", string name = "Team")
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var metadata = new KnowledgeSourceMetadata(root, "root", KnowledgeSourceScope.ImmediateFiles,
                [new(root, "root")], Enumerable.Range(0, files)
                .Select(index => new LocalFileMetadata($@"{root}\{index:D2}.md", $"file-{index}", bytes.Length, DateTimeOffset.UnixEpoch)));
            Source = new(Guid.NewGuid(), 3, name, metadata, KnowledgeSourceState.Enabled, DateTimeOffset.UnixEpoch, 100);
            var historical = new KnowledgeSourceReview(Guid.NewGuid(), Source.Id, HostRequest.Create(RequestOrigin.LocalUi), metadata);
            Revision = new(historical, metadata.Files.Select(item => new LocalFileRevision(
                new(historical.ReviewId, historical.SourceId, historical.Request, item), bytes, DateTimeOffset.UnixEpoch)));
            Search = new LocalFileLexicalRetrieval().Search(Revision, Revision.Reference, "alpha", DateTimeOffset.UnixEpoch, CancellationToken.None);
        }
        internal KnowledgeSourceAnswerReview Review() => new(Source, Revision, "alpha?", Search, Target);
    }
}
