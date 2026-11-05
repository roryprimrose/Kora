using System.Text;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task AgentAnswerRetainsExactHostObservationWithoutPromotingModelTextToEvidence(RequestOrigin origin)
    {
        using var f = new Fixture(origin);
        var transport = new ToolTransport(VersionCall, FinalAnswer);
        await using var loop = CreateLoop(f, transport);
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles(adapter: loop);
        var reader = new AgentReader();
        await using var workflow = new BoundedAgentWorkflow(reader, f, f,
            f.ConversationService(host, execution: host), host, f, f.Time);
        using var root = f.Root();
        using var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null),
            "original agent task", origin, () => true, Token);
        var receipt = await workflow.InvokeAsync(review, origin, Token);
        receipt.Message.Observations.Should().NotBeNull("a genuine agent answer must retain its exact host observation");
        var observation = receipt.Message.Observations!.Items.Should().ContainSingle().Subject;
        observation.CapabilityId.Should().Be(ReadOnlyCapabilityCatalog.Version);
        observation.InputJson.Should().Be("{}");
        observation.ObservedAt.Should().Be(f.Time.Now);
        Encoding.UTF8.GetBytes(observation.ResultJson).Should().Equal(
            transport.Requests[1].Messages.OfType<ModelToolResultMessage>().Single().Result);
        observation.ResultJson.Should().NotContain("The observed version is 1.0.");
        receipt.Message.Body.Should().Be("original agent task");
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.AssistantResponse!.Body.Should().Be("The observed version is 1.0.");
        receipt.Message.Validate();
        reader.Released.Should().BeTrue();
        host.IsQuiescent.Should().BeTrue();
        transport.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Denied)]
    [InlineData(ModelTurnOutcome.Unavailable)]
    [InlineData(ModelTurnOutcome.Unknown)]
    public async Task AgentTerminalWithoutFinalAnswerCannotRetainEarlierObservationAsSuccessfulEvidence(ModelTurnOutcome outcome)
    {
        using var f = new Fixture();
        var transport = new ToolTransport(VersionCall, FinalAnswer);
        transport.ChangeReply = (request, reply) => transport.Requests.Count == 2
            ? new(outcome, request.Provenance) : reply;
        await using var loop = CreateLoop(f, transport);
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles(adapter: loop);
        var reader = new AgentReader();
        await using var workflow = new BoundedAgentWorkflow(reader, f, f,
            f.ConversationService(host, execution: host), host, f, f.Time);
        using var root = f.Root();
        using var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null),
            "original failed agent task", RequestOrigin.LocalUi, () => true, Token);
        var receipt = await workflow.InvokeAsync(review, RequestOrigin.LocalUi, Token);
        receipt.Message.Outcome.Should().Be(outcome);
        receipt.Message.Body.Should().Be("original failed agent task");
        receipt.Message.AssistantResponse.Should().BeNull();
        receipt.Message.Observations.Should().BeNull();
        transport.Requests.Should().HaveCount(2);
        transport.Requests[1].Messages.OfType<ModelToolResultMessage>().Should().ContainSingle();
        reader.Released.Should().BeTrue();
        host.IsQuiescent.Should().BeTrue();
        f.Audits.Should().NotContain(item => string.Equals(item.ActionId, "agents.revision.invoke", StringComparison.Ordinal)
            && item.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
    }
}
