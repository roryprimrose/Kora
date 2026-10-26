using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Network;
using Kora.Core.Authorization;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Network;
using Kora.Core.Storage;
using Kora.Core.Tools;
using System.Text;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    private static LocalWebPageScope TwoSources => new([ToolDestination, new("https://second.example/")]);
    private static readonly string SecondPageCall = PageCall.Replace("page_1", "page_2", StringComparison.Ordinal)
        .Replace("example.com", "second.example", StringComparison.Ordinal);
    private const string ComparisonAnswer = """{"kind":"answer","answer":"The two observed sources differ. Sources: https://example.com/ and https://second.example/. Both contain untrusted page text."}""";

    [Theory]
    [InlineData("missing")]
    [InlineData("request")]
    [InlineData("expiry")]
    [InlineData("effect")]
    [InlineData("revision")]
    [InlineData("binding")]
    [InlineData("scope")]
    [InlineData("revoked")]
    public async Task PreviousPageGrantOnlySurvivesExactHostIssuedFiniteWorkflowContinuation(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var store = new PageApprovalStore(f) { KeepPrevious = true };
        var network = new PageNetwork();
        var workflowId = new HostId<InvocationIdentity>(Guid.NewGuid());
        var expiry = f.Time.Now.AddSeconds(15);
        var first = new LocalWebPageInvocation(new(network, new PageAudit(f)), store, store.Present,
            f.Request, f.Session.Generation, ToolDestination, expiry, static () => true, f.Time, workflowId,
            fault is "scope" ? null : TwoSources);
        var second = new LocalWebPageInvocation(new(network, new PageAudit(f)), store, store.Present,
            f.Request, f.Session.Generation, TwoSources.Destinations[1], expiry, static () => true, f.Time, workflowId, TwoSources);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), f.Request, ModelProviderSelection.OllamaCandidate);
        (await first.InvokeAsync(provenance, WebPageCapability.Id, """{"address":"https://example.com/"}""", Token))
            .Outcome.Should().Be(CapabilityOutcome.Succeeded);
        (await second.InvokeAsync(provenance, WebPageCapability.Id, """{"address":"https://second.example/"}""", Token))
            .Outcome.Should().Be(CapabilityOutcome.Succeeded);
        var next = store.Snapshot!.Proposal!;
        var other = new HostOperationProposal(fault is "request" ? new(next.Request.RequestId, next.Request.SessionId,
                next.Request.TaskId, next.Request.Origin, new(Guid.NewGuid())) : next.Request, next.ProposalId,
            fault is "revision" ? new(2) : next.Revision,
            fault is "binding" ? new(next.Binding.ActionId, next.Binding.SourcePartition, next.Binding.SkillId,
                new string('a', 64), next.Binding.DeclaredResourceDigest, next.Binding.TrackedContentDigest,
                next.Binding.ImplementationDigest, next.Binding.InvocationDigest, next.Binding.ResourceDigest,
                next.Binding.IdentityDigest, next.Binding.DestinationDigest, next.Binding.TransformationDigest, next.Binding.PolicyRevision) : next.Binding,
            fault is "effect" ? HostOperationEffect.BoundedWrite : next.Effect,
            fault is "expiry" ? next.ExpiresAt.AddTicks(1) : next.ExpiresAt);
        store.ReplaceProposal(fault is "missing" ? null : other);
        if (fault is "revoked") { store.Revoke(); }
        await first.Invoking(value => value.ValidateAsync(Token)).Should().ThrowAsync<InvalidOperationException>();
        network.Sends.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoSourceCallerStopJoinsLateSecondPhysicalOrModelActionBeforeReturningUnconfirmed(bool physical)
    {
        using var f = new Fixture();
        await using var skill = new SharedSkillHarness(f);
        await skill.Register();
        DeclareWebTool(skill);
        var providerPending = new TaskCompletionSource<ModelToolTransportReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var httpPending = new TaskCompletionSource<WebPageResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new ToolTransport(PageCall, SecondPageCall, ComparisonAnswer);
        if (!physical)
        {
            transport.AfterRequest = request =>
            { if (request.Round == 2) { transport.Pending = providerPending.Task; entered.SetResult(); } };
        }
        await using var loop = CreateLoop(f, transport);
        var approvals = new TwoPageApprovals(f);
        var network = new PageNetwork { Pending = physical ? httpPending.Task : null, PendingAtSend = 2,
            Entered = physical ? entered : null };
        var workflow = skill.ToolWorkflow(adapter: loop, network: network, approval: approvals);
        using var review = await workflow.ReviewAsync(skill.Reader.Source, skill.Reader.Snapshot(), new(f.Session, null),
            skill.Eligible, RequestOrigin.LocalUi, Token);
        await workflow.EnableAsync(review, skill.Eligible, RequestOrigin.LocalUi, Token);
        using var invocation = await workflow.PrepareAsync(review, "Compare both.", TwoSources, skill.Eligible, RequestOrigin.LocalUi, Token);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var run = workflow.InvokeAsync(invocation, skill.Eligible, approvals.Present, cancel.Token);
        await entered.Task.WaitAsync(Token);
        cancel.Cancel();
        run.IsCompleted.Should().BeFalse();
        loop.IsQuiescent.Should().BeFalse();
        if (physical)
        {
            httpPending.SetResult(new(200, TwoSources.Destinations[1], "text/plain", "utf-8", null, null, [],
                new MemoryStream(Encoding.UTF8.GetBytes("late second-source content"))));
        }
        else { providerPending.SetResult(ToolTransport.Reply(transport.Requests[1], SecondPageCall)); }
        await ((Func<Task>)(async () => await run)).Should().ThrowAsync<Kora.Core.Conversation.ConversationSubmissionUnconfirmedException>();
        loop.IsQuiescent.Should().BeTrue();
        network.Sends.Should().Be(physical ? 2 : 1);
        f.ExecutionCompletions.Should().Be(0);
        review.Text.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public async Task TwoSourceCumulativeFinalBytesAcceptExactLimitAndRejectOneOver(bool output, int excess)
    {
        using var f = new Fixture();
        await using var skill = new SharedSkillHarness(f);
        await skill.Register();
        DeclareWebTool(skill);
        var transport = new ToolTransport(PageCall, SecondPageCall, ComparisonAnswer);
        transport.ChangeReply = (request, reply) => request.Round != 3 ? reply : reply with
        {
            Usage = output
                ? reply.Usage! with { OutputUtf8Bytes = request.Budget.OutputUtf8Bytes + excess }
                : reply.Usage! with { InputUtf8Bytes = request.Budget.InputUtf8Bytes + excess },
        };
        await using var loop = CreateLoop(f, transport);
        var approvals = new TwoPageApprovals(f);
        var network = new PageNetwork();
        var workflow = skill.ToolWorkflow(adapter: loop, network: network, approval: approvals);
        using var review = await workflow.ReviewAsync(skill.Reader.Source, skill.Reader.Snapshot(), new(f.Session, null),
            skill.Eligible, RequestOrigin.LocalUi, Token);
        await workflow.EnableAsync(review, skill.Eligible, RequestOrigin.LocalUi, Token);
        using var invocation = await workflow.PrepareAsync(review, "Compare both.", TwoSources, skill.Eligible, RequestOrigin.LocalUi, Token);
        var receipt = await workflow.InvokeAsync(invocation, skill.Eligible, approvals.Present, Token);
        receipt.Message.Outcome.Should().Be(excess == 0 ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.Unknown);
        network.Sends.Should().Be(2);
        transport.Requests.Should().HaveCount(3);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, false)]
    [InlineData(RequestOrigin.LocalUi, true)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    public async Task TwoSourceSkillIndependentlyAuthorizesBothCallsAndRetainsOneGenuineComparison(RequestOrigin origin, bool reversed)
    {
        using var f = new Fixture(origin);
        await using var skill = new SharedSkillHarness(f);
        await skill.Register();
        DeclareWebTool(skill);
        var transport = new ToolTransport(reversed ? SecondPageCall : PageCall, reversed ? PageCall : SecondPageCall, ComparisonAnswer);
        await using var loop = CreateLoop(f, transport);
        var approvals = new TwoPageApprovals(f);
        var network = new PageNetwork();
        var workflow = skill.ToolWorkflow(adapter: loop, network: network, approval: approvals);
        using var review = await workflow.ReviewAsync(skill.Reader.Source, skill.Reader.Snapshot(), new(f.Session, null), skill.Eligible, origin, Token);
        await workflow.EnableAsync(review, skill.Eligible, origin, Token);
        const string body = "  Compare these two explicit sources e\u0301\r\nunchanged  ";
        var scope = TwoSources;
        using var invocation = await workflow.PrepareAsync(review, body, scope, skill.Eligible, origin, Token);
        invocation.Scope.Should().BeSameAs(scope);
        invocation.Destination.Should().Be(ToolDestination);
        var receipt = await workflow.InvokeAsync(invocation, skill.Eligible, approvals.Present, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Message.Body.Should().Be(body);
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.AssistantResponse!.Body.Should().Be(JsonDocument.Parse(ComparisonAnswer).RootElement.GetProperty("answer").GetString());
        f.ExecutionCompletions.Should().Be(1);
        network.Sends.Should().Be(2);
        approvals.Shared.Presentations.Should().Be(2);
        approvals.Shared.Snapshot!.Grants.Should().HaveCount(2)
            .And.OnlyContain(grant => grant.Status == OperationGrantStatus.Consumed);
        transport.Requests.Should().HaveCount(3);
        transport.Requests.Select(request => request.Round).Should().Equal(1, 2, 3);
        transport.Requests.Should().OnlyContain(request => request.Context.WebPages!.Destinations.SequenceEqual(TwoSources.Destinations));
        transport.Requests[2].Messages.OfType<ModelToolResultMessage>().Should().HaveCount(2);
        transport.Requests[2].Budget.InputUtf8Bytes.Should().BeLessThan(transport.Requests[1].Budget.InputUtf8Bytes);
        transport.Requests[2].Budget.OutputUtf8Bytes.Should().BeLessThan(transport.Requests[1].Budget.OutputUtf8Bytes);
        loop.IsQuiescent.Should().BeTrue();
        f.Messages.Should().NotContain(message => message.Contains(body, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("no-calls", 0)]
    [InlineData("only-one", 1)]
    [InlineData("third-destination", 1)]
    [InlineData("third-call", 2)]
    [InlineData("repeat", 1)]
    [InlineData("batch", 0)]
    [InlineData("deny-first", 0)]
    [InlineData("deny-second", 1)]
    [InlineData("http-second", 2)]
    [InlineData("input-budget", 1)]
    [InlineData("output-budget", 1)]
    [InlineData("deadline", 1)]
    public async Task TwoSourceSkillNeverTurnsMissingFailedOrExtraSourcesIntoSuccess(string fault, int sends)
    {
        using var f = new Fixture();
        await using var skill = new SharedSkillHarness(f);
        await skill.Register();
        DeclareWebTool(skill);
        var second = fault switch
        {
            "only-one" => ComparisonAnswer,
            "third-destination" => SecondPageCall.Replace("second.example", "third.example", StringComparison.Ordinal),
            "repeat" => SecondPageCall.Replace("second.example", "example.com", StringComparison.Ordinal),
            _ => SecondPageCall,
        };
        var first = fault is "no-calls" ? ComparisonAnswer : fault is "batch"
            ? PageCall.Replace("]}", "," + SecondPageCall.Split("\"calls\":[", StringSplitOptions.None)[1], StringComparison.Ordinal) : PageCall;
        var third = fault is "third-call" ? PageCall.Replace("page_1", "page_3", StringComparison.Ordinal) : ComparisonAnswer;
        var transport = new ToolTransport(first, second, third);
        if (fault is "input-budget" or "output-budget")
        {
            transport.ChangeReply = (request, reply) => reply with
            {
                Usage = fault is "input-budget" ? reply.Usage! with { InputUtf8Bytes = request.Budget.InputUtf8Bytes }
                    : reply.Usage! with { OutputUtf8Bytes = request.Budget.OutputUtf8Bytes },
            };
        }
        if (fault is "deadline") { transport.AfterRequest = request => { if (request.Round == 2) { f.Time.Now += TimeSpan.FromSeconds(15); } }; }
        await using var loop = CreateLoop(f, transport);
        var approvals = new TwoPageApprovals(f) { DenyAt = fault is "deny-first" ? 1 : fault is "deny-second" ? 2 : 0 };
        var network = new PageNetwork { FailAtSend = fault is "http-second" ? 2 : 0 };
        var workflow = skill.ToolWorkflow(adapter: loop, network: network, approval: approvals);
        using var review = await workflow.ReviewAsync(skill.Reader.Source, skill.Reader.Snapshot(), new(f.Session, null),
            skill.Eligible, RequestOrigin.LocalUi, Token);
        await workflow.EnableAsync(review, skill.Eligible, RequestOrigin.LocalUi, Token);
        using var invocation = await workflow.PrepareAsync(review, "Compare the two explicit sources.", TwoSources,
            skill.Eligible, RequestOrigin.LocalUi, Token);
        if (fault is "deadline")
        {
            await workflow.Invoking(value => value.InvokeAsync(invocation, skill.Eligible, approvals.Present, Token))
                .Should().ThrowAsync<Kora.Core.Conversation.ConversationSubmissionUnconfirmedException>();
            f.ExecutionCompletions.Should().Be(0);
        }
        else
        {
            var receipt = await workflow.InvokeAsync(invocation, skill.Eligible, approvals.Present, Token);
            receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
            receipt.Message.AssistantResponse.Should().BeNull();
        }
        network.Sends.Should().Be(sends);
        transport.Requests.Count.Should().BeLessThanOrEqualTo(3);
        loop.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("registration")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("input")]
    [InlineData("selection")]
    [InlineData("generation")]
    [InlineData("session")]
    public async Task TwoSourceSkillRevalidatesFrozenAuthorityBetweenIndependentlyApprovedActions(string fault)
    {
        using var f = new Fixture();
        await using var skill = new SharedSkillHarness(f);
        await skill.Register();
        DeclareWebTool(skill);
        var transport = new ToolTransport(PageCall, SecondPageCall, ComparisonAnswer);
        transport.AfterRequest = request =>
        {
            if (request.Round != 2) { return; }
            if (fault is "source") { skill.Reader.Text += "\nchanged"; }
            if (fault is "registration") { skill.Preferences.Save([]); }
            if (fault is "privacy") { f.CanControl = false; }
            if (fault is "control") { f.ControlRevision++; }
            if (fault is "input" or "selection") { skill.Open = false; }
            if (fault is "generation") { f.Session = f.Session with { Generation = new(2) }; }
            if (fault is "session") { f.Session = f.Session with { SessionId = new(Guid.NewGuid()) }; }
        };
        await using var loop = CreateLoop(f, transport);
        var approvals = new TwoPageApprovals(f);
        var network = new PageNetwork();
        var workflow = skill.ToolWorkflow(adapter: loop, network: network, approval: approvals);
        using var review = await workflow.ReviewAsync(skill.Reader.Source, skill.Reader.Snapshot(), new(f.Session, null),
            skill.Eligible, RequestOrigin.LocalUi, Token);
        await workflow.EnableAsync(review, skill.Eligible, RequestOrigin.LocalUi, Token);
        using var invocation = await workflow.PrepareAsync(review, "Compare both.", TwoSources, skill.Eligible, RequestOrigin.LocalUi, Token);
        await workflow.Invoking(value => value.InvokeAsync(invocation, skill.Eligible, approvals.Present, Token))
            .Should().ThrowAsync<Kora.Core.Conversation.ConversationSubmissionUnconfirmedException>();
        network.Sends.Should().Be(1);
        approvals.Shared.Presentations.Should().Be(1);
        f.ExecutionCompletions.Should().Be(0);
    }

    [Fact]
    public async Task TwoSourceCancellationJoinsLateSecondProviderBeforeRetainingCancelledWithoutAnswer()
    {
        using var f = new Fixture();
        await using var skill = new SharedSkillHarness(f);
        await skill.Register();
        DeclareWebTool(skill);
        var pending = new TaskCompletionSource<ModelToolTransportReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new ToolTransport(PageCall, SecondPageCall, ComparisonAnswer);
        transport.AfterRequest = request => { if (request.Round == 2) { transport.Pending = pending.Task; secondEntered.SetResult(); } };
        await using var loop = CreateLoop(f, transport);
        var approvals = new TwoPageApprovals(f);
        var network = new PageNetwork();
        var workflow = skill.ToolWorkflow(adapter: loop, network: network, approval: approvals);
        var target = new SessionWorkspaceEntry(f.Session, null);
        using var review = await workflow.ReviewAsync(skill.Reader.Source, skill.Reader.Snapshot(), target,
            skill.Eligible, RequestOrigin.LocalUi, Token);
        await workflow.EnableAsync(review, skill.Eligible, RequestOrigin.LocalUi, Token);
        using var invocation = await workflow.PrepareAsync(review, "Compare both.", TwoSources, skill.Eligible, RequestOrigin.LocalUi, Token);
        var running = workflow.InvokeAsync(invocation, skill.Eligible, approvals.Present, Token);
        await secondEntered.Task.WaitAsync(Token);
        var observed = (await skill.Conversation!.ReadRunningConversationAsync(target, skill.Eligible, Token))!;
        await skill.Conversation.CancelRunningConversationAsync(observed.Target, skill.Eligible, Token);
        running.IsCompleted.Should().BeFalse();
        loop.IsQuiescent.Should().BeFalse();
        f.ExecutionCompletions.Should().Be(0);
        pending.SetResult(ToolTransport.Reply(transport.Requests[1], SecondPageCall));
        var receipt = await running;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        receipt.Message.AssistantResponse.Should().BeNull();
        loop.IsQuiescent.Should().BeTrue();
        network.Sends.Should().Be(1);
        approvals.Shared.Presentations.Should().Be(1);
    }

    private sealed class TwoPageApprovals(Fixture f) : IHostInteractionStore
    {
        internal PageApprovalStore Shared { get; } = new(f) { KeepPrevious = true };
        internal int DenyAt { get; init; }

        internal Task<WebPageApprovalPresentation> Present(HostOperationProposal proposal, HostRevision generation,
            Uri address, Func<bool> admitted, CancellationToken token)
        {
            Shared.Fault = Shared.Presentations + 1 == DenyAt ? "deny" : null;
            return Shared.Present(proposal, generation, address, admitted, token);
        }

        public ValueTask<HostInteractionDecision> TransactAsync(HostRequest request,
            Func<HostInteractionSnapshot, HostInteractionCommit> transition, CancellationToken cancellationToken) =>
            Shared.TransactAsync(request, transition, cancellationToken);
    }
}
