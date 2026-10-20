using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Fact]
    public async Task ExecutionPortCanObserveIdleAndDisposedQuiescenceWithoutCreatingAnyHostOrProviderAuthority()
    {
        using var fixture = new Fixture();
        IModelTurnExecution execution = fixture.Host;
        await execution.WaitForQuiescenceAsync(Token);
        fixture.Adapter.Calls.Should().Be(0);
        await fixture.Host.DisposeAsync();
        await execution.WaitForQuiescenceAsync(Token);
        fixture.Adapter.Calls.Should().Be(0);
        fixture.Host.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task PreCancelledQuiescenceObservationRemainsCancelledEvenIfNoWorkWasDispatched()
    {
        using var fixture = new Fixture();
        IModelTurnExecution execution = fixture.Host;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var observe = () => execution.WaitForQuiescenceAsync(cancellation.Token);
        var failure = await observe.Should().ThrowAsync<OperationCanceledException>();
        failure.Which.CancellationToken.Should().Be(cancellation.Token);
        fixture.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CancelledOrTimedOutTurnKeepsQuiescenceObservationPendingUntilActualLateSuccessOrFault(
        bool deadline, bool fault)
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        using var observerCancellation = new CancellationTokenSource();
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Adapter.Pending = pending.Task;
        IModelTurnExecution execution = fixture.Host;
        try
        {
            using (var root = fixture.Root())
            {
                var admission = await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate,
                    fixture.Context(toolMode: ModelToolMode.None), Token);
                var running = execution.RunAsync(admission.Turn!, static () => true, cancellation.Token);
                fixture.Adapter.Calls.Should().Be(1);
                if (deadline) { fixture.Time.FireDeadline(); }
                else { await cancellation.CancelAsync(); }
                var terminal = await running;
                terminal.Outcome.Should().Be(deadline ? ModelTurnOutcome.TimedOut : ModelTurnOutcome.Cancelled);
                terminal.Response.Should().BeNull();
            }
            fixture.Host.IsQuiescent.Should().BeFalse();
            var waiting = execution.WaitForQuiescenceAsync(Token);
            var cancelledWait = execution.WaitForQuiescenceAsync(observerCancellation.Token);
            waiting.IsCompleted.Should().BeFalse();
            cancelledWait.IsCompleted.Should().BeFalse();
            await observerCancellation.CancelAsync();
            Func<Task> observeCancelled = () => cancelledWait;
            var failure = await observeCancelled.Should().ThrowAsync<OperationCanceledException>();
            failure.Which.CancellationToken.Should().Be(observerCancellation.Token);
            waiting.IsCompleted.Should().BeFalse();
            fixture.Host.IsQuiescent.Should().BeFalse();
            if (fault) { pending.SetException(new IOException("private late source must not be logged")); }
            else { pending.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("late answer", null))); }
            await waiting;
            fixture.Host.IsQuiescent.Should().BeTrue();
            fixture.Adapter.Calls.Should().Be(1);
            fixture.Messages.Should().NotContain(message => message.Contains("private late source", StringComparison.Ordinal)
                || message.Contains("late answer", StringComparison.Ordinal));
        }
        finally
        {
            pending.TrySetResult(new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate));
            await fixture.Host.WaitForQuiescenceAsync(Token);
        }
    }

    [Fact]
    public async Task QuiescenceObserverDoesNotCancelOrDisposeTheRunningAdapterWhenTheObserverCancels()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        using var observerCancellation = new CancellationTokenSource();
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Adapter.Pending = pending.Task;
        IModelTurnExecution execution = fixture.Host;
        var admission = await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate,
            fixture.Context(toolMode: ModelToolMode.None), Token);
        var running = execution.RunAsync(admission.Turn!, static () => true, Token);
        try
        {
            var waiting = execution.WaitForQuiescenceAsync(observerCancellation.Token);
            waiting.IsCompleted.Should().BeFalse();
            await observerCancellation.CancelAsync();
            Func<Task> observe = () => waiting;
            await observe.Should().ThrowAsync<OperationCanceledException>();
            running.IsCompleted.Should().BeFalse();
            fixture.Host.IsQuiescent.Should().BeFalse();
            pending.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("genuine answer", null)));
            (await running).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
            await execution.WaitForQuiescenceAsync(Token);
            fixture.Host.IsQuiescent.Should().BeTrue();
        }
        finally
        {
            pending.TrySetResult(new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate));
            await running;
        }
    }
}
