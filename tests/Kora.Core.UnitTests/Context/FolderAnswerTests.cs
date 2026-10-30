using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Commands;
using Kora.Core.Context;
using Kora.Core.Context.FolderAnswers;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Context;

public sealed class FolderAnswerTests
{
    private static FolderAnswerReview Review(string text = "# Header\r\nalpha marker", int files = 2)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var metadata = new LocalFolderMetadata(@"C:\Team", "directory",
            Enumerable.Range(0, files).Select(index => new LocalFileMetadata($@"C:\Team\{index:D2}.md",
                $"identity-{index}", Encoding.UTF8.GetByteCount(text), DateTimeOffset.UnixEpoch)));
        var folderReview = new LocalFolderReview(Guid.NewGuid(), Guid.NewGuid(), request, metadata);
        var folder = new LocalFolderRevision(folderReview, metadata.Files.Select(file => new LocalFileRevision(
            new(folderReview.ReviewId, folderReview.SourceId, request, file), Encoding.UTF8.GetBytes(text), DateTimeOffset.UnixEpoch)));
        var search = new LocalFileLexicalRetrieval().Search(folder, folder.Reference, "alpha", DateTimeOffset.UnixEpoch, CancellationToken.None);
        var review = new FolderAnswerReview(folder, "alpha?", search,
            LocalContextSessionTarget.ReviewedFresh(request, HostRequest.Create(RequestOrigin.LocalUi)));
        return review.SelectEvidence(review.Search.Citations);
    }

    private static FolderAnswerContext Context(FolderAnswerReview review) =>
        new(review, review.Target.Request, new(7), DateTimeOffset.UnixEpoch);

    private static ModelTurnResult Success(FolderAnswerContext context, string answer) =>
        new(ModelTurnOutcome.Succeeded, ModelTurnReason.None,
            new(new(Guid.NewGuid()), context.Envelope.Request, ModelProviderSelection.OllamaCandidate), new(answer, null));

    private static string Answer(FolderAnswerContext context) => JsonSerializer.Serialize(new
    {
        unsupported = false,
        claims = new[] { new { text = "Both reviewed files contain the marker.", evidenceIds = context.Envelope.Evidence.Select(item => item.Id.Value) } },
    });

    [Fact]
    public void DeliberateSubsetKeepsWholeOriginalCitationsQuestionTargetAndDeadline()
    {
        var all = Review();
        var raw = new FolderAnswerReview(all.Source, all.Question, all.Search, all.Target);
        raw.IsUserSelected.Should().BeFalse();
        raw.TryConsume().Should().BeFalse();
        var unselected = () => Context(raw);
        unselected.Should().Throw<InvalidDataException>();
        var choice = new List<LocalFileCitation> { raw.Search.Citations[1] };
        var selected = raw.SelectEvidence(choice);
        choice.Clear();
        selected.Question.Should().Be(raw.Question);
        selected.Source.Should().BeSameAs(raw.Source);
        selected.Target.Should().BeSameAs(raw.Target);
        selected.RetrievedSearch.Should().BeSameAs(raw.Search);
        selected.Search.Citations.Single().Should().BeSameAs(raw.Search.Citations[1]);
        selected.ExcerptUtf8Bytes.Should().Be(Encoding.UTF8.GetByteCount(raw.Search.Citations[1].Excerpt));
        selected.ExpiresAt.Should().Be(raw.ExpiresAt);
        selected.Search.Truncated.Should().BeTrue();
        var context = new FolderAnswerContext(selected, selected.Target.Request, new(1), DateTimeOffset.UnixEpoch.AddSeconds(5));
        context.Envelope.ExpiresAt.Should().Be(raw.ExpiresAt);
        context.Envelope.UserRequest.Should().Be(raw.Question);
        context.Envelope.SystemPolicy.Should().Contain("user deliberately selected").And.Contain("Never claim");
        context.Envelope.Evidence.Should().ContainSingle();
        var result = context.Resolve(Success(context, Answer(context)));
        result.Claims.Single().Citations.Single().Should().BeSameAs(raw.Search.Citations[1]);
        result.RetrievalTruncated.Should().BeFalse();
        var foreign = Context(all);
        context.Resolve(Success(context, Answer(foreign))).Outcome.Should().Be(LocalFileAnswerOutcome.InvalidResponse);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("many")]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    [InlineData("clone")]
    [InlineData("excerpt")]
    [InlineData("span")]
    [InlineData("null")]
    public void SelectionIsExistingWholeCitationAuthorityNotArbitraryPathOrRange(string fault)
    {
        var all = Review();
        var citation = all.Search.Citations[0];
        IReadOnlyList<LocalFileCitation> selected = fault switch
        {
            "empty" => [],
            "many" => Enumerable.Repeat(citation, 9).ToArray(),
            "duplicate" => [citation, citation],
            "foreign" => [Review().Search.Citations[0]],
            "clone" => [citation with { }],
            "excerpt" => [citation with { Excerpt = "fabricated" }],
            "span" => [citation with { Start = citation.Start + 1 }],
            _ => [null!],
        };
        var choose = () => all.SelectEvidence(selected);
        choose.Should().Throw<InvalidDataException>();
        all.TryConsume().Should().BeFalse();
        var missing = () => all.SelectEvidence(null!);
        missing.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SelectionChangesRetirementAndConsumptionRevokeAllDerivedContextsWithoutRenewal()
    {
        var first = Review();
        var context = Context(first);
        var second = first.SelectEvidence([first.Search.Citations[1]]);
        first.TryConsume().Should().BeFalse();
        first.IsCurrent(DateTimeOffset.UnixEpoch).Should().BeFalse();
        context.Resolve(Success(context, Answer(context))).Outcome.Should().Be(LocalFileAnswerOutcome.Stale);
        second.InvalidateSelection();
        second.TryConsume().Should().BeFalse();
        var third = second.SelectEvidence([second.RetrievedSearch.Citations[0]]);
        third.IsCurrent(third.ExpiresAt.AddTicks(-1)).Should().BeTrue();
        third.IsCurrent(third.ExpiresAt).Should().BeFalse();
        var expired = () => new FolderAnswerContext(third, third.Target.Request, new(1), third.ExpiresAt);
        expired.Should().Throw<InvalidDataException>();
        third.TryConsume().Should().BeTrue();
        var reuse = () => third.SelectEvidence(third.Search.Citations);
        reuse.Should().Throw<InvalidDataException>();
        var retired = Review();
        retired.Invalidate();
        retired.IsCurrent(DateTimeOffset.UnixEpoch).Should().BeFalse();
        retired.TryConsume().Should().BeFalse();
        var revive = () => retired.SelectEvidence(retired.Search.Citations);
        revive.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void CrossFileEvidencePreservesExactIdentityLocationsBytesAndOneShotConfirmation()
    {
        var review = Review();
        var context = Context(review);
        review.TryConsume().Should().BeTrue();
        review.TryConsume().Should().BeFalse();
        review.ExcerptUtf8Bytes.Should().Be(review.Search.Citations.Sum(citation => Encoding.UTF8.GetByteCount(citation.Excerpt)));
        context.Envelope.ToolMode.Should().Be(ModelToolMode.None);
        context.Envelope.Tools.Should().BeEmpty();
        context.Envelope.Evidence.Should().HaveCount(2).And.OnlyContain(item =>
            item.Disclosure == ModelEvidenceDisclosure.LocalOnly && item.Revision.Value == 7);
        context.Envelope.Evidence.Select(item => item.Content).Should().Equal(review.Search.Citations.Select(citation => citation.Excerpt));
        context.Envelope.RequireCompleteInputLimit();
        var result = context.Resolve(Success(context, Answer(context)));
        result.Outcome.Should().Be(LocalFileAnswerOutcome.Succeeded);
        result.Claims.Single().Citations.Should().Equal(review.Search.Citations);
        foreach (var citation in result.Claims.Single().Citations)
        {
            var file = review.Source.Files.Single(file => file.Reference == citation.Source);
            LocalFileLexicalRetrieval.ResolveCitation(file, citation, LocalFileRetrievalMode.LexicalLines).Text.Should().Be(file.Text);
            citation.StartLine.Should().Be(1);
            citation.EndLine.Should().Be(2);
        }
        var second = Context(review);
        second.Resolve(Success(second, Answer(context))).Outcome.Should().Be(LocalFileAnswerOutcome.InvalidResponse);
    }

    [Theory]
    [InlineData("question")]
    [InlineData("outcome")]
    [InlineData("mode")]
    [InlineData("version")]
    [InlineData("empty")]
    [InlineData("count")]
    [InlineData("matching")]
    [InlineData("truncation")]
    [InlineData("session")]
    [InlineData("task")]
    [InlineData("origin")]
    [InlineData("invocation")]
    [InlineData("foreign-source")]
    [InlineData("duplicate")]
    [InlineData("span")]
    [InlineData("location")]
    [InlineData("excerpt")]
    public void MalformedReviewIsRejectedRatherThanOmittedOrRebound(string defect)
    {
        var review = Review();
        var search = review.Search;
        var target = review.Target;
        var first = search.Citations[0];
        search = defect switch
        {
            "outcome" => search with { Outcome = LocalFileSearchOutcome.NoMatch },
            "mode" => search with { Mode = LocalFileRetrievalMode.MarkdownStructure },
            "version" => search with { PolicyVersion = "foreign" },
            "empty" => search with { Citations = [] },
            "count" => search with { Citations = Enumerable.Repeat(first, 9).ToArray() },
            "matching" => search with { MatchingChunks = 0 },
            "truncation" => search with { Truncated = true },
            "duplicate" => search with { Citations = [first, first] },
            "foreign-source" => search with { Citations = [first with { Source = first.Source with { RevisionId = Guid.NewGuid() } }, search.Citations[1]] },
            "span" => search with { Citations = [first with { Start = 1 }, search.Citations[1]] },
            "location" => search with { Citations = [first with { EndLine = 99 }, search.Citations[1]] },
            "excerpt" => search with { Citations = [first with { Excerpt = "fabricated" }, search.Citations[1]] },
            _ => search,
        };
        var input = target.SourceRequest;
        input = defect switch
        {
            "session" => new(input.RequestId, new(Guid.NewGuid()), input.TaskId, input.Origin),
            "task" => new(input.RequestId, input.SessionId, new(Guid.NewGuid()), input.Origin),
            "origin" => new(input.RequestId, input.SessionId, input.TaskId, RequestOrigin.ActivatedVoice),
            "invocation" => new(input.RequestId, input.SessionId, input.TaskId, input.Origin, new(Guid.NewGuid())),
            _ => input,
        };
        target = LocalContextSessionTarget.ReviewedFresh(input, HostRequest.Create(input.Origin));
        var create = () => new FolderAnswerReview(review.Source,
            string.Equals(defect, "question", StringComparison.Ordinal) ? "!!!" : review.Question, search, target);
        create.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void NullAndUnadmittedTargetsAreExplicitFailures()
    {
        var review = Review();
        var sourceNull = () => new FolderAnswerReview(null!, review.Question, review.Search, review.Target);
        var searchNull = () => new FolderAnswerReview(review.Source, review.Question, null!, review.Target);
        var targetNull = () => new FolderAnswerReview(review.Source, review.Question, review.Search, null!);
        var reviewNull = () => new FolderAnswerContext(null!, review.Target.Request, new(1), DateTimeOffset.UnixEpoch);
        sourceNull.Should().Throw<ArgumentNullException>();
        searchNull.Should().Throw<ArgumentNullException>();
        targetNull.Should().Throw<ArgumentNullException>();
        reviewNull.Should().Throw<ArgumentNullException>();
        var foreign = () => new FolderAnswerContext(review, HostRequest.Create(RequestOrigin.LocalUi), new(1), DateTimeOffset.UnixEpoch);
        var generation = () => new FolderAnswerContext(review, review.Target.Request, default, DateTimeOffset.UnixEpoch);
        foreign.Should().Throw<InvalidDataException>();
        generation.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"unsupported\":true,\"claims\":[],\"path\":\"hidden\"}")]
    [InlineData("{\"unsupported\":true,\"unsupported\":true,\"claims\":[]}")]
    [InlineData("{\"unsupported\":false,\"claims\":[]}")]
    [InlineData("{\"unsupported\":\"false\",\"claims\":[]}")]
    [InlineData("{\"unsupported\":true,\"claims\":{}}")]
    [InlineData("{\"unsupported\":false,\"claims\":[{\"text\":\"fabricated\",\"evidenceIds\":[]}]}")]
    [InlineData("{\"unsupported\":false,\"claims\":[{\"text\":\"fabricated\",\"evidenceIds\":[\"00000000-0000-0000-0000-000000000000\"]}]}")]
    [InlineData("{\"unsupported\":false,\"claims\":[{\"text\":null,\"evidenceIds\":[]}]}")]
    [InlineData("{\"unsupported\":false,\"claims\":[{\"text\":42,\"evidenceIds\":[42]}]}")]
    [InlineData("{\"unsupported\":false,\"claims\":[{\"text\":\"x\",\"evidenceIds\":{}}]}")]
    public void MalformedAnswersNeverPublishClaims(string answer)
    {
        var context = Context(Review());
        var result = context.Resolve(Success(context, answer));
        result.Outcome.Should().Be(LocalFileAnswerOutcome.InvalidResponse);
        result.Claims.Should().BeEmpty();
    }

    [Theory]
    [InlineData("duplicate-id")]
    [InlineData("numeric-id")]
    [InlineData("empty-text")]
    [InlineData("unknown-field")]
    [InlineData("too-many-ids")]
    [InlineData("too-many-claims")]
    [InlineData("overflow")]
    [InlineData("foreign-request")]
    [InlineData("hosted")]
    [InlineData("action")]
    [InlineData("null")]
    [InlineData("no-provenance")]
    [InlineData("grant")]
    [InlineData("question")]
    [InlineData("no-answer")]
    public void InvalidProviderPayloadIsNotCitationOrExecutionAuthority(string defect)
    {
        var context = Context(Review());
        var id = context.Envelope.Evidence[0].Id.Value.ToString("D");
        var claim = new { text = "marker", evidenceIds = new[] { id } };
        var json = defect switch
        {
            "duplicate-id" => JsonSerializer.Serialize(new { unsupported = false, claims = new[] { new { text = "x", evidenceIds = new[] { id, id } } } }),
            "numeric-id" => "{\"unsupported\":false,\"claims\":[{\"text\":\"x\",\"evidenceIds\":[42]}]}",
            "empty-text" => JsonSerializer.Serialize(new { unsupported = false, claims = new[] { new { text = " ", evidenceIds = new[] { id } } } }),
            "unknown-field" => JsonSerializer.Serialize(new { unsupported = false, claims = new[] { new { claim.text, claim.evidenceIds, path = "x" } } }),
            "too-many-ids" => JsonSerializer.Serialize(new { unsupported = false, claims = new[] { new { text = "x", evidenceIds = Enumerable.Repeat(id, 9) } } }),
            "too-many-claims" => JsonSerializer.Serialize(new { unsupported = false, claims = Enumerable.Repeat(claim, 9) }),
            "overflow" => new string('x', ModelContextEnvelope.MaximumOutputUtf8Bytes + 1),
            _ => JsonSerializer.Serialize(new { unsupported = false, claims = new[] { claim } }),
        };
        var reply = Success(context, json);
        reply = defect switch
        {
            "foreign-request" => reply with { Provenance = reply.Provenance! with { Request = HostRequest.Create(RequestOrigin.LocalUi) } },
            "hosted" => reply with { Provenance = reply.Provenance! with { Selection = ModelProviderSelection.CopilotCandidate } },
            "action" => reply with { Response = new(json, BuiltInAction.LockMachine) },
            "null" => reply with { Response = null },
            "no-provenance" => reply with { Provenance = null },
            "grant" => reply with { Response = new(json, null, new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Once)) },
            "question" => reply with { Response = new(json, null, Question: new("choice", ["one", "two"])) },
            "no-answer" => reply with { Response = new(null, null) },
            _ => reply,
        };
        context.Resolve(reply).Outcome.Should().Be(LocalFileAnswerOutcome.InvalidResponse);
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Denied, LocalFileAnswerOutcome.Denied)]
    [InlineData(ModelTurnOutcome.DeniedEgress, LocalFileAnswerOutcome.Denied)]
    [InlineData(ModelTurnOutcome.Cancelled, LocalFileAnswerOutcome.Cancelled)]
    [InlineData(ModelTurnOutcome.Unavailable, LocalFileAnswerOutcome.Unavailable)]
    public void NonSuccessHasNoClaims(ModelTurnOutcome outcome, LocalFileAnswerOutcome expected)
    {
        var context = Context(Review());
        context.Resolve(new(outcome, ModelTurnReason.QualificationPending)).Outcome.Should().Be(expected);
    }

    [Fact]
    public void UnsupportedAndBoundedOmissionsAreTruthfulNotFabricatedAnswers()
    {
        var review = Review();
        var truncated = new FolderAnswerReview(review.Source, review.Question,
            review.Search with { MatchingChunks = 3, Truncated = true }, review.Target);
        truncated = truncated.SelectEvidence(truncated.Search.Citations);
        var context = Context(truncated);
        var result = context.Resolve(Success(context, "{\"unsupported\":true,\"claims\":[]}"));
        result.Outcome.Should().Be(LocalFileAnswerOutcome.Unsupported);
        result.MatchingChunks.Should().Be(3);
        result.RetrievalTruncated.Should().BeTrue();
        result.Claims.Should().BeEmpty();
    }

    [Fact]
    public void ExactExcerptByteLimitAndSerializedEscapingAreMeasuredIndependentlyWithoutDroppingEvidence()
    {
        var text = "alpha " + new string('"', LocalFileRetrievalPolicy.MaximumChunkCharacters - 6);
        var review = Review(text, 8);
        review.Search.Citations.Should().HaveCount(8);
        review.ExcerptUtf8Bytes.Should().Be(LocalFileRetrievalPolicy.MaximumExcerptUtf8Bytes);
        var context = Context(review);
        context.Envelope.Evidence.Should().HaveCount(8);
        context.Envelope.Serialize().Length.Should().BeGreaterThan(ModelContextEnvelope.MaximumInputUtf8Bytes);
        var limit = () => context.Envelope.RequireCompleteInputLimit();
        limit.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ExcessiveExcerptBytesRejectTheCompleteProposalRatherThanSilentlyRemovingAnItem()
    {
        var text = "alpha " + new string('\u4e00', 2042);
        var review = Review(text, 8);
        var citations = review.Source.Files.SelectMany(file => new LocalFileLexicalRetrieval()
            .Search(file, file.Reference, "alpha", DateTimeOffset.UnixEpoch, CancellationToken.None).Citations).ToArray();
        citations.Should().HaveCount(8);
        var excessive = review.Search with { Citations = citations, MatchingChunks = 8, Truncated = false };
        var create = () => new FolderAnswerReview(review.Source, review.Question, excessive, review.Target);
        create.Should().Throw<InvalidDataException>();
        review.Source.Files.Should().HaveCount(8);
    }
}
