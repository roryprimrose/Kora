using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Fact]
    public async Task OriginalCallerCancellationFencesAgentToolWhileLinkedCancellationCallbacksAreStillHeld()
    {
        using var f = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<ModelToolTransportReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new ToolTransport { Pending = pending.Task, AfterRequest = _ => entered.TrySetResult() };
        var observed = 0;
        f.OnVersion = () => observed++;
        await using var loop = CreateLoop(f, transport);
        var host = ConfigureAgentLoop(f, loop);
        await using var workflow = new BoundedAgentWorkflow(new AgentReader(), f, f, f.ConversationService(host, execution: host),
            host, f, f.Time);
        using var root = f.Root();
        using var caller = new CancellationTokenSource();
        using var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null), "original task",
            Kora.Core.Hosting.RequestOrigin.LocalUi, () => true, Token);
        var running = workflow.InvokeAsync(review, caller.Token);
        await entered.Task.WaitAsync(Token);
        var cancellationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCancellation = new ManualResetEventSlim();
        using var registration = caller.Token.Register(() =>
        {
            cancellationEntered.TrySetResult();
            releaseCancellation.Wait(Token);
        });
        var cancellation = caller.CancelAsync();
        try
        {
            await cancellationEntered.Task.WaitAsync(Token);
            caller.IsCancellationRequested.Should().BeTrue();
            cancellation.IsCompleted.Should().BeFalse();
            pending.SetResult(ToolTransport.Reply(transport.Requests[0], VersionCall));
            await running.Invoking(value => value).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
            observed.Should().Be(0);
            transport.Requests.Should().ContainSingle();
            f.Retained!.AssistantResponse.Should().BeNull();
            host.IsQuiescent.Should().BeTrue();
        }
        finally
        {
            releaseCancellation.Set();
            pending.TrySetResult(ToolTransport.Reply(transport.Requests[0], VersionCall));
            await cancellation;
            try { await running; }
            catch (ConversationSubmissionUnconfirmedException) { }
        }
    }

    [Fact]
    public async Task CallerCancellationInvalidatesObservedStepBeforeProviderDispatchWithoutChangingItsOwner()
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        var service = f.ConversationService(host, execution: host);
        using var caller = new CancellationTokenSource();
        ConversationExecutionStatus? observed = null;
        service.RunningConversationChanged += status =>
        {
            if (status.Phase != ConversationExecutionPhase.Running) { return; }
            observed = status;
            service.IsExecutionProgressCurrent(status).Should().BeTrue();
            caller.Cancel();
            service.IsExecutionProgressCurrent(status).Should().BeFalse();
        };
        await service.Invoking(value => value.SubmitAsync(new(f.Session, null), "original cancellation",
            static () => true, caller.Token)).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        observed.Should().NotBeNull();
        f.Adapter.Calls.Should().Be(0);
        f.Retained!.AssistantResponse.Should().BeNull();
        host.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task CancellingObservedBoundedAgentDoesNotSignalOrRetargetAnIndependentLiveAgent()
    {
        using var first = new Fixture();
        using var other = new Fixture();
        var firstHost = ConfigureCancellation(first);
        var otherHost = ConfigureCancellation(other);
        var firstService = first.ConversationService(firstHost, execution: firstHost);
        var otherService = other.ConversationService(otherHost, execution: otherHost);
        var firstRelease = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherRelease = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Adapter.Pending = firstRelease.Task;
        other.Adapter.Pending = otherRelease.Task;
        var firstReader = new AgentReader();
        var otherReader = new AgentReader();
        await using var firstWorkflow = new BoundedAgentWorkflow(firstReader, first, first, firstService, firstHost, first, first.Time);
        await using var otherWorkflow = new BoundedAgentWorkflow(otherReader, other, other, otherService, otherHost, other, other.Time);
        using var root = first.Root();
        using var firstReview = await firstWorkflow.ReviewAsync("first.agent.md", new(first.Session, null),
            "first original task", Kora.Core.Hosting.RequestOrigin.LocalUi, () => true, Token);
        using var otherReview = await otherWorkflow.ReviewAsync("other.agent.md", new(other.Session, null),
            "independent original task", Kora.Core.Hosting.RequestOrigin.LocalUi, () => true, Token);
        var firstRun = firstWorkflow.InvokeAsync(firstReview, Token);
        var otherRun = otherWorkflow.InvokeAsync(otherReview, Token);
        try
        {
            var observed = (await firstService.ReadRunningConversationAsync(new(first.Session, null), () => true, Token))!;
            var independent = (await otherService.ReadRunningConversationAsync(new(other.Session, null), () => true, Token))!;
            (await firstService.CancelRunningConversationAsync(observed.Target, () => true, Token))
                .Phase.Should().Be(ConversationExecutionPhase.Cancelling);
            firstRun.IsCompleted.Should().BeFalse();
            otherRun.IsCompleted.Should().BeFalse();
            firstReader.Released.Should().BeFalse();
            otherReader.Released.Should().BeFalse();
            otherService.IsExecutionProgressCurrent(independent).Should().BeTrue();
            other.CancelAudit.Events.Should().BeEmpty();
            firstRelease.SetResult(LateAnswer());
            (await firstRun).Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
            firstReader.Released.Should().BeTrue();
            otherRun.IsCompleted.Should().BeFalse();
            otherService.IsExecutionProgressCurrent(independent).Should().BeTrue();
            otherRelease.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                new("independent genuine answer", null)));
            var retained = await otherRun;
            retained.Message.Body.Should().Be("independent original task");
            retained.Message.AssistantResponse!.Body.Should().Be("independent genuine answer");
            retained.Message.Request.SessionId.Should().Be(other.Session.SessionId);
            otherReader.Released.Should().BeTrue();
            firstHost.IsQuiescent.Should().BeTrue();
            otherHost.IsQuiescent.Should().BeTrue();
        }
        finally
        {
            firstRelease.TrySetResult(new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate));
            otherRelease.TrySetResult(new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate));
            await Task.WhenAll(firstRun, otherRun);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalRunningProgressRequiresExactLiveLeaseAndCannotOutliveWithdrawalOrCompletion(bool withdraw)
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        var service = f.ConversationService(host, execution: host);
        var release = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = release.Task;
        var current = true;
        var submission = service.SubmitAsync(new(f.Session, null), "original progress request", () => current, Token);
        try
        {
            var observed = (await service.ReadRunningConversationAsync(new(f.Session, null), static () => true, Token))!;
            service.IsExecutionProgressCurrent(observed).Should().BeTrue();
            service.IsExecutionProgressCurrent(observed with { Progress = null }).Should().BeFalse();
            service.IsExecutionProgressCurrent(observed with { Phase = ConversationExecutionPhase.Waiting }).Should().BeFalse();
            service.IsExecutionProgressCurrent(observed with { Phase = ConversationExecutionPhase.Completing }).Should().BeFalse();
            service.IsExecutionProgressCurrent(observed with
            {
                Target = observed.Target with { Request = Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.LocalUi) },
            }).Should().BeFalse();
            service.IsExecutionProgressCurrent(observed with
            {
                Progress = observed.Progress! with { Revision = 100 },
            }).Should().BeFalse();
            if (withdraw)
            {
                current = false;
                service.IsExecutionProgressCurrent(observed).Should().BeFalse();
            }
            release.SetResult(new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate));
            if (withdraw)
            { await submission.Invoking(value => value).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>(); }
            else { await submission; }
            service.IsExecutionProgressCurrent(observed).Should().BeFalse();
            host.IsQuiescent.Should().BeTrue();
            f.Retained!.AssistantResponse.Should().BeNull();
        }
        finally
        {
            release.TrySetResult(new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate));
            try { await submission; }
            catch (ConversationSubmissionUnconfirmedException) when (withdraw) { }
        }
    }
}
