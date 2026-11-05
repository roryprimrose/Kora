using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Mcp;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Mcp;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("configuration")]
    [InlineData("broker")]
    [InlineData("effects")]
    [InlineData("interactions")]
    [InlineData("host")]
    [InlineData("execution")]
    [InlineData("audit")]
    public async Task McpContentModelUseRequiresEveryHostContractBeforeRetainingInput(string missing)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var broker = new CapabilityBroker(source.Account);
        var approvals = new McpApprovals(f);
        var workspace = new SessionWorkspaceService(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance);
        var service = new SessionConversationService(f, f, workspace, f,
            string.Equals(missing, "host", StringComparison.Ordinal) ? new TestAdmission(ModelTurnOutcome.Unavailable) : f.Host,
            f.Time, NullLogger<SessionConversationService>.Instance,
            execution: string.Equals(missing, "execution", StringComparison.Ordinal) ? null : f.Host,
            cancellationAudit: string.Equals(missing, "audit", StringComparison.Ordinal) ? null : f.CancelAudit,
            interactions: string.Equals(missing, "interactions", StringComparison.Ordinal) ? null : approvals,
            mcpConfiguration: string.Equals(missing, "configuration", StringComparison.Ordinal) ? null : source.Configuration,
            mcpBroker: string.Equals(missing, "broker", StringComparison.Ordinal) ? null : broker,
            mcpEffects: string.Equals(missing, "effects", StringComparison.Ordinal) ? null : new([]));
        var original = f.Task;
        await FluentActions.Awaiting(() => service.SubmitMcpContentAsync(new(f.Session, null), "Explain the reference.",
            source.ServerId, source.Account, McpCapabilityKind.Resource, approvals.Present, SelectCapability,
            ContentArguments, static () => true, Token)).Should().ThrowAsync<McpUnavailableException>();
        f.Task.Should().BeSameAs(original);
        f.Retained.Should().BeNull();
        broker.Methods.Should().BeEmpty();
        approvals.Stages.Should().BeEmpty();
        f.CancelAudit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("no-callback")]
    [InlineData("cancelled")]
    [InlineData("admission")]
    [InlineData("metadata")]
    [InlineData("after-metadata")]
    [InlineData("source")]
    [InlineData("during-source-validation")]
    public async Task McpContentPublicationRevalidatesAfterTheGenuineAnswerWasCommitted(string fault)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var transport = new ContentModelTransport();
        await using var loop = new BoundedModelTurnAdapter(transport, f.Time, f);
        var host = f.ConfiguredProfiles(adapter: loop);
        var broker = new CapabilityBroker(source.Account);
        var approvals = new McpApprovals(f);
        var service = CapabilityService(f, host, source, broker, approvals);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var live = true;
        var terminal = false;
        service.RunningConversationChanged += status =>
        {
            if (status.Phase != ConversationExecutionPhase.Terminal) { return; }
            terminal = true;
            switch (fault)
            {
                case "cancelled": cancellation.Cancel(); break;
                case "admission": live = false; break;
                case "metadata": f.Session = f.Session with { Generation = new(2) }; break;
                case "after-metadata": f.OnRead = () => live = false; break;
                case "source": File.AppendAllText(source.File, " "); break;
                case "during-source-validation": source.BeforeLoad = () => live = false; break;
            }
        };
        var publications = 0;
        var submission = () => service.SubmitMcpContentAsync(new(f.Session, null), "Explain the reviewed reference.",
            source.ServerId, source.Account, McpCapabilityKind.Resource, approvals.Present, SelectCapability,
            ContentArguments, () => live, cancellation.Token,
            onResult: string.Equals(fault, "no-callback", StringComparison.Ordinal) ? null : (_, _) => publications++);
        if (string.Equals(fault, "source", StringComparison.Ordinal)) { await submission.Should().ThrowAsync<InvalidOperationException>(); }
        else if (string.Equals(fault, "during-source-validation", StringComparison.Ordinal)) { await submission.Should().ThrowAsync<OperationCanceledException>(); }
        else { (await submission()).Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded); }
        terminal.Should().BeTrue();
        publications.Should().Be(0);
        f.Retained!.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        f.Retained.AssistantResponse!.Body.Should().Be("The observed reference is available.");
        transport.Requests.Should().ContainSingle();
        broker.Disposed.Should().BeTrue();
        broker.Methods.Should().NotContain("tools/call");
    }

    [Theory]
    [InlineData("selection", "revoked")]
    [InlineData("selection", "source")]
    [InlineData("input", "revoked")]
    [InlineData("input", "control")]
    [InlineData("read", "revoked")]
    [InlineData("read", "cancelled")]
    [InlineData("join", "revoked")]
    [InlineData("join", "cancelled")]
    [InlineData("join", "metadata")]
    public async Task McpTextReadsDoNotPublishContentWhenAuthorityChangesAcrossAwaitBoundaries(string phase, string fault)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var approvals = new McpApprovals(f);
        void Retire()
        {
            switch (fault)
            {
                case "revoked": approvals.Revoke(); break;
                case "source": File.AppendAllText(source.File, " "); break;
                case "control": f.ControlRevision++; break;
                case "cancelled": cancellation.Cancel(); break;
                case "metadata": f.Session = f.Session with { Generation = new(2) }; break;
            }
        }
        var broker = new CapabilityBroker(source.Account)
        {
            AfterExchange = method => { if (string.Equals(phase, "read", StringComparison.Ordinal) && string.Equals(method, "prompts/get", StringComparison.Ordinal)) { Retire(); } },
            AfterDispose = () => { if (string.Equals(phase, "join", StringComparison.Ordinal)) { Retire(); } },
        };
        Task<McpCapability?> Select(HostRequest request, HostRevision generation, DateTimeOffset expires,
            IReadOnlyList<McpCapability> items, Func<bool> admitted, CancellationToken token)
        {
            if (string.Equals(phase, "selection", StringComparison.Ordinal)) { Retire(); }
            return SelectCapability(request, generation, expires, items, admitted, token);
        }
        Task<string?> Input(HostRequest request, HostRevision generation, DateTimeOffset expires,
            McpCapability capability, Func<bool> admitted, CancellationToken token)
        {
            if (string.Equals(phase, "input", StringComparison.Ordinal)) { Retire(); }
            return ContentArguments(request, generation, expires, capability, admitted, token);
        }
        var result = await CapabilityService(f, f.Host, source, broker, approvals).ReadMcpContentAsync(
            new(f.Session, null), source.ServerId, source.Account, McpCapabilityKind.Prompt, approvals.Present,
            Select, Input, static () => true, cancellation.Token);
        result.Outcome.Should().Be(string.Equals(fault, "cancelled", StringComparison.Ordinal) ? McpExplicitOutcome.Cancelled : McpExplicitOutcome.Denied);
        result.DurableState.Should().Be(HostTaskState.Unknown);
        result.Content.Should().BeNull();
        result.Result.Should().BeNull();
        broker.Disposed.Should().BeTrue();
        broker.Methods.Should().NotContain("tools/call");
        if (phase is "selection" or "input") { broker.Methods.Should().NotContain("prompts/get"); }
        f.Retained.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(McpCapabilityKind.Resource, """{"contents":[{"uri":"data://item","text":"safe first block"},{"uri":"data://item","blob":"AA==","mimeType":"image/png"}]}""")]
    [InlineData(McpCapabilityKind.Prompt, """{"messages":[{"role":"user","content":{"type":"text","text":"safe first block"}},{"role":"assistant","content":{"type":"audio","data":"AA==","mimeType":"audio/wav"}}]}""")]
    [InlineData(McpCapabilityKind.Prompt, """{"messages":[{"role":"user","content":{"type":"text","text":"safe first block"}},{"role":"assistant","content":{"type":"image","data":"AA==","mimeType":"image/png"}}]}""")]
    public async Task McpMixedUnsupportedRepliesRejectEveryBlockWithoutDurableOrModelDisclosure(McpCapabilityKind kind, string reply)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var broker = new CapabilityBroker(source.Account)
        {
            ReplyResult = (method, _) => method is "resources/read" or "prompts/get" ? reply : null,
        };
        var approvals = new McpApprovals(f);
        var result = await CapabilityService(f, f.Host, source, broker, approvals).ReadMcpContentAsync(
            new(f.Session, null), source.ServerId, source.Account, kind, approvals.Present,
            SelectCapability, ContentArguments, static () => true, Token);
        result.Outcome.Should().Be(McpExplicitOutcome.Denied);
        result.DurableState.Should().Be(HostTaskState.Unknown);
        result.Content.Should().BeNull();
        result.Result.Should().BeNull();
        broker.Disposed.Should().BeTrue();
        f.Retained.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
        f.Messages.Should().NotContain(message => message.Contains("safe first block", StringComparison.Ordinal));
    }
}