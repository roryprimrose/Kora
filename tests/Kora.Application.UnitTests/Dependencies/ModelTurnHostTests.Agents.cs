using System.Text;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Agents;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Tools;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    private const string AgentProfile = """
        ---
        name: selected-agent
        description: User selected role
        kora-agent-format: 1
        runtime: local
        input: text
        output: text
        references: none
        tools: application.get_version
        maximum-calls: 1
        maximum-rounds: 2
        maximum-input-bytes: 32768
        maximum-output-bytes: 4096
        deadline-ms: 15000
        ---
        Use only admitted tools to explain the original task.
        """;

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task ReviewedAgentExecutesCanonicalToolLoopAndRetainsExactOriginalTerminalAnswer(RequestOrigin origin)
    {
        using var f = new Fixture(origin);
        var transport = new ToolTransport(VersionCall, FinalAnswer);
        await using var loop = CreateLoop(f, transport);
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles(adapter: loop);
        var service = new SessionConversationService(f, f,
            new SessionWorkspaceService(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance), f, host, f.Time,
            NullLogger<SessionConversationService>.Instance, host);
        var reader = new AgentReader();
        await using var workflow = new BoundedAgentWorkflow(reader, f, f, service, host, f, f.Time);
        using var root = f.Root();
        using var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null), "original task",
            origin, () => true, Token);
        transport.Requests.Should().BeEmpty();
        f.RetainCalls.Should().Be(0);
        var receipt = await workflow.InvokeAsync(review, Token);
        receipt.Message.Body.Should().Be("original task");
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse!.Body.Should().Be("The observed version is 1.0.");
        receipt.Message.Request.Origin.Should().Be(origin);
        transport.Requests.Should().HaveCount(2);
        transport.Requests.Should().OnlyContain(item => item.Context.Tools.Count == 1
            && item.Context.Tools[0].Id == ReadOnlyCapabilityCatalog.Version
            && item.Context.Constraints!.MaximumCalls == 1);
        reader.Released.Should().BeTrue();
        host.IsQuiescent.Should().BeTrue();
        await workflow.Invoking(value => value.InvokeAsync(review, Token)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("source")]
    [InlineData("privacy")]
    [InlineData("revision")]
    [InlineData("expired")]
    public async Task AgentReviewInvalidationPreventsRetentionAndTools(string fault)
    {
        using var f = new Fixture();
        var transport = new ToolTransport(VersionCall, FinalAnswer);
        await using var loop = CreateLoop(f, transport);
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles(adapter: loop);
        var service = new SessionConversationService(f, f,
            new SessionWorkspaceService(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance), f, host, f.Time,
            NullLogger<SessionConversationService>.Instance, host);
        var reader = new AgentReader();
        await using var workflow = new BoundedAgentWorkflow(reader, f, f, service, host, f, f.Time);
        using var root = f.Root();
        using var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null), "original task",
            RequestOrigin.LocalUi, () => true, Token);
        if (fault is "revoked") { review.Dispose(); }
        if (fault is "source") { reader.Text += "\nChanged"; }
        if (fault is "privacy") { f.CanControl = false; }
        if (fault is "revision") { f.ControlRevision++; }
        if (fault is "expired") { f.Time.Now = review.ExpiresAt; }
        await workflow.Invoking(value => value.InvokeAsync(review, Token)).Should().ThrowAsync<Exception>();
        f.RetainCalls.Should().Be(0);
        transport.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{"kind":"toolCalls","calls":[{"callId":"one","capabilityId":"application.get_version","input":{}},{"callId":"two","capabilityId":"runtime.list","input":{}}]}""", ModelTurnReason.ToolUseNotPermitted)]
    [InlineData("""{"kind":"toolCalls","calls":[{"callId":"one","capabilityId":"application.get_version","input":{}},{"callId":"two","capabilityId":"application.get_version","input":{}}]}""", ModelTurnReason.ToolLoopBudgetExceeded)]
    public async Task AgentRejectsCompleteEscalatingBatchBeforeAllowedPrefix(string message, ModelTurnReason reason)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var observed = 0;
        f.OnVersion = () => observed++;
        var transport = new ToolTransport(message);
        await using var loop = CreateLoop(f, transport);
        var host = f.LoopHost(loop);
        var context = AgentContext(f);
        var turn = (await host.AdmitAsync(ModelProviderSelection.OllamaCandidate, context, Token)).Turn!;
        var result = await host.RunAsync(turn, Token);
        result.Reason.Should().Be(reason);
        observed.Should().Be(0);
        result.Response.Should().BeNull();
    }

    private static ModelContextEnvelope AgentContext(Fixture f) => new(f.Request, "Host agent policy", "original",
        [], f.Time.GetUtcNow(), f.Time.GetUtcNow().AddSeconds(15), ModelToolMode.ReadOnly,
        new(1, [ReadOnlyCapabilityCatalog.Version], 1, 2, 32768, 4096, 15000));

    [Theory]
    [InlineData("inactive")]
    [InlineData("foreign-session")]
    [InlineData("unsupported-profile")]
    [InlineData("cancelled")]
    [InlineData("nonoriginal")]
    [InlineData("admission")]
    public async Task AgentReviewFailureCannotRetainOrExecute(string fault)
    {
        using var f = new Fixture();
        var host = QualifiedHistoryHost(f);
        var reader = new AgentReader();
        await using var workflow = new BoundedAgentWorkflow(reader, f, f, f.ConversationService(host, execution: host), host, f, f.Time);
        using var root = f.Root();
        var target = new SessionWorkspaceEntry(f.Session, null);
        if (fault is "inactive") { target = new(f.Session with { IsActive = false }, null); }
        if (fault is "foreign-session") { target = new(f.Session with { SessionId = new(Guid.NewGuid()) }, null); }
        if (fault is "unsupported-profile") { reader.Text = "unsupported"; }
        using var cancelled = new CancellationTokenSource();
        if (fault is "cancelled") { cancelled.Cancel(); }
        await workflow.Invoking(value => value.ReviewAsync("selected.agent.md", target, "task",
            fault is "nonoriginal" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi, () => fault is not "admission", cancelled.Token))
            .Should().ThrowAsync<Exception>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task AgentRevocationAndRepeatedDisposalReleasePinnedSourceAndNeverReviveReview()
    {
        using var f = new Fixture();
        var host = QualifiedHistoryHost(f);
        var reader = new AgentReader();
        var workflow = new BoundedAgentWorkflow(reader, f, f, f.ConversationService(host, execution: host), host, f, f.Time);
        using var root = f.Root();
        var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null), "task", RequestOrigin.LocalUi, () => true, Token);
        await workflow.RevokeAsync(review);
        reader.Released.Should().BeTrue();
        await workflow.RevokeAsync(review);
        await workflow.DisposeAsync();
        await workflow.DisposeAsync();
        await workflow.Invoking(value => value.ReviewAsync("selected.agent.md", new(f.Session, null), "task",
            RequestOrigin.LocalUi, () => true, Token)).Should().ThrowAsync<ObjectDisposedException>();
        await workflow.Invoking(value => value.InvokeAsync(review, Token)).Should().ThrowAsync<ObjectDisposedException>();
        f.RetainCalls.Should().Be(0);
    }

    [Fact]
    public async Task UnavailableAgentRuntimeRetainsTruthfulTerminalWithoutSubmittingProfileAsPromptSuccess()
    {
        using var f = new Fixture();
        var host = f.ConfiguredProfiles(production: true);
        var reader = new AgentReader { Text = AgentProfile.Replace("tools: application.get_version", "tools: none", StringComparison.Ordinal)
            .Replace("maximum-calls: 1", "maximum-calls: 0", StringComparison.Ordinal) };
        await using var workflow = new BoundedAgentWorkflow(reader, f, f, f.ConversationService(host, execution: host), host, f, f.Time);
        using var root = f.Root();
        using var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null), "task", RequestOrigin.LocalUi, () => true, Token);
        var result = await workflow.InvokeAsync(review, Token);
        result.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        result.Message.AssistantResponse.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
        f.Audits.Where(item => string.Equals(item.ActionId, "agents.revision.invoke", StringComparison.Ordinal))
            .Select(item => item.Outcome).Should().Equal(Kora.Core.Auditing.SecurityAuditOutcome.Requested, Kora.Core.Auditing.SecurityAuditOutcome.Failed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgentCancellationOrRevocationOwnsLateTransportUntilQuiescentAndSuppressesLateAnswer(bool revoke)
    {
        using var f = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<ModelToolTransportReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new ToolTransport { Pending = pending.Task, AfterRequest = _ => entered.SetResult() };
        await using var loop = CreateLoop(f, transport);
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles(adapter: loop);
        var reader = new AgentReader();
        await using var workflow = new BoundedAgentWorkflow(reader, f, f, f.ConversationService(host, execution: host), host, f, f.Time);
        using var root = f.Root();
        using var cancellation = new CancellationTokenSource();
        using var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null), "task", RequestOrigin.LocalUi, () => true, Token);
        var running = workflow.InvokeAsync(review, cancellation.Token);
        await entered.Task.WaitAsync(Token);
        if (revoke) { review.Dispose(); }
        else { await cancellation.CancelAsync(); }
        host.IsQuiescent.Should().BeFalse();
        running.IsCompleted.Should().BeFalse();
        reader.Released.Should().BeFalse();
        pending.SetResult(ToolTransport.Reply(transport.Requests[0], FinalAnswer));
        Func<Task> join = async () => await running;
        await join.Should().ThrowAsync<Exception>();
        reader.Released.Should().BeTrue();
        host.IsQuiescent.Should().BeTrue();
        f.Retained!.AssistantResponse.Should().BeNull();
        f.Retained.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
    }

    [Fact]
    public async Task AgentCallBudgetIsIndependentlyEnforcedAtHostDispatchAgainstDirectAdapterEscalation()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var versions = 0;
        f.OnVersion = () => versions++;
        var turn = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, AgentContext(f), Token)).Turn!;
        turn.IsRunning = true;
        (await f.Host.InvokeToolAsync(turn, ReadOnlyCapabilityCatalog.Version, "{}", Token, Token)).Outcome.Should().Be(CapabilityOutcome.Succeeded);
        (await f.Host.InvokeToolAsync(turn, ReadOnlyCapabilityCatalog.Version, "{}", Token, Token)).Outcome.Should().Be(CapabilityOutcome.Denied);
        turn.IsRunning = false;
        versions.Should().Be(1);
    }

    [Fact]
    public async Task AgentReviewCannotBeConsumedByAnotherWorkflowOrReplayedBeforeDispatch()
    {
        using var f = new Fixture();
        var host = QualifiedHistoryHost(f);
        var reader = new AgentReader();
        var service = f.ConversationService(host, execution: host);
        await using var workflow = new BoundedAgentWorkflow(reader, f, f, service, host, f, f.Time);
        await using var other = new BoundedAgentWorkflow(new AgentReader(), f, f, service, host, f, f.Time);
        using var root = f.Root();
        using var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null), "task", RequestOrigin.LocalUi, () => true, Token);
        Action foreign = () => review.Consume(other);
        foreign.Should().Throw<InvalidOperationException>();
        review.Consume(workflow);
        await workflow.Invoking(value => value.InvokeAsync(review, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    private sealed class AgentReader : IAgentProfileReader, IAgentProfileSelection
    {
        public string Text { get; set; } = AgentProfile;
        public bool Released { get; private set; }
        public LocalFileMetadata Metadata { get; } = new(Path.GetFullPath("selected.agent.md"), "native-identity", 512, DateTimeOffset.UnixEpoch);
        public Task<IAgentProfileSelection> OpenAsync(string path, CancellationToken token) => Task.FromResult<IAgentProfileSelection>(this);
        public Task<byte[]> ReadAsync(CancellationToken token) => Task.FromResult(Encoding.UTF8.GetBytes(Text));
        public void Dispose() => Released = true;
    }
}
