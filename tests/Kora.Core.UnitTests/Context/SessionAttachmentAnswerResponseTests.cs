using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Context;

public sealed class SessionAttachmentAnswerResponseTests
{
    [Fact]
    public void ResolvedClaimRetainsExactExistingSourceCoordinatesAndNeverModelSelectedLocations()
    {
        var (source, search, binding) = Create();
        var result = SessionAttachmentAnswerResponse.Resolve(source, binding, search, Answer(binding, "The marker is alpha."));
        result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        var body = result.Response!.Answer!;
        body.Should().Contain("The marker is alpha.")
            .And.Contain(source.Session.Value.ToString("D"))
            .And.Contain(source.File.Reference.RevisionId.ToString("D"))
            .And.Contain(source.File.Reference.ItemId.ToString("D"))
            .And.Contain(source.File.Digest)
            .And.Contain($"UTF-16 {search.Citations[0].Start}+{search.Citations[0].Length}")
            .And.NotContain(@"C:\Team");
        body.Should().NotContain(binding.Envelope.Evidence[0].Id.Value.ToString("D"));
    }

    [Theory]
    [InlineData("invalid-json")]
    [InlineData("unknown-citation")]
    [InlineData("secret")]
    [InlineData("output-budget")]
    [InlineData("truncated")]
    public void InvalidOrUnsafeGroundingCannotBeRetainedAsSuccess(string fault)
    {
        var (source, search, binding) = Create();
        var result = Answer(binding, fault is "secret" ? "api_key=synthetic-only-value"
            : fault is "output-budget" ? new string('a', 3900) : "alpha");
        if (fault is "invalid-json") { result = result with { Response = new("not JSON", null) }; }
        if (fault is "unknown-citation")
        {
            result = result with { Response = new("{\"unsupported\":false,\"claims\":[{\"text\":\"alpha\",\"evidenceIds\":[\"00000000-0000-0000-0000-000000000000\"]}]}", null) };
        }
        if (fault is "truncated") { search = search with { Truncated = true }; }
        var observed = SessionAttachmentAnswerResponse.Resolve(source, binding, search, result);
        observed.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        observed.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        observed.Response.Should().BeNull();
    }

    [Fact]
    public void UnsupportedEvidenceIsExplicitAndUnsuccessfulModelResultsArePreserved()
    {
        var (source, search, binding) = Create();
        var result = Answer(binding, "unused") with { Response = new("{\"unsupported\":true,\"claims\":[]}", null) };
        var observed = SessionAttachmentAnswerResponse.Resolve(source, binding, search, result);
        observed.Response!.Answer.Should().Be("The selected evidence does not support an answer.");
        var denied = new ModelTurnResult(ModelTurnOutcome.Unavailable, ModelTurnReason.QualificationPending);
        SessionAttachmentAnswerResponse.Resolve(source, binding, search, denied).Should().BeSameAs(denied);
    }

    [Theory]
    [InlineData(4096, ModelTurnOutcome.Succeeded)]
    [InlineData(4097, ModelTurnOutcome.Unknown)]
    public void CompleteRenderedReferencesRespectExactSerializedOutputBudget(int bytes, ModelTurnOutcome outcome)
    {
        var (source, search, binding) = Create();
        var minimum = SessionAttachmentAnswerResponse.Resolve(source, binding, search, Answer(binding, "x"));
        var framing = JsonSerializer.SerializeToUtf8Bytes(minimum.Response).Length;
        var result = SessionAttachmentAnswerResponse.Resolve(source, binding, search,
            Answer(binding, new string('x', 1 + bytes - framing)));
        result.Outcome.Should().Be(outcome);
        if (outcome == ModelTurnOutcome.Succeeded)
        { JsonSerializer.SerializeToUtf8Bytes(result.Response).Length.Should().Be(bytes); }
        else { result.Response.Should().BeNull(); }
    }

    private static (SessionFileAttachment Source, LocalFileSearchResult Search, LocalFileAnswerContext Binding) Create()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var bytes = Encoding.UTF8.GetBytes("# Source\nalpha marker");
        var file = new LocalFileRevision(new(Guid.NewGuid(), Guid.NewGuid(), request,
            new(@"C:\Team\guide.md", "identity", bytes.Length, DateTimeOffset.UnixEpoch)), bytes, DateTimeOffset.UnixEpoch);
        var source = new SessionFileAttachment(request.SessionId, new(1), new(2), file);
        var search = new LocalFileLexicalRetrieval().Search(file, file.Reference, "alpha", DateTimeOffset.UnixEpoch, CancellationToken.None);
        return (source, search, new(file, search, "alpha", request, new(1), DateTimeOffset.UnixEpoch));
    }

    private static ModelTurnResult Answer(LocalFileAnswerContext binding, string claim) =>
        new(ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome,
            new(new(Guid.NewGuid()), binding.Envelope.Request, ModelProviderSelection.OllamaCandidate),
            new(JsonSerializer.Serialize(new
            {
                unsupported = false,
                claims = new[] { new { text = claim, evidenceIds = binding.Envelope.Evidence.Select(item => item.Id.Value) } },
            }), null));
}
