using System.Collections.Immutable;
using System.Text;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Application.Mcp;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Mcp;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(CapabilityOutcome.Succeeded, McpExplicitOutcome.Succeeded)]
    [InlineData(CapabilityOutcome.Denied, McpExplicitOutcome.Denied)]
    [InlineData(CapabilityOutcome.Failed, McpExplicitOutcome.Failed)]
    public void Explicit_mcp_results_preserve_exact_capability_outcomes(CapabilityOutcome input, McpExplicitOutcome expected) =>
        McpExplicitResult.From(input).Should().Be(expected);

    [Theory]
    [InlineData("configuration")]
    [InlineData("broker")]
    [InlineData("effects")]
    [InlineData("interactions")]
    [InlineData("audit")]
    [InlineData("coordinator")]
    [InlineData("task-store")]
    public async Task Explicit_mcp_requires_every_consumed_host_contract_before_creating_an_intent(string missing)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var broker = new McpBroker(source.Account);
        var approvals = new McpApprovals(f);
        var workspace = new SessionWorkspaceService(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance);
        var service = new SessionConversationService(f, missing is "task-store" ? new CompoundReaderUnavailableStore(f) : f,
            workspace, f, f.Host, f.Time, NullLogger<SessionConversationService>.Instance,
            cancellationAudit: missing is "audit" ? null : f.CancelAudit,
            interactions: missing is "interactions" ? null : approvals,
            mcpConfiguration: missing is "configuration" ? null : source.Configuration,
            mcpBroker: missing is "broker" ? null : broker,
            mcpEffects: missing is "effects" ? null : await source.Policy(),
            explicitTaskCoordinator: missing is "coordinator" ? null : new(f));
        var original = f.Task;
        var action = () => service.InvokeMcpAsync(new(f.Session, null), source.ServerId, source.Account,
            approvals.Present, McpSelect, ExplicitMcpInput, static () => true, Token);
        await action.Should().ThrowAsync<McpUnavailableException>();
        f.Task.Should().BeSameAs(original);
        broker.Connected.Should().BeFalse();
    }

    [Theory]
    [InlineData("control")]
    [InlineData("inactive")]
    [InlineData("session")]
    [InlineData("during-read")]
    [InlineData("cancelled")]
    public async Task Explicit_mcp_initial_source_must_be_live_before_any_intent_or_connection(string fault)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        using var cancellation = new CancellationTokenSource();
        var broker = new McpBroker(source.Account);
        var approvals = new McpApprovals(f);
        var service = McpService(f, f.Host, source, broker, approvals, await source.Policy());
        var target = new SessionWorkspaceEntry(f.Session, null);
        if (fault is "control") { f.CanControl = false; }
        if (fault is "inactive") { target = new(f.Session with { IsActive = false }, null); }
        if (fault is "session") { f.Session = f.Session with { Generation = new(2) }; }
        if (fault is "during-read") { f.OnRead = () => f.CanControl = false; }
        if (fault is "cancelled") { await cancellation.CancelAsync(); }
        var original = f.Task;
        var action = () => service.InvokeMcpAsync(target, source.ServerId, source.Account,
            approvals.Present, McpSelect, ExplicitMcpInput, static () => true, cancellation.Token);
        if (fault is "cancelled") { await action.Should().ThrowAsync<OperationCanceledException>(); }
        else { await action.Should().ThrowAsync<InvalidOperationException>(); }
        f.Task.Should().BeSameAs(original);
        broker.Connected.Should().BeFalse();
    }

    [Theory]
    [InlineData("control")]
    [InlineData("session")]
    [InlineData("during-read")]
    [InlineData("cancel")]
    [InlineData("unavailable")]
    public async Task Explicit_mcp_admission_revalidates_committed_original_input_and_settles_without_dispatch(string fault)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var broker = new McpBroker(source.Account);
        var approvals = new McpApprovals(f);
        var service = McpService(f, f.Host, source, broker, approvals, await source.Policy());
        var target = new SessionWorkspaceEntry(f.Session, null);
        f.ExplicitIntentRecorded = () =>
        {
            if (fault is "control") { f.CanControl = false; }
            if (fault is "session") { f.Session = f.Session with { Generation = new(2) }; }
            if (fault is "during-read") { f.OnRead = () => f.CanControl = false; }
        };
        f.ExplicitDispatchFault = fault;
        var result = await service.InvokeMcpAsync(target, source.ServerId, source.Account,
            approvals.Present, McpSelect, ExplicitMcpInput, static () => true, Token);
        result.Outcome.Should().Be(fault is "cancel" ? McpExplicitOutcome.Cancelled
            : fault is "unavailable" ? McpExplicitOutcome.Unavailable : McpExplicitOutcome.Denied);
        result.DurableState.Should().Be(fault is "cancel" ? HostTaskState.Cancelled : HostTaskState.Failed);
        broker.Connected.Should().BeFalse();
        result.Result.Should().BeNull();
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("source")]
    [InlineData("cancel")]
    public async Task Explicit_mcp_revalidates_after_physical_transport_join_before_claiming_success(string fault)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        using var cancellation = new CancellationTokenSource();
        var broker = new McpBroker(source.Account);
        broker.AfterDispose = () =>
        {
            if (fault is "permission") { f.CanControl = false; }
            if (fault is "source") { File.AppendAllText(source.File, " "); }
            if (fault is "cancel") { cancellation.Cancel(); }
        };
        var approvals = new McpApprovals(f);
        var service = McpService(f, f.Host, source, broker, approvals, await source.Policy());
        var result = await service.InvokeMcpAsync(new(f.Session, null), source.ServerId, source.Account,
            approvals.Present, McpSelect, ExplicitMcpInput, static () => true, cancellation.Token);
        result.Outcome.Should().Be(fault is "cancel" ? McpExplicitOutcome.Cancelled : McpExplicitOutcome.Denied);
        result.Result.Should().BeNull();
        result.DurableState.Should().Be(HostTaskState.Unknown);
        broker.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong")]
    [InlineData("cancel")]
    [InlineData("control")]
    [InlineData("source")]
    [InlineData("source-during-read")]
    [InlineData("session")]
    [InlineData("last-read")]
    public async Task Explicit_mcp_terminal_readback_and_disclosure_remain_bound_to_original_authority(string fault)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        using var cancellation = new CancellationTokenSource();
        var broker = new McpBroker(source.Account);
        var approvals = new McpApprovals(f);
        var service = McpService(f, f.Host, source, broker, approvals, await source.Policy());
        var target = new SessionWorkspaceEntry(f.Session, null);
        var altered = false;
        f.BeforeTaskRead = () =>
        {
            if (altered || f.Task?.Task.IsTerminal != true) { return Task.CompletedTask; }
            altered = true;
            switch (fault)
            {
                case "missing": f.Task = null; break;
                case "wrong": f.Task = f.Task with { Task = new(f.Request, new(99), HostTaskState.Unknown) }; break;
                case "cancel": cancellation.Cancel(); break;
                case "control": f.CanControl = false; break;
                case "source": File.AppendAllText(source.File, " "); break;
                case "source-during-read": source.BeforeLoad = () => f.CanControl = false; break;
                case "session": f.Session = f.Session with { Generation = new(2) }; break;
                case "last-read": f.OnRead = () => f.CanControl = false; break;
            }
            return Task.CompletedTask;
        };
        var action = () => service.InvokeMcpAsync(target, source.ServerId, source.Account,
            approvals.Present, McpSelect, ExplicitMcpInput, static () => true, cancellation.Token);
        if (fault is "missing" or "wrong") { await action.Should().ThrowAsync<IOException>(); }
        else
        {
            var result = await action();
            result.Outcome.Should().Be(fault is "cancel" ? McpExplicitOutcome.Cancelled : McpExplicitOutcome.Denied);
            result.Result.Should().BeNull();
            result.DurableState.Should().Be(HostTaskState.Succeeded, "a committed outcome is not rolled back; disclosure still fails closed");
        }
        broker.Disposed.Should().BeTrue();
        f.Retained.Should().BeNull();
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task Explicit_mcp_completes_the_original_session_without_model_selection_or_retaining_a_fabricated_answer(RequestOrigin origin)
    {
        using var f = new Fixture(origin);
        using var source = new McpSource();
        var broker = new McpBroker(source.Account);
        var approvals = new McpApprovals(f);
        var service = McpService(f, f.Host, source, broker, approvals, await source.Policy());
        var statuses = new List<Kora.Core.Conversation.ConversationExecutionStatus>();
        service.RunningConversationChanged += statuses.Add;
        var result = await service.InvokeMcpAsync(new(f.Session, null), source.ServerId, source.Account,
            approvals.Present, McpSelect, ExplicitMcpInput, static () => true, Token, origin);
        result.Outcome.Should().Be(McpExplicitOutcome.Succeeded);
        result.Request.SessionId.Should().Be(f.Session.SessionId);
        result.Request.Origin.Should().Be(origin);
        result.Result!.Text.Should().Contain("value 42");
        result.DurableState.Should().Be(HostTaskState.Succeeded);
        f.Task!.Task.Request.Should().Be(result.Request);
        f.Task.Task.State.Should().Be(HostTaskState.Succeeded);
        f.Retained.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
        broker.Methods.Should().Equal("initialize", "tools/list", "tools/call");
        broker.Disposed.Should().BeTrue();
        approvals.Stages.Should().Equal(McpReviewStage.Connection, McpReviewStage.ToolSelection, McpReviewStage.Invocation);
        var review = approvals.LastReview!;
        review.Source!.ServerName.Should().Be("reference");
        review.Source.Path.Should().Be(source.File);
        review.Source.ToString().Should().Be(nameof(McpReviewSource));
        review.NativeText.Should().Contain(source.File).And.Contain("Configured server: reference");
        (review with { Source = null }).IsBoundTo(approvals.LastProposal!).Should().BeFalse();
        (review with { Source = review.Source with { ServerName = "other" } }).IsBoundTo(approvals.LastProposal!).Should().BeFalse();
        (review with { Source = review.Source with { Path = Path.Combine(Path.GetDirectoryName(source.File)!, "other.json") } })
            .IsBoundTo(approvals.LastProposal!).Should().BeFalse();
        var sourceRevisionSwap = review.Source with { Revision = new string('a', 64) };
        var proposal = approvals.LastProposal!;
        var b = proposal.Binding;
        var alteredBinding = new Kora.Core.Authorization.ExactOperationBinding(b.ActionId, b.SourcePartition, b.SkillId,
            b.DefinitionDigest, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(sourceRevisionSwap)))),
            b.TrackedContentDigest, b.ImplementationDigest, b.InvocationDigest, b.ResourceDigest,
            b.IdentityDigest, b.DestinationDigest, b.TransformationDigest, b.PolicyRevision);
        (review with { Source = sourceRevisionSwap }).IsBoundTo(new(proposal.Request, proposal.ProposalId, proposal.Revision,
            alteredBinding, proposal.Effect, proposal.ExpiresAt)).Should().BeFalse();
        (await service.ReadRunningConversationAsync(new(f.Session, null), static () => true, Token)).Should().BeNull();
        f.Messages.Should().NotContain(message => message.Contains("value 42", StringComparison.Ordinal));
        statuses.Select(status => status.Phase).Should().Equal(Kora.Core.Conversation.ConversationExecutionPhase.Running,
            Kora.Core.Conversation.ConversationExecutionPhase.Completing, Kora.Core.Conversation.ConversationExecutionPhase.Terminal);
    }

    [Fact]
    public async Task Explicit_mcp_join_failure_never_releases_a_confirmed_terminal_or_discloses_content()
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var broker = new McpBroker(source.Account) { AfterDispose = () => throw new IOException("Synthetic physical join failure.") };
        var approvals = new McpApprovals(f);
        var service = McpService(f, f.Host, source, broker, approvals, await source.Policy());
        var statuses = new List<Kora.Core.Conversation.ConversationExecutionStatus>();
        service.RunningConversationChanged += statuses.Add;
        var action = () => service.InvokeMcpAsync(new(f.Session, null), source.ServerId, source.Account,
            approvals.Present, McpSelect, ExplicitMcpInput, static () => true, Token);
        await action.Should().ThrowAsync<IOException>();
        f.Task!.Task.State.Should().Be(HostTaskState.DispatchRecorded);
        statuses.Should().NotContain(status => status.Phase == Kora.Core.Conversation.ConversationExecutionPhase.Terminal);
        statuses[^1].Phase.Should().Be(Kora.Core.Conversation.ConversationExecutionPhase.Unconfirmed);
        f.Retained.Should().BeNull();
    }

    [Theory]
    [InlineData("missing-required")]
    [InlineData("extra-field")]
    [InlineData("wrong-type")]
    [InlineData("cancel-input")]
    [InlineData("private")]
    [InlineData("permission")]
    [InlineData("expiry")]
    [InlineData("source")]
    [InlineData("server")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("revoked")]
    [InlineData("input-io")]
    public async Task Explicit_mcp_input_and_current_authority_fail_closed_before_the_only_tool_send(string fault)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var broker = new McpBroker(source.Account);
        var approvals = new McpApprovals(f);
        var service = McpService(f, f.Host, source, broker, approvals, await source.Policy());
        Task<string?> Input(HostRequest request, HostRevision generation, DateTimeOffset expires,
            McpModelTool tool, Func<bool> admitted, CancellationToken token)
        {
            switch (fault)
            {
                case "private": f.ControlRevision++; break;
                case "permission": f.CanControl = false; break;
                case "expiry": f.Time.Now = expires; break;
                case "source": File.AppendAllText(source.File, " "); break;
                case "server": broker.Fault = "redirect"; break;
                case "session": f.Session = f.Session with { IsActive = false }; break;
                case "generation": f.Session = f.Session with { Generation = new(2) }; break;
                case "revoked": approvals.Revoke(); break;
                case "input-io": throw new IOException("synthetic input storage failure");
            }
            return Task.FromResult(fault switch
            {
                "missing-required" => "{}",
                "extra-field" => """{"query":"reference","extra":true}""",
                "wrong-type" => """{"query":7}""",
                "cancel-input" => null,
                _ => """{"query":"reference"}""",
            });
        }
        var result = await service.InvokeMcpAsync(new(f.Session, null), source.ServerId, source.Account,
            approvals.Present, McpSelect, Input, static () => true, Token);
        result.Outcome.Should().Be(fault is "cancel-input" ? McpExplicitOutcome.Cancelled
            : fault is "input-io" ? McpExplicitOutcome.Unknown : McpExplicitOutcome.Denied);
        result.DurableState.Should().Be(HostTaskState.Unknown);
        result.Result.Should().BeNull();
        broker.Methods.Should().Equal("initialize", "tools/list");
        broker.Disposed.Should().BeTrue();
        f.Retained.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("denied-review")]
    [InlineData("denied-account")]
    [InlineData("unknown-effect")]
    [InlineData("unsupported-schema")]
    [InlineData("server-error")]
    [InlineData("unavailable")]
    public async Task Explicit_mcp_admission_and_server_failure_are_not_model_success(string fault)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var broker = new McpBroker(source.Account) { Fault = fault };
        var approvals = new McpApprovals(f) { Deny = fault is "denied-review" };
        var service = McpService(f, f.Host, source, broker, approvals,
            fault is "unknown-effect" ? new([]) : await source.Policy());
        var result = await service.InvokeMcpAsync(new(f.Session, null), source.ServerId, source.Account,
            approvals.Present, McpSelect, ExplicitMcpInput, static () => true, Token);
        result.Outcome.Should().Be(fault is "server-error" ? McpExplicitOutcome.Failed
            : fault is "unavailable" ? McpExplicitOutcome.Unavailable : McpExplicitOutcome.Denied);
        result.Result.Should().BeNull();
        broker.Methods.Count(method => string.Equals(method, "tools/call", StringComparison.Ordinal))
            .Should().Be(fault is "server-error" ? 1 : 0);
        f.Adapter.Calls.Should().Be(0);
        f.Retained.Should().BeNull();
    }

    [Theory]
    [InlineData("disconnect")]
    [InlineData("body")]
    [InlineData("disposal")]
    [InlineData("timeout")]
    [InlineData("withdrawal")]
    public async Task Explicit_mcp_transport_failure_joins_one_original_dispatch_without_retry_or_disclosure(string fault)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var pending = new TaskCompletionSource<ImmutableArray<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var broker = new McpBroker(source.Account) { PendingCall = pending, EnteredCall = entered };
        var approvals = new McpApprovals(f);
        var service = McpService(f, f.Host, source, broker, approvals, await source.Policy());
        var target = new SessionWorkspaceEntry(f.Session, null);
        var operation = service.InvokeMcpAsync(target, source.ServerId, source.Account,
            approvals.Present, McpSelect, ExplicitMcpInput, static () => true, Token);
        await entered.Task.WaitAsync(Token);
        try
        {
            operation.IsCompleted.Should().BeFalse();
            broker.Disposed.Should().BeFalse();
            f.Task!.Task.State.Should().Be(HostTaskState.DispatchRecorded);
            (await service.ReadRunningConversationAsync(target, static () => true, Token)).Should().NotBeNull();
        }
        finally
        {
            if (fault is "withdrawal") { broker.Fault = "withdrawn-connection"; }
            pending.TrySetException(fault switch
            {
                "disconnect" => new HttpRequestException("synthetic private disconnect"),
                "body" => new IOException("synthetic private body failure"),
                "disposal" => new ObjectDisposedException("synthetic private disposed transport"),
                _ => new OperationCanceledException("synthetic transport-owned cancellation"),
            });
        }
        var result = await operation;
        result.Request.Should().Be(f.Task!.Task.Request);
        result.Outcome.Should().Be(McpExplicitOutcome.Failed);
        result.DurableState.Should().Be(HostTaskState.Unknown);
        result.Result.Should().BeNull();
        broker.Methods.Should().Equal("initialize", "tools/list", "tools/call");
        broker.Disposed.Should().BeTrue();
        f.Adapter.Calls.Should().Be(0);
        f.Retained.Should().BeNull();
        (await service.ReadRunningConversationAsync(target, static () => true, Token)).Should().BeNull();
        f.CancelAudit.Events.Where(item => string.Equals(item.ActionId, "mcp.exchange", StringComparison.Ordinal))
            .Select(item => item.Outcome).Should().Equal(
                Kora.Core.Auditing.SecurityAuditOutcome.Requested, Kora.Core.Auditing.SecurityAuditOutcome.Succeeded,
                Kora.Core.Auditing.SecurityAuditOutcome.Requested, Kora.Core.Auditing.SecurityAuditOutcome.Succeeded,
                Kora.Core.Auditing.SecurityAuditOutcome.Requested, Kora.Core.Auditing.SecurityAuditOutcome.Unknown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_mcp_cancellation_joins_ignoring_transport_and_rejects_late_result(bool hostCancel)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        using var caller = new CancellationTokenSource();
        var pending = new TaskCompletionSource<ImmutableArray<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var broker = new McpBroker(source.Account) { PendingCall = pending, EnteredCall = entered };
        var approvals = new McpApprovals(f);
        var service = McpService(f, f.Host, source, broker, approvals, await source.Policy());
        var target = new SessionWorkspaceEntry(f.Session, null);
        var operation = service.InvokeMcpAsync(target, source.ServerId, source.Account,
            approvals.Present, McpSelect, ExplicitMcpInput, static () => true, caller.Token);
        await entered.Task.WaitAsync(Token);
        if (hostCancel)
        {
            var running = (await service.ReadRunningConversationAsync(target, static () => true, Token))!;
            var cancellation = await service.CancelRunningConversationAsync(running.Target, static () => true, Token);
            cancellation.Phase.Should().Be(Kora.Core.Conversation.ConversationExecutionPhase.Cancelling);
        }
        else { await caller.CancelAsync(); }
        operation.IsCompleted.Should().BeFalse("cancellation-ignoring transport is still physically owned");
        broker.Disposed.Should().BeFalse();
        f.Task!.Task.State.Should().Be(HostTaskState.DispatchRecorded);
        pending.SetResult(ImmutableArray.CreateRange(Encoding.UTF8.GetBytes(
            """{"jsonrpc":"2.0","id":3,"result":{"content":[{"type":"text","text":"late private result"}],"isError":false}}""")));
        var result = await operation;
        result.Outcome.Should().Be(McpExplicitOutcome.Cancelled);
        result.DurableState.Should().Be(HostTaskState.Unknown);
        result.Result.Should().BeNull();
        broker.Disposed.Should().BeTrue();
        f.Retained.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
        f.CancelAudit.Events.Should().Contain(item => item.ActionId == "mcp.exchange"
            && item.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Unknown);
        (await service.ReadRunningConversationAsync(target, static () => true, Token)).Should().BeNull();
    }

    [Fact]
    public async Task Explicit_mcp_dispatch_is_single_use_and_never_accepts_model_provenance_as_original_user_authority()
    {
        using var f = new Fixture();
        using var source = new McpSource();
        using var root = f.Root();
        var broker = new McpBroker(source.Account);
        var approvals = new McpApprovals(f);
        await using var invocation = McpUse(f, source, broker, approvals, await source.Policy());
        (await invocation.InvokeOriginalUserAsync(ExplicitMcpInput, Token)).Outcome.Should().Be(CapabilityOutcome.Denied);
        invocation.Invoking(value => value.BindOriginalUserTask(f.Task!.Task)).Should().Throw<InvalidOperationException>();
        var dispatched = f.Task!.Task.Next(HostTaskState.DispatchRecorded);
        invocation.Invoking(value => value.BindOriginalUserTask(new(HostRequest.Create(RequestOrigin.LocalUi),
            new(2), HostTaskState.DispatchRecorded))).Should().Throw<InvalidOperationException>();
        invocation.Invoking(value => value.BindOriginalUserTask(new(f.Request,
            new(3), HostTaskState.DispatchRecorded))).Should().Throw<InvalidOperationException>();
        f.Task = f.Task with { Task = dispatched, Source = "local-context" };
        invocation.BindOriginalUserTask(dispatched);
        invocation.Invoking(value => value.BindOriginalUserTask(dispatched)).Should().Throw<InvalidOperationException>();
        await invocation.PrepareAsync(Token);
        var context = new ModelContextEnvelope(f.Request, "policy", "task", [], f.Time.Now, f.Time.Now.AddSeconds(15),
            ModelToolMode.McpReadOnly, mcpTool: invocation.ModelTool);
        var turn = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, context, Token)).Turn!;
        invocation.Invoking(value => value.BindTurn(turn)).Should().Throw<InvalidOperationException>();
        await using var modelUse = McpUse(f, source, new(source.Account), new(f), await source.Policy());
        modelUse.BindTurn(turn);
        modelUse.Invoking(value => value.BindOriginalUserTask(dispatched)).Should().Throw<InvalidOperationException>();
        (await invocation.InvokeAsync(new(new(Guid.NewGuid()), f.Request, ModelProviderSelection.OllamaCandidate),
            "mcp.search", """{"query":"reference"}""", Token)).Outcome.Should().Be(CapabilityOutcome.Denied);
        (await invocation.InvokeOriginalUserAsync(ExplicitMcpInput, Token)).Outcome.Should().Be(CapabilityOutcome.Succeeded);
        (await invocation.InvokeOriginalUserAsync(ExplicitMcpInput, Token)).Outcome.Should().Be(CapabilityOutcome.Denied);
        broker.Methods.Should().Equal("initialize", "tools/list", "tools/call");
    }

    private static Task<string?> ExplicitMcpInput(HostRequest request, HostRevision generation, DateTimeOffset expires,
        McpModelTool tool, Func<bool> admitted, CancellationToken token) =>
        Task.FromResult<string?>("""{"query":"reference"}""");

    private sealed partial class Fixture : ILocalContextTaskStore
    {
        internal Action? ExplicitIntentRecorded { get; set; }
        internal string? ExplicitDispatchFault { get; set; }
        public ValueTask<HostTaskRecord> AdmitLocalContextDispatchAsync(HostRequest request, HostRevision generation,
            Func<bool> eligible, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (ExplicitDispatchFault is "cancel") { throw new OperationCanceledException("Synthetic dispatch cancellation."); }
            if (ExplicitDispatchFault is "unavailable") { throw new McpUnavailableException(); }
            eligible().Should().BeTrue();
            Task!.Task.Request.Should().Be(request);
            Task.Generation.Should().Be(generation);
            Task = Task with { Task = Task.Task.Next(HostTaskState.DispatchRecorded), Source = "local-context" };
            return ValueTask.FromResult(Task.Task);
        }
    }
}
