using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Context;

public sealed class LocalFileAnswerContextTests
{
    private static (LocalFileRevision Source, LocalFileSearchResult Search, LocalFileAnswerContext Context) Create()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var bytes = Encoding.UTF8.GetBytes("# Header\r\nalpha exact marker");
        var source = new LocalFileRevision(new(Guid.NewGuid(), Guid.NewGuid(), request,
            new(@"C:\Team\guide.md", "identity", bytes.Length, DateTimeOffset.UnixEpoch)), bytes, DateTimeOffset.UnixEpoch);
        var search = new LocalFileLexicalRetrieval().Search(source, source.Reference, "alpha", DateTimeOffset.UnixEpoch, CancellationToken.None);
        return (source, search, new(source, search, "alpha?", request, new(1), DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void ContextPreservesExactEvidenceAndResolvesHostHeldCitationRatherThanModelLocations()
    {
        var (source, search, context) = Create();
        context.Envelope.Evidence.Single().Disclosure.Should().Be(ModelEvidenceDisclosure.LocalOnly);
        context.Envelope.Evidence.Single().Content.Should().Be(search.Citations.Single().Excerpt);
        var result = context.Resolve(Success(context, JsonSerializer.Serialize(new
        {
            unsupported = false,
            claims = new[] { new { text = "An exact marker is present.", evidenceIds = context.Envelope.Evidence.Select(item => item.Id.Value).ToArray() } },
        })), search);
        result.Outcome.Should().Be(LocalFileAnswerOutcome.Succeeded);
        result.Claims.Single().Citations.Single().Should().BeSameAs(search.Citations.Single());
        LocalFileLexicalRetrieval.ResolveCitation(source, result.Claims.Single().Citations.Single(),
            LocalFileRetrievalMode.LexicalLines).Text.Should().Be(source.Text);
        context.Envelope.RequireCompleteInputLimit();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"unsupported\":true,\"claims\":[],\"path\":\"C:\\\\hidden.txt\"}")]
    [InlineData("{\"unsupported\":true,\"unsupported\":true,\"claims\":[]}")]
    [InlineData("{\"unsupported\":false,\"claims\":[]}")]
    [InlineData("{\"unsupported\":\"false\",\"claims\":[]}")]
    [InlineData("{\"unsupported\":true,\"claims\":{}}")]
    [InlineData("{\"unsupported\":false,\"claims\":[{\"text\":\"fabricated\",\"evidenceIds\":[]}]}")]
    [InlineData("{\"unsupported\":false,\"claims\":[{\"text\":\"fabricated\",\"evidenceIds\":[\"00000000-0000-0000-0000-000000000000\"]}]}")]
    [InlineData("{\"unsupported\":false,\"claims\":[{\"text\":null,\"evidenceIds\":[]}]}")]
    [InlineData("{\"unsupported\":false,\"claims\":[{\"text\":42,\"evidenceIds\":[42]}]}")]
    public void MalformedUnknownDuplicateAndFabricatedCitationsProduceNoClaims(string answer)
    {
        var (_, search, context) = Create();
        var result = context.Resolve(Success(context, answer), search);
        result.Outcome.Should().Be(LocalFileAnswerOutcome.InvalidResponse);
        result.Claims.Should().BeEmpty();
    }

    [Theory]
    [InlineData("duplicate-id")]
    [InlineData("numeric-id")]
    [InlineData("empty-text")]
    [InlineData("unknown-field")]
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
    public void InvalidProviderPayloadCannotBecomeAnswerAuthority(string defect)
    {
        var (_, search, context) = Create();
        var id = context.Envelope.Evidence.Single().Id.Value.ToString("D");
        var claim = new { text = "Exact marker", evidenceIds = new[] { id } };
        var json = defect switch
        {
            "duplicate-id" => JsonSerializer.Serialize(new { unsupported = false, claims = new[] { new { text = "x", evidenceIds = new[] { id, id } } } }),
            "numeric-id" => "{\"unsupported\":false,\"claims\":[{\"text\":\"x\",\"evidenceIds\":[42]}]}",
            "empty-text" => JsonSerializer.Serialize(new { unsupported = false, claims = new[] { new { text = " ", evidenceIds = new[] { id } } } }),
            "unknown-field" => JsonSerializer.Serialize(new { unsupported = false, claims = new[] { new { claim.text, claim.evidenceIds, path = "x" } } }),
            "too-many-claims" => JsonSerializer.Serialize(new { unsupported = false, claims = Enumerable.Repeat(claim, 9) }),
            "overflow" => new string('x', ModelContextEnvelope.MaximumOutputUtf8Bytes + 1),
            _ => JsonSerializer.Serialize(new { unsupported = false, claims = new[] { claim } }),
        };
        var reply = Success(context, json);
        reply = defect switch
        {
            "foreign-request" => reply with { Provenance = reply.Provenance! with { Request = HostRequest.Create(RequestOrigin.LocalUi) } },
            "hosted" => reply with { Provenance = reply.Provenance! with { Selection = ModelProviderSelection.CopilotCandidate } },
            "action" => reply with { Response = new(json, Kora.Core.Commands.BuiltInAction.LockMachine) },
            "null" => reply with { Response = null },
            "no-provenance" => reply with { Provenance = null },
            "grant" => reply with { Response = new(json, null,
                new(Kora.Core.Commands.GrantChangeOperation.Add, Kora.Core.Commands.BuiltInAction.LockMachine,
                    Kora.Core.Commands.ModelApprovalScope.Once)) },
            "question" => reply with { Response = new(json, null, Question: new("choice", ["one", "two"])) },
            "no-answer" => reply with { Response = new(null, null) },
            _ => reply,
        };
        context.Resolve(reply, search).Outcome.Should().Be(LocalFileAnswerOutcome.InvalidResponse);
    }

    [Fact]
    public void UnsupportedEvidenceIsAnHonestAdmittedModelOutcomeWithoutInventedCitation()
    {
        var (_, search, context) = Create();
        var result = context.Resolve(Success(context, "{\"unsupported\":true,\"claims\":[]}"), search);
        result.Outcome.Should().Be(LocalFileAnswerOutcome.Unsupported);
        result.Claims.Should().BeEmpty();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("span")]
    [InlineData("location")]
    [InlineData("excerpt")]
    [InlineData("version")]
    [InlineData("mode")]
    [InlineData("query")]
    [InlineData("generation")]
    [InlineData("empty")]
    [InlineData("outcome")]
    [InlineData("count")]
    [InlineData("matching-count")]
    [InlineData("session")]
    [InlineData("task")]
    [InlineData("origin")]
    [InlineData("invocation")]
    public void ContextRejectsStaleOrForgedSourceLocationAndInvalidSelection(string defect)
    {
        var (source, search, _) = Create();
        var citation = search.Citations.Single();
        citation = defect switch
        {
            "source" => citation with { Source = source.Reference with { RevisionId = Guid.NewGuid() } },
            "span" => citation with { Start = citation.Start + 1 },
            "location" => citation with { StartLine = 99 },
            "excerpt" => citation with { Excerpt = "forged" },
            _ => citation,
        };
        search = search with { Citations = [citation] };
        if (string.Equals(defect, "version", StringComparison.Ordinal)) { search = search with { PolicyVersion = "unadmitted" }; }
        if (string.Equals(defect, "mode", StringComparison.Ordinal)) { search = search with { Mode = LocalFileRetrievalMode.MarkdownStructure }; }
        if (string.Equals(defect, "empty", StringComparison.Ordinal)) { search = search with { Citations = [] }; }
        if (string.Equals(defect, "outcome", StringComparison.Ordinal)) { search = search with { Outcome = LocalFileSearchOutcome.NoMatch }; }
        if (string.Equals(defect, "count", StringComparison.Ordinal)) { search = search with { Citations = Enumerable.Repeat(citation, 9).ToArray() }; }
        if (string.Equals(defect, "matching-count", StringComparison.Ordinal)) { search = search with { MatchingChunks = 0 }; }
        var original = source.Review.Request;
        var request = defect switch
        {
            "session" => HostRequest.Create(RequestOrigin.LocalUi),
            "task" => new(original.RequestId, original.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            "origin" => new(original.RequestId, original.SessionId, original.TaskId, RequestOrigin.HostSystem),
            "invocation" => new(original.RequestId, original.SessionId, original.TaskId, RequestOrigin.LocalUi, new(Guid.NewGuid())),
            _ => original,
        };
        var create = () => new LocalFileAnswerContext(source, search, string.Equals(defect, "query", StringComparison.Ordinal) ? string.Empty : "alpha",
            request, string.Equals(defect, "generation", StringComparison.Ordinal) ? default : new(1), DateTimeOffset.UnixEpoch);
        create.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void AggregateExcerptBudgetCannotBeBypassedByOtherwiseValidExactCitations()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var bytes = Encoding.UTF8.GetBytes(string.Join("\n\n", Enumerable.Range(0, 8).Select(index => $"a{index} " + new string('\u4e00', 1900))));
        var source = new LocalFileRevision(new(Guid.NewGuid(), Guid.NewGuid(), request,
            new(@"C:\Team\guide.md", "identity", bytes.Length, DateTimeOffset.UnixEpoch)), bytes, DateTimeOffset.UnixEpoch);
        var citations = Enumerable.Range(0, 8).Select(index => new LocalFileLexicalRetrieval().Search(source, source.Reference,
            $"a{index}", DateTimeOffset.UnixEpoch, CancellationToken.None).Citations.Single()).ToArray();
        var search = new LocalFileSearchResult(LocalFileSearchOutcome.Matched, DateTimeOffset.UnixEpoch, citations, 8, false);
        var create = () => new LocalFileAnswerContext(source, search, "alpha", request, new(1), DateTimeOffset.UnixEpoch);
        create.Should().Throw<InvalidDataException>().WithMessage("*budget*");
    }

    [Theory]
    [InlineData("answer file", LocalFileOperation.Answer)]
    [InlineData("answer file arbitrary question", LocalFileOperation.Invalid)]
    [InlineData("answer file C:\\Team\\hidden.md", LocalFileOperation.Invalid)]
    public void CommandOnlyOpensTheExplicitNativeQuestionWithoutPathOrInlineContentAuthority(string command, LocalFileOperation operation)
    {
        LocalFileCommand.Parse(command)!.Operation.Should().Be(operation);
        LocalFileCommand.FixedPhrases.Should().Contain("answer file");
    }

    private static ModelTurnResult Success(LocalFileAnswerContext context, string answer) =>
        new(ModelTurnOutcome.Succeeded, ModelTurnReason.None,
            new(new(Guid.NewGuid()), context.Envelope.Request, ModelProviderSelection.OllamaCandidate), new(answer, null));
}
