using AwesomeAssertions;
using Kora.Application.UnitTests.Agents;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class RuntimeResourceAllocatorTests
{
    [Fact]
    public async Task ConfiguredOneEnforcesFairIndependentSessionAdmissionAndCancellationJoinsBeforeReusingLease()
    {
        await using var settings = new ConversationExecutionConfigurationServiceTests.Fixture();
        var source = new RuntimeSource();
        await using var f = new AgentRunSetTests.Fixture(runtimeCapacity: source, registrationSource: source,
            conversationExecution: settings.Service);
        using var cleanup = source;
        await settings.Set(1);
        var one = f.Conversation.SubmitAsync(f.Sessions[0], "one private request", () => true, Token);
        await source.Started(0, f);
        var two = f.Conversation.SubmitAsync(f.Sessions[1], "two private request", () => true, Token);
        var three = f.Conversation.SubmitAsync(f.Sessions[2], "three private request", () => true, Token);
        var waiting = (await f.Conversation.ReadRunningConversationAsync(f.Sessions[1], () => true, Token))!;
        waiting.Resources.Should().Be(new ConversationResourceStatus(1, 1, 2, 1, 1, ConversationResourceWaitReason.ConfiguredMaximum));
        waiting.Resources!.EffectiveCeiling.Should().Be(1);
        source.Transport(1, f).Started.Task.IsCompleted.Should().BeFalse();
        source.Transport(2, f).Started.Task.IsCompleted.Should().BeFalse();
        var running = (await f.Conversation.ReadRunningConversationAsync(f.Sessions[0], () => true, Token))!;
        await f.Conversation.CancelRunningConversationAsync(running.Target, () => true, Token);
        await source.Transport(0, f).Cancelled.Task;
        one.IsCompleted.Should().BeFalse();
        source.Transport(1, f).Started.Task.IsCompleted.Should().BeFalse("cancellation-ignoring provider still owns its lease");
        source.Transport(0, f).Finish("forbidden late answer");
        (await one).Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        await source.Started(1, f);
        source.Transport(2, f).Started.Task.IsCompleted.Should().BeFalse("global resource FIFO is independent of selection");
        source.Transport(1, f).Finish("only two");
        (await two).Message.AssistantResponse!.Body.Should().Be("only two");
        await source.Started(2, f);
        source.Transport(2, f).Finish("only three");
        (await three).Message.AssistantResponse!.Body.Should().Be("only three");
        f.Host.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task LowerMaximumNeverCancelsTwoActiveSessionsAndRevalidatesWaitingWorkBeforeDispatch()
    {
        await using var settings = new ConversationExecutionConfigurationServiceTests.Fixture();
        settings.Service.Observe();
        var source = new RuntimeSource();
        await using var f = new AgentRunSetTests.Fixture(runtimeCapacity: source, registrationSource: source,
            conversationExecution: settings.Service);
        using var cleanup = source;
        var one = f.Conversation.SubmitAsync(f.Sessions[0], "one", () => true, Token);
        await source.Started(0, f);
        var two = f.Conversation.SubmitAsync(f.Sessions[1], "two", () => true, Token);
        await source.Started(1, f);
        var three = f.Conversation.SubmitAsync(f.Sessions[2], "three", () => true, Token);
        var lowered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oneLease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Conversation.RunningConversationChanged += status =>
        {
            if (status.Target.Request.SessionId != f.Sessions[2].Authority.SessionId
                || status.Resources is not { ConfiguredMaximum: 1 } resources) { return; }
            lowered.TrySetResult();
            if (resources.OwnedLeases == 1) { oneLease.TrySetResult(); }
        };
        await settings.Set(1);
        await lowered.Task.WaitAsync(Token);
        source.Transport(0, f).Cancelled.Task.IsCompleted.Should().BeFalse();
        source.Transport(1, f).Cancelled.Task.IsCompleted.Should().BeFalse();
        source.Transport(0, f).Finish("one");
        (await one).Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        await oneLease.Task.WaitAsync(Token);
        source.Transport(2, f).Started.Task.IsCompleted.Should().BeFalse();
        source.Transport(1, f).Finish("two");
        (await two).Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        await source.Started(2, f);
        source.Transport(2, f).Finish("three");
        (await three).Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
    }

    [Fact]
    public async Task RaisingMaximumDoesNotExpandCapturedBudgetAndNewSubmissionsDoNotJumpEarlierWaiter()
    {
        await using var settings = new ConversationExecutionConfigurationServiceTests.Fixture();
        var source = new RuntimeSource();
        await using var f = new AgentRunSetTests.Fixture(runtimeCapacity: source, registrationSource: source,
            conversationExecution: settings.Service);
        using var cleanup = source;
        await settings.Set(1);
        var one = f.Conversation.SubmitAsync(f.Sessions[0], "one", () => true, Token);
        await source.Started(0, f);
        var two = f.Conversation.SubmitAsync(f.Sessions[1], "two", () => true, Token);
        var rechecked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Conversation.RunningConversationChanged += status =>
        {
            if (status.Resources is { ConfiguredMaximum: 2, CapturedMaximum: 1 })
            { rechecked.TrySetResult(); }
        };
        await settings.Set(2);
        await rechecked.Task.WaitAsync(Token);
        source.Transport(1, f).Started.Task.IsCompleted.Should().BeFalse();
        var three = f.Conversation.SubmitAsync(f.Sessions[2], "three", () => true, Token);
        var waiting = (await f.Conversation.ReadRunningConversationAsync(f.Sessions[2], () => true, Token))!;
        waiting.Resources!.WaitReason.Should().Be(ConversationResourceWaitReason.EarlierRequest);
        source.Transport(0, f).Finish("one");
        await one;
        await source.Started(1, f);
        // Admission of the head must wake the next FIFO waiter even before its provider completes.
        await source.Started(2, f);
        source.Transport(1, f).Finish("two");
        source.Transport(2, f).Finish("three");
        await Task.WhenAll(two, three);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownSettingsOrAuthorityChangeDuringAcquireCannotIssueANewLease(bool revokeDuringAudit)
    {
        await using var settings = new ConversationExecutionConfigurationServiceTests.Fixture();
        if (revokeDuringAudit) { settings.Service.Observe(); }
        var source = new RuntimeSource();
        await using var f = new AgentRunSetTests.Fixture(runtimeCapacity: source, registrationSource: source,
            conversationExecution: settings.Service);
        using var cleanup = source;
        f.OnAudit = entry =>
        {
            if (string.Equals(entry.ActionId, "runtime.resource.acquire", StringComparison.Ordinal) && entry.Outcome == SecurityAuditOutcome.Requested)
            { settings.Service.HoldUnavailable(); }
        };
        var result = await f.Conversation.SubmitAsync(f.Sessions[0], "one", () => true, Token);
        result.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        result.Message.Execution.Should().BeNull();
        source.Transport(0, f).Started.Task.IsCompleted.Should().BeFalse();
        f.Audits.Should().NotContain(item => string.Equals(item.ActionId, "runtime.resource.acquire", StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task UnknownPreferenceRetiresWaitingAdmissionButNeverReleasesOrCancelsActiveProviders()
    {
        await using var settings = new ConversationExecutionConfigurationServiceTests.Fixture();
        settings.Service.Observe();
        var source = new RuntimeSource();
        await using var f = new AgentRunSetTests.Fixture(runtimeCapacity: source, registrationSource: source,
            conversationExecution: settings.Service);
        using var cleanup = source;
        var one = f.Conversation.SubmitAsync(f.Sessions[0], "one", () => true, Token);
        await source.Started(0, f);
        var two = f.Conversation.SubmitAsync(f.Sessions[1], "two", () => true, Token);
        await source.Started(1, f);
        var three = f.Conversation.SubmitAsync(f.Sessions[2], "three", () => true, Token);
        settings.Service.HoldUnavailable();
        (await three).Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        source.Transport(2, f).Started.Task.IsCompleted.Should().BeFalse();
        source.Transport(0, f).Cancelled.Task.IsCompleted.Should().BeFalse();
        source.Transport(1, f).Cancelled.Task.IsCompleted.Should().BeFalse();
        source.Transport(0, f).Finish("one");
        source.Transport(1, f).Finish("two");
        (await one).Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        (await two).Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        f.Host.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task UnconfirmedSettingsAtDurableDispatchFenceCannotRunProviderAndReservedLeaseStillJoins()
    {
        await using var settings = new ConversationExecutionConfigurationServiceTests.Fixture();
        settings.Service.Observe();
        var source = new RuntimeSource();
        await using var f = new AgentRunSetTests.Fixture(runtimeCapacity: source, registrationSource: source,
            conversationExecution: settings.Service);
        using var cleanup = source;
        f.BeforeDispatch = settings.Service.HoldUnavailable;
        await f.Conversation.Invoking(service => service.SubmitAsync(f.Sessions[0], "one", () => true, Token))
            .Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        source.Transport(0, f).Started.Task.IsCompleted.Should().BeFalse();
        f.Audits.Should().Contain(entry => string.Equals(entry.ActionId, "runtime.resource.release", StringComparison.Ordinal)
            && entry.Outcome == SecurityAuditOutcome.Succeeded);
        f.Messages.Values.Should().OnlyContain(message => message.Execution == null && message.AssistantResponse == null);
        f.Host.IsQuiescent.Should().BeTrue();
    }
}
