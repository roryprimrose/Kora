using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Fact]
    public async Task SourceFenceIsSampledAfterSlotAcquisitionImmediatelyBeforeAdapterDispatch()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var turn = (await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate,
            fixture.Context(toolMode: ModelToolMode.None), Token)).Turn!;
        IModelTurnExecution execution = fixture.Host;
        var result = await execution.RunAsync(turn, () => !turn.IsRunning, Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Denied);
        result.Reason.Should().Be(ModelTurnReason.HostAdmissionClosed);
        result.Response.Should().BeNull();
        fixture.Adapter.Calls.Should().Be(0);
        fixture.RetainCalls.Should().Be(0);
        await execution.WaitForQuiescenceAsync(Token);
        fixture.Host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceRevocationDuringAwaitedSourceOrWorkspaceValidationPreventsAdapterDispatch(bool sourceValidation)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var turn = (await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate,
            fixture.Context(toolMode: ModelToolMode.None), Token)).Turn!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Wait()
        {
            entered.TrySetResult();
            await release.Task;
        }
        if (!sourceValidation) { fixture.BeforeRead = Wait; }
        var current = true;
        IModelTurnExecution execution = fixture.Host;
        var running = execution.RunAsync(turn, () => current, Token, sourceValidation ? _ => Wait() : null);
        try
        {
            await entered.Task.WaitAsync(Token);
            running.IsCompleted.Should().BeFalse();
            current = false;
        }
        finally { release.TrySetResult(); }
        var result = await running;
        result.Outcome.Should().Be(ModelTurnOutcome.Denied);
        result.Reason.Should().Be(ModelTurnReason.HostAdmissionClosed);
        result.Response.Should().BeNull();
        fixture.Adapter.Calls.Should().Be(0);
        fixture.RetainCalls.Should().Be(0);
        await execution.WaitForQuiescenceAsync(Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceRevocationDuringProviderOutputSuppressesLateAnswerAndCannotRebindAnExistingTurn(bool cancel)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Adapter.Pending = pending.Task;
        var turn = (await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate,
            fixture.Context(toolMode: ModelToolMode.None), Token)).Turn!;
        var current = true;
        IModelTurnExecution execution = fixture.Host;
        var running = execution.RunAsync(turn, () => current, cancellation.Token);
        try
        {
            fixture.Adapter.Calls.Should().Be(1);
            current = false;
            var rebound = await execution.RunAsync(turn, static () => true, Token);
            rebound.Outcome.Should().Be(ModelTurnOutcome.Denied);
            if (cancel)
            {
                await cancellation.CancelAsync();
                (await running).Outcome.Should().Be(ModelTurnOutcome.Cancelled);
                fixture.Host.IsQuiescent.Should().BeFalse();
            }
            pending.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("private stale answer", null)));
            var result = await running;
            result.Outcome.Should().Be(cancel ? ModelTurnOutcome.Cancelled : ModelTurnOutcome.Denied);
            result.Response.Should().BeNull();
            await execution.WaitForQuiescenceAsync(Token);
            fixture.Host.IsQuiescent.Should().BeTrue();
            fixture.Adapter.Calls.Should().Be(1);
            fixture.RetainCalls.Should().Be(0);
            fixture.Messages.Should().NotContain(message => message.Contains("private stale answer", StringComparison.Ordinal));
        }
        finally
        {
            pending.TrySetResult(new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate));
            await running;
            await execution.WaitForQuiescenceAsync(Token);
        }
    }

    [Theory]
    [InlineData("before-send")]
    [InlineData("after-send")]
    [InlineData("before-handler")]
    [InlineData("after-handler")]
    public async Task SupplementalSourceFenceSurroundsEveryReadOnlyContinuationWithoutGrantingToolFreeAuthority(string boundary)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var current = true;
        var versions = 0;
        fixture.OnVersion = () =>
        {
            versions++;
            if (boundary is "after-handler") { current = false; }
        };
        var transport = new ToolTransport(VersionCall, FinalAnswer);
        if (boundary is "after-send") { transport.AfterRequest = _ => current = false; }
        await using var adapter = CreateLoop(fixture, transport);
        var host = fixture.LoopHost(adapter);
        var turn = (await host.AdmitAsync(ModelProviderSelection.OllamaCandidate, fixture.Context(), Token)).Turn!;
        IModelTurnExecution execution = host;
        if (boundary is "before-send") { current = false; }
        var result = await execution.RunAsync(turn, () => current, Token, _ =>
        {
            if (boundary is "before-handler" && transport.Requests.Count == 1) { current = false; }
            return Task.CompletedTask;
        });
        result.Outcome.Should().Be(ModelTurnOutcome.Denied);
        result.Reason.Should().Be(ModelTurnReason.HostAdmissionClosed);
        result.Response.Should().BeNull();
        versions.Should().Be(boundary is "after-handler" ? 1 : 0);
        transport.Requests.Should().HaveCount(boundary is "before-send" ? 0 : 1);
        fixture.RetainCalls.Should().Be(0);
        await execution.WaitForQuiescenceAsync(Token);
    }

    [Fact]
    public async Task AlwaysTrueSourcePredicateCannotSupplyMissingDurableTaskAuthority()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var turn = (await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate,
            fixture.Context(toolMode: ModelToolMode.None), Token)).Turn!;
        fixture.Task = null;
        IModelTurnExecution execution = fixture.Host;
        var result = await execution.RunAsync(turn, static () => true, Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Denied);
        result.Reason.Should().Be(ModelTurnReason.SessionNotAdmitted);
        fixture.Adapter.Calls.Should().Be(0);
        fixture.RetainCalls.Should().Be(0);
    }
}
