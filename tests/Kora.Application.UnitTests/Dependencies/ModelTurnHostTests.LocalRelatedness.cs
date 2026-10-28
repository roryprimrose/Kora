using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting.SessionRouting;
using Kora.Core.Conversation.SessionRouting;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task LocalRelatednessUsesOneInertMetadataEnvelopeNoToolsAndOriginalChannel(RequestOrigin origin)
    {
        using var f = new Fixture(origin);
        using var root = f.Root();
        var snapshot = RoutingSnapshot();
        f.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
            new(RoutingAnswer(snapshot), null));
        var matcher = new LocalSessionRelatednessMatcher(f.PolicyHost, f.PolicyHost, f.Time);
        const string body = "  original\r\nunchanged  ";
        var result = await matcher.MatchAsync(body, snapshot, static () => true, Token);
        result.Candidates.Should().Equal(snapshot.Descriptors[0].Candidate);
        result.ExpectedRevision.Should().Be(snapshot.Revision);
        f.Adapter.Calls.Should().Be(1);
        using var wire = JsonDocument.Parse(f.Adapter.Wire!);
        wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(body);
        wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
        wire.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(1);
        f.Adapter.Provenance!.Request.Origin.Should().Be(origin);
        f.PolicyHost.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Unavailable)]
    [InlineData(ModelTurnOutcome.Denied)]
    [InlineData(ModelTurnOutcome.Unknown)]
    [InlineData(ModelTurnOutcome.Cancelled)]
    public async Task OnlyExplicitUnavailableInferenceMayOfferNewAndNoFailureAutomaticallyFallsBack(ModelTurnOutcome outcome)
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.Adapter.Reply = new(outcome, ModelProviderSelection.OllamaCandidate);
        var matcher = new LocalSessionRelatednessMatcher(f.PolicyHost, f.PolicyHost, f.Time);
        var operation = () => matcher.MatchAsync("original", RoutingSnapshot(), static () => true, Token);
        if (outcome == ModelTurnOutcome.Unavailable) { (await operation()).Match.Should().Be(SessionRouteMatch.Unavailable); }
        else if (outcome == ModelTurnOutcome.Cancelled) { await operation.Should().ThrowAsync<OperationCanceledException>(); }
        else { await operation.Should().ThrowAsync<InvalidOperationException>(); }
        f.Adapter.Calls.Should().Be(1);
        f.PolicyHost.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalRelatednessChecksOriginalAdmissionOnBothSidesOfInference(bool after)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var live = after;
        var snapshot = RoutingSnapshot();
        f.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
            new(RoutingAnswer(snapshot), null));
        f.Adapter.OnRun = () => live = false;
        var matcher = new LocalSessionRelatednessMatcher(f.PolicyHost, f.PolicyHost, f.Time);
        await matcher.Invoking(subject => subject.MatchAsync("original", snapshot, () => live, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Adapter.Calls.Should().Be(after ? 1 : 0);
        f.PolicyHost.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task UnqualifiedLocalRoutingNeverContactsAProvider()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var host = f.Production();
        var result = await new LocalSessionRelatednessMatcher(host, host, f.Time)
            .MatchAsync("original", RoutingSnapshot(), static () => true, Token);
        result.Match.Should().Be(SessionRouteMatch.Unavailable);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingAnswerFromExecutionPortCannotBecomeAnEmptyRoute(bool response)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var execution = new MissingRoutingAnswerExecution(response);
        var matcher = new LocalSessionRelatednessMatcher(f.PolicyHost, execution, f.Time);
        await matcher.Invoking(subject => subject.MatchAsync("original", RoutingSnapshot(), static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        execution.Joined.Should().BeTrue();
        f.Adapter.Calls.Should().Be(0);
    }

    private sealed class MissingRoutingAnswerExecution(bool response) : IModelTurnExecution
    {
        internal bool Joined { get; private set; }
        public Task<ModelTurnResult> RunAsync(ModelTurn turn, Func<bool> admitted, CancellationToken token,
            Func<CancellationToken, Task>? validateSource = null) =>
            Task.FromResult(new ModelTurnResult(ModelTurnOutcome.Succeeded, ModelTurnReason.None,
                Response: response ? new(null, null) : null));
        public Task WaitForQuiescenceAsync(CancellationToken cancellationToken)
        {
            Joined = true;
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("array")]
    [InlineData("duplicate")]
    [InlineData("extra")]
    [InlineData("revision")]
    [InlineData("match-type")]
    [InlineData("unknown")]
    [InlineData("candidates-type")]
    [InlineData("count")]
    [InlineData("candidate-duplicate")]
    [InlineData("candidate-extra")]
    [InlineData("index-type")]
    [InlineData("negative")]
    [InlineData("outside")]
    [InlineData("evidence-type")]
    [InlineData("evidence-format")]
    [InlineData("oversize")]
    public void StrictLocalRoutingProtocolRejectsMalformedOversizedAndDuplicateFields(string fault)
    {
        var snapshot = RoutingSnapshot();
        var answer = fault switch
        {
            "json" => "{",
            "array" => "[]",
            "duplicate" => "{\"revision\":7,\"revision\":7,\"match\":\"none\",\"candidates\":[]}",
            "extra" => "{\"revision\":7,\"match\":\"none\",\"candidates\":[],\"permission\":true}",
            "revision" => "{\"revision\":\"7\",\"match\":\"none\",\"candidates\":[]}",
            "match-type" => "{\"revision\":7,\"match\":7,\"candidates\":[]}",
            "unknown" => "{\"revision\":7,\"match\":\"unavailable\",\"candidates\":[]}",
            "candidates-type" => "{\"revision\":7,\"match\":\"none\",\"candidates\":{}}",
            "count" => JsonSerializer.Serialize(new { revision = 7, match = "clear", candidates = Enumerable.Repeat(new { index = 0, evidence = Guid.Empty }, 51) }),
            "candidate-duplicate" => "{\"revision\":7,\"match\":\"clear\",\"candidates\":[{\"index\":0,\"index\":0,\"evidence\":\"00000000-0000-0000-0000-000000000000\"}]}",
            "candidate-extra" => "{\"revision\":7,\"match\":\"clear\",\"candidates\":[{\"index\":0,\"evidence\":\"00000000-0000-0000-0000-000000000000\",\"id\":\"foreign\"}]}",
            "index-type" => "{\"revision\":7,\"match\":\"clear\",\"candidates\":[{\"index\":\"0\",\"evidence\":\"00000000-0000-0000-0000-000000000000\"}]}",
            "negative" => "{\"revision\":7,\"match\":\"clear\",\"candidates\":[{\"index\":-1,\"evidence\":\"00000000-0000-0000-0000-000000000000\"}]}",
            "outside" => "{\"revision\":7,\"match\":\"clear\",\"candidates\":[{\"index\":50,\"evidence\":\"00000000-0000-0000-0000-000000000000\"}]}",
            "evidence-type" => "{\"revision\":7,\"match\":\"clear\",\"candidates\":[{\"index\":0,\"evidence\":0}]}",
            "evidence-format" => "{\"revision\":7,\"match\":\"clear\",\"candidates\":[{\"index\":0,\"evidence\":\"wrong\"}]}",
            _ => new string('x', ModelContextEnvelope.MaximumOutputUtf8Bytes + 1),
        };
        var parse = () => LocalSessionRelatednessMatcher.Parse(answer, snapshot);
        parse.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("ambiguous")]
    [InlineData("none")]
    public void LocalRoutingProtocolPreservesHostCandidatesEvidenceAndRevisionForSharedValidation(string match)
    {
        var snapshot = RoutingSnapshot();
        var count = match is "clear" ? 1 : match is "ambiguous" ? 2 : 0;
        var answer = JsonSerializer.Serialize(new
        {
            revision = snapshot.Revision, match,
            candidates = snapshot.Descriptors.Take(count).Select((item, index) => new { index, evidence = item.EvidenceId }),
        });
        var result = LocalSessionRelatednessMatcher.Parse(answer, snapshot);
        result.ExpectedRevision.Should().Be(snapshot.Revision);
        result.Candidates.Should().Equal(snapshot.Descriptors.Take(count).Select(item => item.Candidate));
        var padding = ModelContextEnvelope.MaximumOutputUtf8Bytes - Encoding.UTF8.GetByteCount(answer);
        LocalSessionRelatednessMatcher.Parse(answer + new string(' ', padding), snapshot).Should().BeEquivalentTo(result);
    }

    private static SessionRouteSnapshot RoutingSnapshot()
    {
        static SessionRouteDescriptor Entry(string name)
        {
            var id = new HostId<SessionIdentity>(Guid.NewGuid());
            return new(new(new(id, new(1), true), new(id, new(1), new(name))), Guid.NewGuid());
        }
        return new(7, [Entry("Alpha"), Entry("Beta")]);
    }

    private static string RoutingAnswer(SessionRouteSnapshot snapshot) =>
        JsonSerializer.Serialize(new
        {
            revision = snapshot.Revision, match = "clear",
            candidates = new[] { new { index = 0, evidence = snapshot.Descriptors[0].EvidenceId } },
        });
}
