using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Application.Tools;
using Kora.Core.Agents;
using Kora.Core.Artifacts;
using Kora.Core.Customizations;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Fact]
    public async Task AgentRecommendationCannotBorrowAQualifiedHostedAccountOrBypassFinalEgressReview()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var host = f.Qualified(ModelProviderSelection.CopilotCandidate);
        var context = new ModelContextEnvelope(f.Request, ModelAgentUseProposal.ModelPolicy, "Original question",
            [new(new(Guid.NewGuid()), f.Request, new(1), "PRIVATE-SOURCE-OR-CHILD-OUTPUT", ModelEvidenceDisclosure.LocalOnly)],
            f.Time.Now, f.Time.Now.AddSeconds(15), ModelToolMode.CustomizationDiscovery);
        var admission = await host.AdmitAsync(ModelProviderSelection.CopilotCandidate, context, Token);
        admission.Result.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        admission.Result.Reason.Should().Be(ModelTurnReason.EgressNotAdmitted);
        admission.Turn.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        f.ExecutionStarts.Should().Be(0);
        host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("success")]
    [InlineData("unserved")]
    [InlineData("foreign")]
    [InlineData("skill")]
    [InlineData("disabled")]
    [InlineData("stale")]
    [InlineData("no-observation")]
    [InlineData("malformed")]
    [InlineData("unbound")]
    public async Task OnlyOneServedEnabledRegisteredAgentCanProduceUntrustedSourceBoundReviewData(string scenario)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var sources = new MetadataSources();
        var use = new MetadataUse();
        using var catalogue = sources.Catalogue(use);
        if (scenario is "disabled")
        { catalogue.Update(items => items.Select(item => item with { IsUseEnabled = false }).ToArray()); }
        using var snapshot = await new CustomizationDiscoveryService(sources, catalogue, use, f, f.Time)
            .Review(sources.Values.Select(value => value.Source).ToArray(), new(f.Session, null),
                "Original request", RequestOrigin.LocalUi, static () => true, Token, proposeAgent: true);
        snapshot.ProposeAgent.Should().BeTrue();
        snapshot.Consume();
        if (scenario is not "no-observation")
        {
            var input = scenario is "unserved" ? "{\"count\":1}" : "{}";
            (await snapshot.Invoke(CustomizationDiscoveryTools.List, input, Token)).Outcome.Should().Be(CapabilityOutcome.Succeeded);
        }
        var reference = scenario is "foreign" ? Guid.NewGuid().ToString("N")
            : snapshot.Records[scenario is "skill" ? 0 : 1].Ref;
        if (scenario is "stale") { snapshot.Retire(); }
        if (scenario is "unbound")
        {
            catalogue.Update(items => items.Select(item => item with { UseControlId = "different.identity" }).ToArray());
        }
        var result = new ModelTurnResult(ModelTurnOutcome.Succeeded, ModelTurnReason.None,
            new(new(Guid.NewGuid()), f.Request, ModelProviderSelection.OllamaCandidate),
            new(scenario is "malformed" ? "not a proposal" : JsonSerializer.Serialize(new { agentRef = reference }), null));
        var projected = snapshot.Resolve(result);
        if (scenario is "success")
        {
            projected.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
            projected.Response!.Answer.Should().Contain("No agent has run").And.NotContain("BODY-SECRET");
            var proposal = snapshot.AgentProposal!;
            proposal.Definition.Should().Be(catalogue.Candidates[1]);
            proposal.Target.Authority.Should().Be(f.Session);
            proposal.Input.Should().Be("Original request");
            proposal.Origin.Should().Be(RequestOrigin.LocalUi);
            snapshot.Dispose();
            proposal.IsCurrent().Should().BeTrue("the parent has physically finished; the separate native review still requires current source and user admission");
            await proposal.ConsumeForReviewAsync(Token);
            await proposal.ValidateAsync(Token);
            await proposal.Invoking(item => item.ConsumeForReviewAsync(Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            projected.Outcome.Should().Be(ModelTurnOutcome.Unknown);
            projected.Response.Should().BeNull();
            snapshot.AgentProposal.Should().BeNull();
        }
        f.ExecutionStarts.Should().Be(0);
        f.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("caller")]
    [InlineData("source")]
    [InlineData("late-caller")]
    [InlineData("cancelled")]
    public async Task ProposedAgentDataRevalidatesSourceAndOriginalAdmissionWithoutAcquiringAnyExecutionAuthority(string scenario)
    {
        using var f = new Fixture();
        var sources = new MetadataSources();
        var use = new MetadataUse();
        using var catalogue = sources.Catalogue(use);
        var current = true;
        var observation = sources.Values[1];
        var proposal = new ProposedBoundedAgentUse(catalogue.Candidates[1], observation, sources,
            new(f.Session, null), "Original input", RequestOrigin.ActivatedVoice, () => current);
        if (scenario is "caller") { current = false; }
        if (scenario is "source") { sources.Values[1] = observation with { Revision = "new-source" }; }
        if (scenario is "late-caller") { sources.AfterRead = () => current = false; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (scenario is "cancelled") { await cancellation.CancelAsync(); }
        await proposal.Invoking(item => item.ValidateAsync(cancellation.Token)).Should().ThrowAsync<Exception>();
        proposal.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        f.ExecutionStarts.Should().Be(0);
        f.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("caller")]
    [InlineData("clock")]
    [InlineData("expiry")]
    [InlineData("use")]
    [InlineData("reload")]
    [InlineData("catalogue")]
    public async Task ParentCompletionDoesNotRenewProposalAuthorityAcrossChangedInputs(string scenario)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var sources = new MetadataSources();
        var use = new MetadataUse();
        using var catalogue = sources.Catalogue(use);
        var admitted = true;
        using var snapshot = await new CustomizationDiscoveryService(sources, catalogue, use, f, f.Time)
            .Review([sources.Values[1].Source], new(f.Session, null), "Original request", RequestOrigin.LocalUi,
                () => admitted, Token, proposeAgent: true);
        snapshot.Consume();
        await snapshot.Invoke(CustomizationDiscoveryTools.List, "{}", Token);
        snapshot.Resolve(new(ModelTurnOutcome.Succeeded, ModelTurnReason.None,
            new(new(Guid.NewGuid()), f.Request, ModelProviderSelection.OllamaCandidate),
            new(JsonSerializer.Serialize(new { agentRef = snapshot.Records[0].Ref }), null)));
        var proposal = snapshot.AgentProposal!;
        proposal.IsCurrent().Should().BeTrue();
        if (scenario is "caller") { admitted = false; }
        if (scenario is "clock") { f.Time.Now = f.Time.Now.AddSeconds(-1); }
        if (scenario is "expiry") { f.Time.Now = f.Time.Now.AddMinutes(2); }
        if (scenario is "use") { use.Revoke(); }
        if (scenario is "reload") { catalogue.ReportReloadFailure(new IOException("changed")); }
        if (scenario is "catalogue") { catalogue.Update(items => items.Select(item => item with { IsUseEnabled = false }).ToArray()); }
        proposal.IsCurrent().Should().BeFalse();
        await proposal.Invoking(item => item.ConsumeForReviewAsync(Token)).Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("kind")]
    [InlineData("disabled")]
    [InlineData("use-disabled")]
    [InlineData("digest")]
    [InlineData("id")]
    public async Task ForgedMetadataProjectionCannotTurnAnUnboundOrIneligibleCandidateIntoAnAgent(string scenario)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var sources = new MetadataSources();
        var observed = sources.Values[1];
        var use = new MetadataUse();
        var definition = new ArtifactDefinition(scenario is "id" ? "other.agent" : observed.ArtifactId,
            scenario is "kind" ? ArtifactKind.Skill : ArtifactKind.Agent, observed.Name, observed.Description,
            "candidate", ["candidate"], "kora-profile", "1",
            scenario is "digest" ? new string('b', 64) : observed.Digest, "Inert role")
        {
            UseControlId = scenario is "missing" ? "foreign.source" : observed.Source.Id,
            IsUseEnabled = scenario is not "disabled",
            IsUseDisabled = scenario is "use-disabled",
        };
        using var catalogue = new ArtifactCatalogue([definition]);
        var reference = Guid.NewGuid().ToString("N");
        using var snapshot = new CustomizationDiscoverySnapshot(sources, catalogue, use, [observed],
            [new(reference, CustomizationKind.Agent, observed.Name, observed.Description, CustomizationScope.User,
                true, true, CapabilityAvailability.NotObserved)],
            new(f.Session, null), "Original input", RequestOrigin.LocalUi, static () => true, f.Time, proposeAgent: true);
        snapshot.Consume();
        await snapshot.Invoke(CustomizationDiscoveryTools.List, "{}", Token);
        var result = snapshot.Resolve(new(ModelTurnOutcome.Succeeded, ModelTurnReason.None,
            new(new(Guid.NewGuid()), f.Request, ModelProviderSelection.OllamaCandidate),
            new(JsonSerializer.Serialize(new { agentRef = reference }), null)));
        result.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        result.Response.Should().BeNull();
        snapshot.AgentProposal.Should().BeNull();
        f.ExecutionStarts.Should().Be(0);
    }
}
