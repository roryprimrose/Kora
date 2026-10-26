using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Application.Dependencies;
using Kora.Application.UnitTests.Agents;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    private static AgentReader InstructionToolAgent() => new()
    {
        Text = AgentProfile.Replace("kora-agent-format: 1", "kora-agent-format: 4", StringComparison.Ordinal)
            .Replace("references: none", "references: disk.kora-profile.instruction.example", StringComparison.Ordinal),
    };

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task ReviewedInstructionToolAgentUsesRealCanonicalCallbackAndRetainsOriginalAnswer(RequestOrigin origin)
    {
        using var f = new Fixture(origin);
        var sources = new AgentInstructionReferenceResolverTests.Fixture();
        var reader = InstructionToolAgent();
        var observed = 0;
        f.OnVersion = () => observed++;
        var transport = new ToolTransport(VersionCall, FinalAnswer);
        await using var loop = CreateLoop(f, transport);
        var host = ConfigureAgentLoop(f, loop);
        await using var workflow = new BoundedAgentWorkflow(reader, f, f, f.ConversationService(host, execution: host),
            host, f, f.Time, sources.Resolver);
        using var root = f.Root();
        using var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null), "original task",
            origin, () => true, Token);
        review.IsInvocationReady.Should().BeFalse();
        sources.Store.Reads.Should().Be(0);
        transport.Requests.Should().BeEmpty();
        await workflow.Invoking(value => value.InvokeAsync(review, origin, Token)).Should().ThrowAsync<InvalidOperationException>();
        await workflow.ResolveAsync(review, origin, Token);
        review.IsInvocationReady.Should().BeTrue();
        review.References!.Bundle!.Instructions.Single().Should().BeSameAs(sources.Store.Value);
        await workflow.Invoking(value => value.InvokeAsync(review, Token)).Should().ThrowAsync<InvalidOperationException>();
        var receipt = await workflow.InvokeAsync(review, origin, Token);
        receipt.Message.Body.Should().Be("original task");
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.Request.SessionId.Should().Be(f.Session.SessionId);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse!.Body.Should().Be("The observed version is 1.0.");
        observed.Should().Be(1);
        transport.Requests.Should().HaveCount(2);
        var first = transport.Requests[0];
        using var wire = JsonDocument.Parse(first.Context.Wire.ToArray());
        wire.RootElement.GetProperty("UserRequest").GetString().Should().Be("original task");
        var evidence = wire.RootElement.GetProperty("Evidence").EnumerateArray().ToArray();
        evidence.Should().HaveCount(2);
        evidence[0].GetProperty("Content").GetString().Should().Be(reader.Text);
        evidence[1].GetProperty("Content").GetString().Should().Be(sources.Store.Value!.Document.Text);
        wire.RootElement.GetProperty("SystemPolicy").GetString().Should().NotContain("Exact instruction.");
        transport.Requests.Should().OnlyContain(item => ReferenceEquals(item.Context, first.Context)
            && item.Context.Tools.Count == 1 && string.Equals(item.Context.Tools[0].Id, ReadOnlyCapabilityCatalog.Version, StringComparison.Ordinal)
            && item.Context.Tools[0] == ReadOnlyCapabilityCatalog.Descriptors.Single(tool => string.Equals(tool.Id, ReadOnlyCapabilityCatalog.Version, StringComparison.Ordinal))
            && item.Context.Constraints!.MaximumCalls == 1 && item.Context.Constraints.MaximumRounds == 2);
        var actual = (ModelToolResultMessage)transport.Requests[1].Messages[1];
        actual.Result.Should().Equal(CapabilityReplyWire.Serialize(new(CapabilityOutcome.Succeeded, "observed",
            Version: new("1.0", "Not observed by the current version provider."))));
        transport.Requests[1].Budget.InputUtf8Bytes.Should().BeLessThan(first.Budget.InputUtf8Bytes);
        transport.Requests[1].Budget.OutputUtf8Bytes.Should().BeLessThan(first.Budget.OutputUtf8Bytes);
        sources.Store.Reads.Should().BeGreaterThan(4);
        f.Audits.Where(item => string.Equals(item.ActionId, "agents.revision.invoke", StringComparison.Ordinal)).Select(item => item.Outcome)
            .Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Messages.Should().NotContain(item => item.Contains("Exact instruction.", StringComparison.Ordinal));
        reader.Released.Should().BeTrue();
        host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("source", false)]
    [InlineData("source", true)]
    [InlineData("profile", false)]
    [InlineData("profile", true)]
    [InlineData("disable", false)]
    [InlineData("disable", true)]
    [InlineData("control", false)]
    [InlineData("control", true)]
    [InlineData("session", false)]
    [InlineData("session", true)]
    [InlineData("generation", false)]
    [InlineData("generation", true)]
    [InlineData("permission", false)]
    [InlineData("permission", true)]
    [InlineData("late-currentness", false)]
    [InlineData("late-currentness", true)]
    public async Task InstructionToolAgentRevalidatesSourceAuthorityBeforeCallbackAndLateAnswer(string fault, bool afterTool)
    {
        using var f = new Fixture();
        var sources = new AgentInstructionReferenceResolverTests.Fixture();
        var reader = InstructionToolAgent();
        var observed = 0;
        f.OnVersion = () => observed++;
        var transport = new ToolTransport(VersionCall, FinalAnswer)
        {
            AfterRequest = request =>
            {
                if (request.Round != (afterTool ? 2 : 1)) { return; }
                if (fault is "source") { sources.Store.Value = sources.Store.Value! with { FileIdentity = new string('c', 48) }; }
                if (fault is "profile") { reader.Text += "\nChanged role"; }
                if (fault is "disable") { sources.Revisions.DisableExact(sources.Store.Value!, sources.Catalogue); }
                if (fault is "control") { f.ControlRevision++; }
                if (fault is "session") { f.Session = f.Session with { IsActive = false }; }
                if (fault is "generation") { f.Session = f.Session with { Generation = new(2) }; }
                if (fault is "permission") { f.Inspect = false; }
                if (fault is "late-currentness") { sources.Store.BeforeRead = () => f.ControlRevision++; }
            },
        };
        await using var loop = CreateLoop(f, transport);
        var host = ConfigureAgentLoop(f, loop);
        await using var workflow = new BoundedAgentWorkflow(reader, f, f, f.ConversationService(host, execution: host),
            host, f, f.Time, sources.Resolver);
        using var root = f.Root();
        using var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null), "original task",
            RequestOrigin.LocalUi, () => true, Token);
        await workflow.ResolveAsync(review, RequestOrigin.LocalUi, Token);
        await workflow.Invoking(value => value.InvokeAsync(review, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        observed.Should().Be(afterTool ? 1 : 0);
        f.Retained!.AssistantResponse.Should().BeNull();
        f.Retained.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        f.Audits.Should().NotContain(item => string.Equals(item.ActionId, "agents.revision.invoke", StringComparison.Ordinal) && item.Outcome == SecurityAuditOutcome.Succeeded);
        reader.Released.Should().BeTrue();
        host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("input")]
    [InlineData("output")]
    [InlineData("rounds")]
    public async Task InstructionToolAgentCumulativeBudgetsStopBeforeAnotherProviderAttempt(string budget)
    {
        using var f = new Fixture();
        var sources = new AgentInstructionReferenceResolverTests.Fixture();
        var reader = InstructionToolAgent();
        if (budget is "rounds")
        { reader.Text = reader.Text.Replace("maximum-rounds: 2", "maximum-rounds: 1", StringComparison.Ordinal); }
        var observed = 0;
        f.OnVersion = () => observed++;
        var transport = new ToolTransport(VersionCall);
        if (budget is not "rounds")
        {
            transport.ChangeReply = (request, reply) => reply with
            {
                Usage = budget is "input"
                    ? reply.Usage! with { InputUtf8Bytes = request.Budget.InputUtf8Bytes }
                    : reply.Usage! with { OutputUtf8Bytes = request.Budget.OutputUtf8Bytes },
            };
        }
        await using var loop = CreateLoop(f, transport);
        var host = ConfigureAgentLoop(f, loop);
        await using var workflow = new BoundedAgentWorkflow(reader, f, f, f.ConversationService(host, execution: host),
            host, f, f.Time, sources.Resolver);
        using var root = f.Root();
        using var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null), "original task",
            RequestOrigin.LocalUi, () => true, Token);
        await workflow.ResolveAsync(review, RequestOrigin.LocalUi, Token);
        var receipt = await workflow.InvokeAsync(review, RequestOrigin.LocalUi, Token);
        receipt.Message.Reason.Should().Be(ModelTurnReason.ToolLoopBudgetExceeded);
        receipt.Message.AssistantResponse.Should().BeNull();
        observed.Should().Be(1);
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task InstructionToolAgentUnqualifiedModelRemainsUnavailableWithoutAnyCallback()
    {
        using var f = new Fixture();
        var sources = new AgentInstructionReferenceResolverTests.Fixture();
        var host = f.ConfiguredProfiles(production: true);
        await using var workflow = new BoundedAgentWorkflow(InstructionToolAgent(), f, f, f.ConversationService(host, execution: host),
            host, f, f.Time, sources.Resolver);
        using var root = f.Root();
        using var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null), "original task",
            RequestOrigin.LocalUi, () => true, Token);
        await workflow.ResolveAsync(review, RequestOrigin.LocalUi, Token);
        var receipt = await workflow.InvokeAsync(review, RequestOrigin.LocalUi, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        receipt.Message.AssistantResponse.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("""{"kind":"toolCalls","calls":[{"callId":"one","capabilityId":"runtime.list","input":{}}]}""", ModelTurnReason.ToolUseNotPermitted)]
    [InlineData("""{"kind":"toolCalls","calls":[{"callId":"one","capabilityId":"application.get_version","input":{}},{"callId":"two","capabilityId":"application.get_version","input":{}}]}""", ModelTurnReason.ToolLoopBudgetExceeded)]
    [InlineData("""{"kind":"toolCalls","calls":[{"callId":"one","capabilityId":"computer.lock","input":{}}]}""", ModelTurnReason.InvalidProviderResponse)]
    [InlineData("""{"kind":"toolCalls","calls":[{"callId":"one","capabilityId":"network.get_web_page","input":{}}]}""", ModelTurnReason.InvalidProviderResponse)]
    [InlineData("""{"kind":"toolCalls","calls":[{"callId":"one","capabilityId":"application.get_version","input":{}},{"callId":"two","capabilityId":"runtime.list","input":{}}]}""", ModelTurnReason.ToolUseNotPermitted)]
    [InlineData("""{"kind":"toolCalls","calls":[{"callId":"one","capabilityId":"application.get_version","input":{"sessionId":"foreign"}}]}""", ModelTurnReason.InvalidProviderResponse)]
    public async Task InstructionToolAgentCannotEscalateSubsetBudgetOrEffects(string proposal, ModelTurnReason reason)
    {
        using var f = new Fixture();
        var sources = new AgentInstructionReferenceResolverTests.Fixture();
        var observed = 0;
        f.OnVersion = () => observed++;
        var transport = new ToolTransport(proposal);
        await using var loop = CreateLoop(f, transport);
        var host = ConfigureAgentLoop(f, loop);
        await using var workflow = new BoundedAgentWorkflow(InstructionToolAgent(), f, f, f.ConversationService(host, execution: host),
            host, f, f.Time, sources.Resolver);
        using var root = f.Root();
        using var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null), "original task",
            RequestOrigin.LocalUi, () => true, Token);
        await workflow.ResolveAsync(review, RequestOrigin.LocalUi, Token);
        var receipt = await workflow.InvokeAsync(review, RequestOrigin.LocalUi, Token);
        receipt.Message.Reason.Should().Be(reason);
        receipt.Message.AssistantResponse.Should().BeNull();
        observed.Should().Be(0);
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task InstructionToolAgentCompleteInputLimitRejectsWholeBundleWithoutDroppingGuidance()
    {
        using var f = new Fixture();
        var sources = new AgentInstructionReferenceResolverTests.Fixture();
        var reader = InstructionToolAgent();
        reader.Text = reader.Text.Replace("maximum-input-bytes: 32768", "maximum-input-bytes: 1000", StringComparison.Ordinal);
        var host = QualifiedHistoryHost(f);
        await using var workflow = new BoundedAgentWorkflow(reader, f, f, f.ConversationService(host, execution: host),
            host, f, f.Time, sources.Resolver);
        using var root = f.Root();
        using var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null), "original task",
            RequestOrigin.LocalUi, () => true, Token);
        await workflow.Invoking(value => value.ResolveAsync(review, RequestOrigin.LocalUi, Token))
            .Should().ThrowAsync<InvalidDataException>();
        review.IsInvocationReady.Should().BeFalse();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        reader.Released.Should().BeTrue();
    }

    [Fact]
    public async Task InstructionToolAgentExpiredDeadlineRejectsProposalWithoutCallbackOrLateAnswer()
    {
        using var f = new Fixture();
        var sources = new AgentInstructionReferenceResolverTests.Fixture();
        var observed = 0;
        f.OnVersion = () => observed++;
        var transport = new ToolTransport(VersionCall) { AfterRequest = _ => f.Time.Now = f.Time.Now.AddSeconds(16) };
        await using var loop = CreateLoop(f, transport);
        var host = ConfigureAgentLoop(f, loop);
        await using var workflow = new BoundedAgentWorkflow(InstructionToolAgent(), f, f, f.ConversationService(host, execution: host),
            host, f, f.Time, sources.Resolver);
        using var root = f.Root();
        using var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null), "original task",
            RequestOrigin.LocalUi, () => true, Token);
        await workflow.ResolveAsync(review, RequestOrigin.LocalUi, Token);
        var receipt = await workflow.InvokeAsync(review, RequestOrigin.LocalUi, Token);
        receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse.Should().BeNull();
        observed.Should().Be(0);
        transport.Requests.Should().ContainSingle();
        host.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task InstructionToolAgentStopJoinsActualBorrowedCallbackFromCancellationIgnoringAdapter()
    {
        using var f = new Fixture();
        var sources = new AgentInstructionReferenceResolverTests.Fixture();
        var reader = InstructionToolAgent();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fenced = false;
        var observed = 0;
        f.OnVersion = () => observed++;
        f.Adapter.OnTool = async invoke =>
        {
            entered.SetResult();
            await release.Task;
            Func<Task> late = async () => await invoke(ReadOnlyCapabilityCatalog.Version, "{}", CancellationToken.None);
            await late.Should().ThrowAsync<OperationCanceledException>();
            fenced = true;
        };
        var host = ConfigureAgentLoop(f, f.Adapter);
        await using var workflow = new BoundedAgentWorkflow(reader, f, f, f.ConversationService(host, execution: host),
            host, f, f.Time, sources.Resolver);
        using var root = f.Root();
        using var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null), "original task",
            RequestOrigin.LocalUi, () => true, Token);
        await workflow.ResolveAsync(review, RequestOrigin.LocalUi, Token);
        var running = workflow.InvokeAsync(review, RequestOrigin.LocalUi, Token);
        await entered.Task.WaitAsync(Token);
        var stopping = workflow.RevokeAsync(review);
        stopping.IsCompleted.Should().BeFalse();
        running.IsCompleted.Should().BeFalse();
        reader.Released.Should().BeFalse();
        host.IsQuiescent.Should().BeFalse();
        release.SetResult();
        await running.Invoking(value => value).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        await stopping;
        fenced.Should().BeTrue();
        observed.Should().Be(0);
        reader.Released.Should().BeTrue();
        host.IsQuiescent.Should().BeTrue();
        f.Retained!.AssistantResponse.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstructionToolAgentStopJoinsIgnoringProviderAndFencesLateToolCallback(bool revoke)
    {
        using var f = new Fixture();
        var sources = new AgentInstructionReferenceResolverTests.Fixture();
        var reader = InstructionToolAgent();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<ModelToolTransportReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new ToolTransport { Pending = pending.Task, AfterRequest = _ => entered.SetResult() };
        var observed = 0;
        f.OnVersion = () => observed++;
        await using var loop = CreateLoop(f, transport);
        var host = ConfigureAgentLoop(f, loop);
        await using var workflow = new BoundedAgentWorkflow(reader, f, f, f.ConversationService(host, execution: host),
            host, f, f.Time, sources.Resolver);
        using var root = f.Root();
        using var cancellation = new CancellationTokenSource();
        using var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null), "original task",
            RequestOrigin.LocalUi, () => true, Token);
        await workflow.ResolveAsync(review, RequestOrigin.LocalUi, Token);
        var running = workflow.InvokeAsync(review, RequestOrigin.LocalUi, cancellation.Token);
        await entered.Task.WaitAsync(Token);
        var stopping = revoke ? workflow.RevokeAsync(review) : cancellation.CancelAsync();
        running.IsCompleted.Should().BeFalse();
        reader.Released.Should().BeFalse();
        host.IsQuiescent.Should().BeFalse();
        pending.SetResult(ToolTransport.Reply(transport.Requests[0], VersionCall));
        await running.Invoking(value => value).Should().ThrowAsync<Exception>();
        await stopping;
        observed.Should().Be(0);
        transport.Requests.Should().ContainSingle();
        reader.Released.Should().BeTrue();
        host.IsQuiescent.Should().BeTrue();
        f.Retained!.AssistantResponse.Should().BeNull();
    }

    private static ModelTurnHost ConfigureAgentLoop(Fixture f, IModelTurnAdapter loop)
    {
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        return f.ConfiguredProfiles(adapter: loop);
    }
}
