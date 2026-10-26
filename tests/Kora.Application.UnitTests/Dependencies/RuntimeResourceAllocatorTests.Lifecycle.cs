using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.UnitTests.Agents;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class RuntimeResourceAllocatorTests
{
    [Fact]
    public async Task ExactAccountProfileReplacementCannotShareTheSamePhysicalRuntimeWithItsPredecessor()
    {
        var source = new RuntimeSource();
        await using var f = new AgentRunSetTests.Fixture(runtimeCapacity: source, registrationSource: source);
        using var cleanup = source;
        var one = f.Conversation.SubmitAsync(f.Sessions[0], "one", () => true, Token);
        await source.Started(0, f);
        var replacement = Profile with { Account = new("other-account") };
        f.RuntimeCatalogue.Account = replacement.Account.Value;
        f.RuntimeProfiles.Value = new(ProviderService.Local, [replacement]);
        source.Evidence = source.Evidence with { Profile = replacement };
        var two = f.Conversation.SubmitAsync(f.Sessions[1], "two", () => true, Token);
        var waiting = (await f.Conversation.ReadRunningConversationAsync(f.Sessions[1], () => true, Token))!;
        waiting.Phase.Should().Be(ConversationExecutionPhase.Waiting);
        source.Transport(1, f).Started.Task.IsCompleted.Should().BeFalse();
        source.Transport(0, f).Finish("stale account response");
        (await one).Message.AssistantResponse.Should().BeNull();
        await source.Started(1, f);
        source.Transport(1, f).Finish("current exact account");
        (await two).Message.AssistantResponse!.Body.Should().Be("current exact account");
    }

    [Fact]
    public async Task HostShutdownWithdrawsWaitingAdmissionAndJoinsEachDispatchedResourceBeforeClosing()
    {
        var source = new RuntimeSource();
        await using var f = new AgentRunSetTests.Fixture(runtimeCapacity: source, registrationSource: source);
        using var cleanup = source;
        var one = f.Conversation.SubmitAsync(f.Sessions[0], "one", () => true, Token);
        await source.Started(0, f);
        var two = f.Conversation.SubmitAsync(f.Sessions[1], "two", () => true, Token);
        await source.Started(1, f);
        var three = f.Conversation.SubmitAsync(f.Sessions[2], "three", () => true, Token);
        var closing = f.Host.DisposeAsync().AsTask();
        var waiting = await three;
        waiting.Message.Outcome.Should().Be(Kora.Core.Dependencies.ModelTurnOutcome.Unavailable);
        waiting.Message.Reason.Should().Be(Kora.Core.Dependencies.ModelTurnReason.HostAdmissionClosed);
        closing.IsCompleted.Should().BeFalse();
        source.Transport(0, f).Finish("never retained one");
        (await one).Message.AssistantResponse.Should().BeNull();
        closing.IsCompleted.Should().BeFalse();
        source.Transport(1, f).Finish("never retained two");
        (await two).Message.AssistantResponse.Should().BeNull();
        await closing;
    }

    [Fact]
    public async Task CapacityRevocationDuringRequiredTurnTerminalAuditStillFencesAssistantCommit()
    {
        var source = new RuntimeSource();
        await using var f = new AgentRunSetTests.Fixture(runtimeCapacity: source, registrationSource: source);
        using var cleanup = source;
        var one = f.Conversation.SubmitAsync(f.Sessions[0], "one", () => true, Token);
        await source.Started(0, f);
        f.OnAudit = entry =>
        {
            if (string.Equals(entry.ActionId, "model.turn", StringComparison.Ordinal)
                && entry.Outcome == SecurityAuditOutcome.Succeeded) { source.Current = false; }
        };
        source.Transport(0, f).Finish("must not retain");
        await one.Invoking(value => value).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        f.Messages.Values.Should().OnlyContain(item => item.AssistantResponse == null);
        f.Host.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task LegacyDispatchRacingRuntimeObservationIsRefusedBeforeOrdinaryExecutionIntent()
    {
        var source = new RuntimeSource();
        await using var f = new AgentRunSetTests.Fixture(runtimeCapacity: source, registrationSource: source);
        using var cleanup = source;
        var legacyRequest = new HostRequest(new(Guid.NewGuid()), f.Sessions[0].Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        using var legacyIssuer = HostActivity.BeginRoot(legacyRequest, HostActivityLayer.Application, HostOperation.Request);
        f.Tasks[legacyRequest.TaskId] = new(new(legacyRequest, new(1), HostTaskState.IntentRecorded), new(1), "host", true, null);
        var legacyContext = new ModelContextEnvelope(legacyRequest, "policy", "legacy", [], f.Time.Now, f.Time.Now.AddMinutes(1));
        var legacy = (await f.Host.AdmitAsync(ModelTurnChoice.Default, legacyContext, null, Token)).Turn!;
        var request = new HostRequest(new(Guid.NewGuid()), f.Sessions[1].Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        using var issuer = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        f.Tasks[request.TaskId] = new(new(request, new(1), HostTaskState.IntentRecorded), new(1), "host", true, null);
        var context = new ModelContextEnvelope(request, "policy", "ordinary", [], f.Time.Now, f.Time.Now.AddMinutes(1));
        var ordinary = (await f.Host.AdmitAsync(ModelTurnChoice.Default, context, null, Token)).Turn!;
        async Task<ModelTurnResult> RunLegacy()
        {
            using var root = HostActivity.BeginRoot(legacyRequest, HostActivityLayer.Application, HostOperation.Request);
            return await f.Host.RunAsync(legacy, Token);
        }
        Task<ModelTurnResult>? legacyWork = null;
        source.OnObserve = () =>
        {
            source.OnObserve = null;
            legacyWork = RunLegacy();
        };
        (await f.Host.ReserveConversationResourcesAsync(ordinary, () => true, Token)).Should().Be(ModelTurnReason.ProviderBusy);
        await source.Started(0, f);
        source.Transport(1, f).Started.Task.IsCompleted.Should().BeFalse();
        await f.Host.JoinConversationResourcesAsync(ordinary, Token);
        source.Transport(0, f).Finish("only legacy ran");
        (await legacyWork!).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        f.Messages.Should().BeEmpty();
    }
}