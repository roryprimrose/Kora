using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Mcp;
using Kora.Core.Authorization;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Mcp;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("presented-proposal")]
    [InlineData("presented-request")]
    [InlineData("presented-generation")]
    [InlineData("missing-grant")]
    [InlineData("grant-scope")]
    [InlineData("grant-proposal")]
    [InlineData("grant-generation")]
    [InlineData("missing-answer")]
    [InlineData("answer-status")]
    [InlineData("answer-id")]
    [InlineData("answer-request")]
    [InlineData("answer-proposal")]
    [InlineData("answer-draft")]
    [InlineData("answer-text")]
    [InlineData("answer-choice")]
    [InlineData("answer-channel")]
    [InlineData("connection-voice")]
    [InlineData("consume-grant")]
    [InlineData("consume-admission")]
    [InlineData("dispatch-denied")]
    [InlineData("dispatch-admission")]
    public async Task Mcp_native_approval_identity_scope_and_answer_cannot_be_forged(string fault)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        using var root = f.Root();
        var broker = new McpBroker(source.Account);
        var approvals = new McpApprovals(f) { Fault = fault };
        await using var use = McpUse(f, source, broker, approvals, await source.Policy());
        await use.Invoking(value => value.PrepareAsync(Token)).Should().ThrowAsync<InvalidOperationException>();
        broker.Connected.Should().BeFalse();
        broker.Methods.Should().BeEmpty();
    }

    [Theory]
    [InlineData("control")]
    [InlineData("revision")]
    [InlineData("ended")]
    [InlineData("generation")]
    [InlineData("session")]
    [InlineData("missing-task")]
    [InlineData("foreign-task")]
    [InlineData("task-generation")]
    [InlineData("task-source")]
    [InlineData("task-revision")]
    [InlineData("task-terminal")]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("source-swap")]
    public async Task Mcp_exact_session_origin_task_revision_source_and_permission_remain_required_after_review(string fault)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        using var root = f.Root();
        var broker = new McpBroker(source.Account);
        var approvals = new McpApprovals(f);
        await using var use = McpUse(f, source, broker, approvals, await source.Policy());
        await use.PrepareAsync(Token);
        switch (fault)
        {
            case "control": f.CanControl = false; break;
            case "revision": f.ControlRevision++; break;
            case "ended": f.Session = new(f.Session.SessionId, f.Session.Generation, false); break;
            case "generation": f.Session = new(f.Session.SessionId, new(2), true); break;
            case "session": f.Session = new(new(Guid.NewGuid()), new(1), true); break;
            case "missing-task": f.Task = null; break;
            case "foreign-task": f.Task = f.Task! with { Task = new(HostRequest.Create(RequestOrigin.LocalUi), new(1), HostTaskState.IntentRecorded) }; break;
            case "task-generation": f.Task = f.Task! with { Generation = new(2) }; break;
            case "task-source": f.Task = f.Task! with { CurrentSource = false }; break;
            case "task-revision": f.Task = f.Task! with { Task = new(f.Request, new(2), HostTaskState.IntentRecorded) }; break;
            case "task-terminal": f.Task = f.Task! with { Task = new(f.Request, new(1), HostTaskState.Succeeded) }; break;
            case "expired": f.Time.Now = f.Time.Now.AddMinutes(3); break;
            case "revoked": approvals.Revoke(); break;
            case "source-swap": File.AppendAllText(source.File, " "); break;
        }
        await use.Invoking(value => value.ValidateAsync(Token)).Should().ThrowAsync<InvalidOperationException>();
        use.Completed.Should().BeFalse();
        broker.Methods.Should().NotContain("tools/call");
    }

    [Fact]
    public async Task Mcp_unprepared_disposed_cross_session_hosted_and_replayed_requests_never_gain_another_call()
    {
        using var f = new Fixture();
        using var source = new McpSource();
        using var root = f.Root();
        var broker = new McpBroker(source.Account);
        var approvals = new McpApprovals(f);
        await using var use = McpUse(f, source, broker, approvals, await source.Policy());
        FluentActions.Invoking(() => { _ = use.ModelTool; }).Should().Throw<InvalidOperationException>();
        await use.PrepareAsync(Token);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), f.Request, ModelProviderSelection.OllamaCandidate);
        (await use.InvokeAsync(provenance with { Request = HostRequest.Create(RequestOrigin.LocalUi) }, "mcp.search", "{}", Token))
            .Outcome.Should().Be(CapabilityOutcome.Denied);
        (await use.InvokeAsync(provenance with { Selection = ModelProviderSelection.CopilotCandidate }, "mcp.search", "{}", Token))
            .Outcome.Should().Be(CapabilityOutcome.Denied);
        (await use.InvokeAsync(provenance, "mcp.other", "{}", Token)).Outcome.Should().Be(CapabilityOutcome.Denied);
        (await use.InvokeAsync(provenance, "mcp.search", """{"query":"reference"}""", Token)).Outcome.Should().Be(CapabilityOutcome.Succeeded);
        (await use.InvokeAsync(provenance, "mcp.search", """{"query":"reference"}""", Token)).Outcome.Should().Be(CapabilityOutcome.Denied);
        broker.Methods.Count(method => string.Equals(method, "tools/call", StringComparison.Ordinal)).Should().Be(1);
        await use.DisposeAsync();
        await use.DisposeAsync();
        await use.Invoking(value => value.ValidateAsync(Token)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("server-error")]
    [InlineData("result-overhead")]
    public async Task Mcp_errors_and_complete_result_overhead_never_become_success(string fault)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        using var root = f.Root();
        var broker = new McpBroker(source.Account) { Fault = fault };
        await using var use = McpUse(f, source, broker, new(f), await source.Policy());
        await use.PrepareAsync(Token);
        var reply = await use.InvokeAsync(new(new(Guid.NewGuid()), f.Request, ModelProviderSelection.OllamaCandidate),
            "mcp.search", """{"query":"reference"}""", Token);
        reply.Outcome.Should().Be(CapabilityOutcome.Failed);
        use.Completed.Should().BeFalse();
        use.Result.Should().BeNull();
    }

    [Fact]
    public async Task Mcp_review_binding_cannot_substitute_any_destination_account_schema_effect_or_argument()
    {
        using var f = new Fixture();
        using var source = new McpSource();
        using var root = f.Root();
        var approvals = new McpApprovals(f);
        await using var use = McpUse(f, source, new(source.Account), approvals, await source.Policy());
        await use.PrepareAsync(Token);
        var review = approvals.LastReview!;
        var proposal = approvals.LastProposal!;
        review.ToString().Should().Be("McpReview");
        foreach (var altered in new[]
        {
            review with { Stage = (McpReviewStage)42 },
            review with { Endpoint = new("https://other.example.com/mcp") },
            review with { Account = new(Guid.NewGuid(), new(1), Guid.NewGuid(), source.Account.Destination) },
            review with { InputSchema = "{}" },
            review with { ToolName = "other" },
            review with { Arguments = "{}" },
        }) { altered.IsBoundTo(proposal).Should().BeFalse(); }
        foreach (var (action, partition, effect) in new[]
        {
            ("other.action", "mcp", HostOperationEffect.BoundedRead),
            ("mcp.read-only", "other", HostOperationEffect.BoundedRead),
            ("mcp.read-only", "mcp", HostOperationEffect.Prohibited),
        })
        {
            var b = proposal.Binding;
            var changed = new ExactOperationBinding(action, partition, b.SkillId, b.DefinitionDigest,
                b.DeclaredResourceDigest, b.TrackedContentDigest, b.ImplementationDigest, b.InvocationDigest,
                b.ResourceDigest, b.IdentityDigest, b.DestinationDigest, b.TransformationDigest, b.PolicyRevision);
            review.IsBoundTo(new(proposal.Request, proposal.ProposalId, proposal.Revision, changed, effect, proposal.ExpiresAt))
                .Should().BeFalse();
        }
    }

    [Fact]
    public async Task Mcp_storage_fault_is_truthful_unknown_and_never_connects()
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var broker = new McpBroker(source.Account);
        var approvals = new McpApprovals(f) { Fault = "io" };
        var service = McpService(f, f.Host, source, broker, approvals, await source.Policy());
        var receipt = await service.SubmitMcpAsync(new(f.Session, null), "Find reference", source.ServerId, source.Account,
            approvals.Present, McpSelect, static () => true, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        broker.Connected.Should().BeFalse();
    }

    [Theory]
    [InlineData("configuration")]
    [InlineData("broker")]
    [InlineData("effects")]
    [InlineData("interactions")]
    [InlineData("host")]
    [InlineData("execution")]
    public async Task Mcp_missing_consumed_contract_never_retains_or_falls_back(string missing)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var approvals = new McpApprovals(f);
        var broker = new McpBroker(source.Account);
        var policy = await source.Policy();
        var workspace = new Kora.Application.Hosting.SessionWorkspaceService(f, new(f), f,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Kora.Application.Hosting.SessionWorkspaceService>.Instance);
        var service = new Kora.Application.Hosting.SessionConversationService(f, f, workspace, f,
            missing is "host" ? new TestAdmission(ModelTurnOutcome.Unavailable) : f.Host, f.Time,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Kora.Application.Hosting.SessionConversationService>.Instance,
            execution: missing is "execution" ? null : f.Host, cancellationAudit: f.CancelAudit,
            interactions: missing is "interactions" ? null : approvals,
            mcpConfiguration: missing is "configuration" ? null : source.Configuration,
            mcpBroker: missing is "broker" ? null : broker, mcpEffects: missing is "effects" ? null : policy);
        await service.Invoking(value => value.SubmitMcpAsync(new(f.Session, null), "Find reference",
            source.ServerId, source.Account, approvals.Present, McpSelect, static () => true, Token))
            .Should().ThrowAsync<McpUnavailableException>();
        f.RetainCalls.Should().Be(0);
        broker.Connected.Should().BeFalse();
    }

    [Fact]
    public async Task Mcp_account_options_and_unqualified_connector_remain_explicit_without_a_connection()
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var service = McpService(f, f.Host, source, new McpBroker(source.Account), new(f), await source.Policy());
        service.ReadMcpServers().Should().ContainSingle();
        (await service.ReadMcpAccountsAsync(Token)).Should().ContainSingle().Which.Reference.Should().Be(source.Account);
        var missing = f.ConversationService(f.Host);
        FluentActions.Invoking(() => missing.ReadMcpServers()).Should().Throw<McpUnavailableException>();
        await FluentActions.Awaiting(() => missing.ReadMcpAccountsAsync(Token)).Should().ThrowAsync<McpUnavailableException>();
        var unavailable = new UnavailableMcpConnectionBroker();
        await FluentActions.Awaiting(() => unavailable.ConnectAsync(source.Account, Token)).Should().ThrowAsync<McpUnavailableException>();
        await FluentActions.Awaiting(() => unavailable.ConnectAsync(source.Account, new CancellationToken(true))).Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => unavailable.ReadAccountsAsync(new CancellationToken(true))).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Mcp_source_closure_before_retention_keeps_no_owned_transport()
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var broker = new McpBroker(source.Account);
        var approvals = new McpApprovals(f);
        var service = McpService(f, f.Host, source, broker, approvals, await source.Policy());
        await service.Invoking(value => value.SubmitMcpAsync(new(f.Session, null), "Find reference", source.ServerId,
            source.Account, approvals.Present, McpSelect, static () => false, Token)).Should().ThrowAsync<InvalidOperationException>();
        broker.Connected.Should().BeFalse();
        f.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("selection-cancel")]
    [InlineData("selection-fault")]
    [InlineData("review-cancel")]
    [InlineData("late-read")]
    [InlineData("late-validation")]
    public async Task Mcp_cancellation_and_admission_changes_at_await_boundaries_do_not_release_or_publish_success(string fault)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        using var root = f.Root();
        var approvals = new McpApprovals(f);
        var broker = new McpBroker(source.Account);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        McpApprovalPresenter review = async (proposal, generation, value, live, token) =>
        {
            var answer = await approvals.Present(proposal, generation, value, live, token);
            if (fault is "review-cancel") { cancellation.Cancel(); }
            return answer;
        };
        McpToolSelector selector = fault switch
        {
            "selection-cancel" => (_, _, _, _, _, _) => throw new OperationCanceledException(cancellation.Token),
            "selection-fault" => (_, _, _, _, _, _) => throw new IOException("synthetic native selection failure"),
            _ => McpSelect,
        };
        await using var use = new McpInvocation(source.Configuration, broker, await source.Policy(), approvals, f, f, f.CancelAudit,
            f.Time, f.Request, f.Session.Generation, source.ServerId, source.Account, review, selector, static () => true);
        if (fault is "selection-cancel" or "review-cancel")
        {
            await use.Invoking(value => value.PrepareAsync(cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
        }
        else if (fault is "selection-fault")
        { await use.Invoking(value => value.PrepareAsync(Token)).Should().ThrowAsync<IOException>(); }
        else
        {
            await use.PrepareAsync(Token);
            var calls = 0;
            if (fault is "late-read") { f.OnRead = () => f.CanControl = false; }
            else
            {
                approvals.BeforeTransaction = () =>
                {
                    calls++;
                    // Retire after all native grant transactions have observed their current snapshots.
                    if (calls == approvals.Snapshots.Values.Sum(snapshot => snapshot.Grants.Length)) { f.CanControl = false; }
                };
            }
            await use.Invoking(value => value.ValidateAsync(Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        broker.Methods.Should().NotContain("tools/call");
    }

    [Fact]
    public async Task Mcp_host_binding_checks_original_host_mode_locality_descriptor_and_single_use()
    {
        using var f = new Fixture();
        using var source = new McpSource();
        using var root = f.Root();
        await using var use = McpUse(f, source, new(source.Account), new(f), await source.Policy());
        await use.PrepareAsync(Token);
        var context = new ModelContextEnvelope(f.Request, "policy", "task", [], f.Time.Now, f.Time.Now.AddSeconds(15),
            ModelToolMode.McpReadOnly, mcpTool: use.ModelTool);
        var turn = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, context, Token)).Turn!;
        FluentActions.Invoking(() => f.PolicyHost.BindMcp(turn, use)).Should().Throw<InvalidOperationException>();
        var ordinary = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, f.Context(), Token)).Turn!;
        FluentActions.Invoking(() => f.Host.BindMcp(ordinary, use)).Should().Throw<InvalidOperationException>();
        var hosted = new ModelTurn(turn.Owner, turn.Issuer, turn.Provenance with { Selection = ModelProviderSelection.CopilotCandidate },
            turn.Context, turn.Wire, turn.SessionGeneration, turn.TaskRevision, turn.ControlRevision, turn.Registration);
        FluentActions.Invoking(() => f.Host.BindMcp(hosted, use)).Should().Throw<InvalidOperationException>();
        foreach (var tool in new[]
        {
            new McpModelTool("mcp.other", McpSchema),
            new McpModelTool("mcp.search", McpSchema + " "),
        })
        {
            var mismatchContext = new ModelContextEnvelope(f.Request, "policy", "task", [], f.Time.Now, f.Time.Now.AddSeconds(15),
                ModelToolMode.McpReadOnly, mcpTool: tool);
            var mismatch = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, mismatchContext, Token)).Turn!;
            FluentActions.Invoking(() => f.Host.BindMcp(mismatch, use)).Should().Throw<InvalidOperationException>();
        }
        f.Host.BindMcp(turn, use);
        FluentActions.Invoking(() => f.Host.BindMcp(turn, use)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => use.BindTurn(turn)).Should().Throw<InvalidOperationException>();
        await using var other = McpUse(f, source, new(source.Account), new(f), await source.Policy());
        var foreign = new ModelTurn(turn.Owner, turn.Issuer, turn.Provenance with { Request = HostRequest.Create(RequestOrigin.LocalUi) },
            turn.Context, turn.Wire, turn.SessionGeneration, turn.TaskRevision, turn.ControlRevision, turn.Registration);
        FluentActions.Invoking(() => other.BindTurn(foreign)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Mcp_unbound_canonical_host_callback_fails_closed_before_external_dispatch()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var model = new McpModelTransport();
        await using var loop = new BoundedModelTurnAdapter(model, f.Time, f);
        var host = f.LoopHost(loop);
        var context = new ModelContextEnvelope(f.Request, "policy", "task", [], f.Time.Now, f.Time.Now.AddSeconds(15),
            ModelToolMode.McpReadOnly, mcpTool: new("mcp.search", McpSchema));
        var turn = (await host.AdmitAsync(ModelProviderSelection.OllamaCandidate, context, Token)).Turn!;
        (await host.RunAsync(turn, Token)).Outcome.Should().Be(ModelTurnOutcome.Denied);
        model.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Mcp_source_validation_cannot_hide_a_control_change_before_any_grant_exists()
    {
        using var f = new Fixture();
        using var source = new McpSource();
        using var root = f.Root();
        var policy = await source.Policy();
        var broker = new McpBroker(source.Account);
        var loads = 0;
        source.BeforeLoad = () => { if (++loads == 8) { f.CanControl = false; } };
        await using var use = McpUse(f, source, broker, new(f), policy);
        await use.Invoking(value => value.PrepareAsync(Token)).Should().ThrowAsync<InvalidOperationException>();
        broker.Connected.Should().BeFalse();
        loads.Should().Be(8);
    }

    [Fact]
    public async Task Mcp_actual_result_can_complete_without_an_optional_volatile_presenter()
    {
        using var f = new Fixture();
        using var source = new McpSource();
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        await using var loop = new BoundedModelTurnAdapter(new McpModelTransport(), f.Time, f);
        var approval = new McpApprovals(f);
        var service = McpService(f, f.ConfiguredProfiles(adapter: loop), source, new McpBroker(source.Account), approval, await source.Policy());
        var receipt = await service.SubmitMcpAsync(new(f.Session, null), "Find reference", source.ServerId,
            source.Account, approval.Present, McpSelect, static () => true, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse.Should().NotBeNull();
    }

    private static McpInvocation McpUse(Fixture f, McpSource source, McpBroker broker, McpApprovals approvals,
        McpReadOnlyPolicy policy) => new(source.Configuration, broker, policy, approvals, f, f, f.CancelAudit,
            f.Time, f.Request, f.Session.Generation, source.ServerId, source.Account, approvals.Present, McpSelect, static () => true);
}
