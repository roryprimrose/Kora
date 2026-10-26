using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Fact]
    public async Task ResourceOperationsRejectForeignUnleasedReplayedAndReleasedTurnCapabilities()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var turn = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, f.Context(), Token)).Turn!;
        var other = f.Qualified(ModelProviderSelection.OllamaCandidate);
        (await other.ReserveConversationResourcesAsync(turn, () => true, Token)).Should().Be(ModelTurnReason.HostAdmissionClosed);
        other.IsTurnQuiescent(turn).Should().BeFalse();
        other.ConversationResourcesCurrent(turn).Should().BeFalse();
        await other.Invoking(value => value.JoinConversationResourcesAsync(turn, Token)).Should().ThrowAsync<InvalidOperationException>();
        await f.Host.Invoking(value => value.JoinConversationResourcesAsync(turn, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Host.IsTurnQuiescent(turn).Should().BeFalse();
        f.Host.ConversationResourcesCurrent(turn).Should().BeFalse();
        (await f.Host.ReserveConversationResourcesAsync(turn, () => false, Token)).Should().Be(ModelTurnReason.HostAdmissionClosed);
        var earlier = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, f.Context(), Token)).Turn!;
        (await ((IModelTurnExecution)f.Host).RunAsync(earlier, () => true, Token)).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        (await f.Host.ReserveConversationResourcesAsync(earlier, () => true, Token)).Should().Be(ModelTurnReason.HostAdmissionClosed);
        (await f.Host.ReserveConversationResourcesAsync(turn, () => true, Token)).Should().Be(ModelTurnReason.None);
        (await f.Host.ReserveConversationResourcesAsync(turn, () => true, Token)).Should().Be(ModelTurnReason.HostAdmissionClosed);
        f.Host.IsTurnQuiescent(turn).Should().BeFalse();
        f.Host.ConversationResourcesCurrent(turn).Should().BeTrue();
        var legacy = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, f.Context(), Token)).Turn!;
        (await f.Host.RunAsync(legacy, Token)).Reason.Should().Be(ModelTurnReason.ProviderBusy);
        f.Current = false;
        f.Host.ConversationResourcesCurrent(turn).Should().BeFalse();
        f.Current = true;
        var global = f.Host.WaitForQuiescenceAsync(Token);
        global.IsCompleted.Should().BeFalse();
        await f.Host.JoinConversationResourcesAsync(turn, Token);
        await global;
        f.Host.IsTurnQuiescent(turn).Should().BeTrue();
        f.Host.ConversationResourcesCurrent(turn).Should().BeFalse();
        (await f.Host.RunAsync(turn, Token)).Reason.Should().Be(ModelTurnReason.ProviderBusy);
        f.Adapter.Calls.Should().Be(1, "a joined resource capability cannot dispatch a later transport");
    }

    [Fact]
    public async Task ResourceReservationRevalidatesAuthorityAndAtomicSameSessionOwnership()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var occupied = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, f.Context(), Token)).Turn!;
        var competing = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, f.Context(), Token)).Turn!;
        var closed = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, f.Context(), Token)).Turn!;
        f.Current = false;
        (await f.Host.ReserveConversationResourcesAsync(closed, () => true, Token)).Should().Be(ModelTurnReason.HostAdmissionClosed);
        f.Current = true;
        Task<ModelTurnReason>? occupant = null;
        f.OnRead = () =>
        {
            f.OnRead = null;
            occupant = f.Host.ReserveConversationResourcesAsync(occupied, () => true, Token);
        };
        (await f.Host.ReserveConversationResourcesAsync(competing, () => true, Token)).Should().Be(ModelTurnReason.ProviderBusy);
        (await occupant!).Should().Be(ModelTurnReason.None);
        await f.Host.JoinConversationResourcesAsync(occupied, Token);
        f.Host.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task ReentrantClosureDuringFinalResourceIssuanceIsRefusedBeforeDurableDispatch()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var turn = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, f.Context(), Token)).Turn!;
        var closeAfterAudit = false;
        Task? closing = null;
        f.OnAudit = entry =>
        {
            if (string.Equals(entry.ActionId, "runtime.resource.acquire", StringComparison.Ordinal)
                && entry.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Succeeded) { closeAfterAudit = true; }
        };
        bool Current()
        {
            if (closeAfterAudit)
            {
                closeAfterAudit = false;
                closing = f.Host.DisposeAsync().AsTask();
            }
            return true;
        }
        (await f.Host.ReserveConversationResourcesAsync(turn, Current, Token)).Should().Be(ModelTurnReason.HostAdmissionClosed);
        await f.Host.JoinConversationResourcesAsync(turn, Token);
        await closing!;
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task SourceRevocationAfterResourceAuditAndBeforeExecutionIntentNeverInvokesAdapter()
    {
        using var f = new Fixture();
        var host = QualifiedHistoryHost(f);
        var service = f.ConversationService(host, execution: host);
        var acquired = false;
        var live = true;
        f.OnAudit = entry =>
        {
            if (string.Equals(entry.ActionId, "runtime.resource.acquire", StringComparison.Ordinal)
                && entry.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Succeeded) { acquired = true; }
        };
        Task Validate(CancellationToken _)
        {
            if (acquired) { live = false; }
            return Task.CompletedTask;
        }
        await service.Invoking(value => value.SubmitAsync(new(f.Session, null), "original request",
            () => live, Token, beforeRetain: Validate)).Should().ThrowAsync<Kora.Core.Conversation.ConversationSubmissionUnconfirmedException>();
        f.ExecutionStarts.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task ReviewedToolRequestConsumesTerminalHookWhenResourceAdmissionIsUnavailableWithoutFetchingPage(RequestOrigin origin)
    {
        using var f = new Fixture(origin);
        await using var skill = new SharedSkillHarness(f);
        await skill.Register();
        DeclareWebTool(skill);
        var adapter = new Kora.Application.UnitTests.Agents.AgentRunSetTests.BarrierAdapter { Quiescent = false };
        var network = new PageNetwork();
        var workflow = skill.ToolWorkflow(adapter: adapter, network: network);
        using var review = await workflow.ReviewAsync(skill.Reader.Source, skill.Reader.Snapshot(),
            new(f.Session, null), skill.Eligible, origin, Token);
        await workflow.EnableAsync(review, skill.Eligible, origin, Token);
        using var invocation = await workflow.PrepareAsync(review, "original reviewed request", ToolDestination,
            skill.Eligible, origin, Token);
        using var root = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        try
        {
            var receipt = await workflow.InvokeAsync(invocation, skill.Eligible, UnexpectedPageApproval, Token);
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
            receipt.Message.Reason.Should().Be(ModelTurnReason.ProviderBusy);
            receipt.Message.Execution.Should().BeNull();
            receipt.Message.AssistantResponse.Should().BeNull();
            receipt.Message.Body.Should().Be("original reviewed request");
            receipt.Message.Request.Origin.Should().Be(origin);
            receipt.Message.Should().Be(f.Retained);
            receipt.Task.IsTerminal.Should().BeTrue();
            receipt.Task.Should().Be(f.Task!.Task);
            f.ExecutionStarts.Should().Be(0);
            f.ExecutionCompletions.Should().Be(0);
            network.Sends.Should().Be(0);
            adapter.Active.Should().Be(0);
            adapter.IsQuiescent.Should().BeFalse();
            review.Text.Should().BeEmpty();
            invocation.Body.Should().BeEmpty();
            skill.Audit.Events.Where(entry => string.Equals(entry.ActionId, "skills.tool-revision.invoke", StringComparison.Ordinal))
                .Select(entry => entry.Outcome).Should().Equal(
                    Kora.Core.Auditing.SecurityAuditOutcome.Requested, Kora.Core.Auditing.SecurityAuditOutcome.Failed);
        }
        finally { adapter.Quiescent = true; }
    }

    [Fact]
    public async Task ReviewedToolRequestRejectsUnconfirmedResourceReleaseBeforeTerminalRetention()
    {
        using var f = new Fixture();
        await using var skill = new SharedSkillHarness(f);
        await skill.Register();
        DeclareWebTool(skill);
        var network = new PageNetwork();
        var workflow = skill.ToolWorkflow(network: network);
        using var review = await workflow.ReviewAsync(skill.Reader.Source, skill.Reader.Snapshot(),
            new(f.Session, null), skill.Eligible, RequestOrigin.LocalUi, Token);
        await workflow.EnableAsync(review, skill.Eligible, RequestOrigin.LocalUi, Token);
        using var invocation = await workflow.PrepareAsync(review, "original reviewed request", ToolDestination,
            skill.Eligible, RequestOrigin.LocalUi, Token);
        f.AfterExecutionStart = () => throw new IOException("Execution intent readback is unavailable.");
        f.OnAudit = entry =>
        {
            if (string.Equals(entry.ActionId, "runtime.resource.release", StringComparison.Ordinal))
            { throw new IOException("Required resource release audit is unavailable."); }
        };
        var failure = await workflow.Invoking(value => value.InvokeAsync(invocation, skill.Eligible, UnexpectedPageApproval, Token))
            .Should().ThrowAsync<Kora.Core.Conversation.ConversationSubmissionUnconfirmedException>();
        failure.Which.InnerException.Should().BeOfType<IOException>()
            .Which.Message.Should().Be("Required resource release audit is unavailable.");
        f.Adapter.Calls.Should().Be(0);
        f.ExecutionStarts.Should().Be(1);
        f.ExecutionCompletions.Should().Be(0);
        f.Retained!.Outcome.Should().BeNull();
        f.Retained.AssistantResponse.Should().BeNull();
        f.Task!.Task.State.Should().Be(HostTaskState.DispatchRecorded);
        network.Sends.Should().Be(0);
        review.Text.Should().BeEmpty();
        invocation.Body.Should().BeEmpty();
        skill.Audit.Events.Where(entry => string.Equals(entry.ActionId, "skills.tool-revision.invoke", StringComparison.Ordinal))
            .Select(entry => entry.Outcome).Should().Equal(
                Kora.Core.Auditing.SecurityAuditOutcome.Requested, Kora.Core.Auditing.SecurityAuditOutcome.Failed);
    }

    private sealed class ReportedIdleAdmission(ModelTurnHost host) : IModelTurnAdmission
    {
        public bool IsQuiescent => true;
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token) => host.AdmitAsync(choice, context, review, token);
    }
}