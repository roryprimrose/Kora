using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Fact]
    public async Task RetainedOriginalExecutesTheBoundedLoopAndRetainsOnlyTheGenuineCorrelatedToolAnswer()
    {
        using var fixture = new Fixture();
        fixture.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        fixture.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var transport = new ToolTransport(VersionCall, FinalAnswer);
        transport.ChangeReply = (request, reply) =>
        {
            if (request.Round == 1) { return reply; }
            var result = request.Messages.OfType<ModelToolResultMessage>().Single();
            using var document = JsonDocument.Parse(result.Result.ToArray());
            var version = document.RootElement.GetProperty("version").GetProperty("version").GetString();
            return ToolTransport.Reply(request, JsonSerializer.Serialize(new { kind = "answer", answer = "Observed " + version }));
        };
        await using var loop = CreateLoop(fixture, transport);
        var host = fixture.ConfiguredProfiles(adapter: loop);
        var receipt = await fixture.ConversationService(host, execution: host).SubmitAsync(new(fixture.Session, null),
            "What version is this application?", static () => true, Token);
        transport.Requests.Should().HaveCount(2);
        transport.HostRequests.Should().OnlyContain(request => ReferenceEquals(request, receipt.Message.Request));
        transport.Requests[0].Context.UserRequest.Should().Be(receipt.Message.Body);
        transport.Requests[1].Messages.OfType<ModelToolResultMessage>().Single().CapabilityId
            .Should().Be(ReadOnlyCapabilityCatalog.Version);
        receipt.Message.AssistantResponse!.Body.Should().Be("Observed 1.0");
        receipt.Message.AssistantResponse.Provenance.Should().BeSameAs(transport.Requests[0].Provenance);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Task.State.Should().Be(HostTaskState.Succeeded);
        fixture.Retained.Should().Be(receipt.Message);
        fixture.ExecutionStarts.Should().Be(1);
        fixture.ExecutionCompletions.Should().Be(1);
        fixture.Adapter.Calls.Should().Be(0);
        host.IsQuiescent.Should().BeTrue();
        fixture.Messages.Should().NotContain(message => message.Contains(receipt.Message.Body, StringComparison.Ordinal)
            || message.Contains("Observed 1.0", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("held", false)]
    [InlineData("held", true)]
    [InlineData("catalogue", false)]
    [InlineData("catalogue", true)]
    [InlineData("installation", false)]
    [InlineData("installation", true)]
    public async Task ProfileAuthorityLossDuringTheLoopCannotDispatchAnotherToolOrRetainAnAnswer(
        string fault, bool afterTool)
    {
        using var fixture = new Fixture();
        fixture.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        fixture.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var versions = 0;
        void Withdraw()
        {
            if (fault is "held") { fixture.SessionProfileHeld = true; }
            if (fault is "catalogue") { fixture.ProfileCatalogue.Status = ProviderCatalogueStatus.Unavailable; }
            if (fault is "installation") { fixture.ProfileCatalogue.Installation = LocalProviderInstallation.Unknown; }
        }
        fixture.OnVersion = () => { versions++; if (afterTool) { Withdraw(); } };
        var transport = new ToolTransport(VersionCall, FinalAnswer);
        if (!afterTool) { transport.AfterRequest = _ => Withdraw(); }
        await using var loop = CreateLoop(fixture, transport);
        var host = fixture.ConfiguredProfiles(adapter: loop);
        var receipt = await fixture.ConversationService(host, execution: host).SubmitAsync(new(fixture.Session, null),
            "original request", static () => true, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Denied);
        receipt.Message.Reason.Should().Be(fault is "held" ? ModelTurnReason.SessionNotAdmitted : ModelTurnReason.UnregisteredModel);
        receipt.Message.AssistantResponse.Should().BeNull();
        receipt.Task.State.Should().Be(HostTaskState.Denied);
        versions.Should().Be(afterTool ? 1 : 0);
        transport.Requests.Should().ContainSingle();
        fixture.ExecutionCompletions.Should().Be(1);
    }
}
