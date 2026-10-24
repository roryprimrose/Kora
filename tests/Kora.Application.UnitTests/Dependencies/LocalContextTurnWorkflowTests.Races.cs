using System.Diagnostics;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class LocalContextTurnWorkflowTests
{
    [Fact]
    public async Task DisposalDuringSourceCheckReportsCancellationBeforeAnyModelDispatch()
    {
        await using var fixture = new Fixture(fresh: false);
        using var root = fixture.Root();
        var armed = false;
        Task? disposal = null;
        bool Current()
        {
            if (!armed) { return true; }
            disposal = fixture.Workflow.DisposeAsync().AsTask();
            return false;
        }
        var admission = await fixture.Workflow.AdmitAsync(fixture.Target, Current, Token);
        await using var turn = admission.Turn!;
        armed = true;
        var receipt = await turn.RunAsync(fixture.Context(), Token);
        receipt.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        receipt.Answer.Should().BeNull();
        fixture.Model.Calls.Should().Be(0);
        await disposal!;
    }

    [Fact]
    public async Task NullSubmissionDoesNotConsumeTheAdmittedTurn()
    {
        await using var fixture = new Fixture(fresh: false);
        using var root = fixture.Root();
        var admission = await fixture.Workflow.AdmitAsync(fixture.Target, () => fixture.Eligible, Token);
        await using var turn = admission.Turn ?? throw new InvalidOperationException("Missing admission.");
        var submit = () => turn.RunAsync(null!, Token);

        await submit.Should().ThrowAsync<ArgumentNullException>();

        turn.Completion.IsCompleted.Should().BeFalse();
        fixture.Model.Calls.Should().Be(0);
        (await turn.RunAsync(fixture.Context(), Token)).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        fixture.Model.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("stopped")]
    [InlineData("completed")]
    public async Task SubmissionWithoutLiveOriginalAuthorityTerminatesItsIntentWithoutModelWork(string state)
    {
        await using var fixture = new Fixture(fresh: false);
        using var root = fixture.Root();
        var admission = await fixture.Workflow.AdmitAsync(fixture.Target, () => fixture.Eligible, Token);
        await using var turn = admission.Turn ?? throw new InvalidOperationException("Missing admission.");
        var previousActivity = Activity.Current;
        try
        {
            if (state is "missing") { Activity.Current = null; }
            if (state is "completed") { root.Complete(HostOperationOutcome.Completed); }
            if (state is "stopped")
            {
                root.Activity!.Stop();
                Activity.Current = root.Activity;
            }

            var receipt = await turn.RunAsync(fixture.Context(), Token);

            receipt.Outcome.Should().Be(ModelTurnOutcome.Denied);
            receipt.Reason.Should().Be(LocalContextTurnReason.OriginalInputRequired);
            receipt.DurableState.Should().Be(HostTaskState.Denied);
            receipt.Answer.Should().BeNull();
            fixture.Model.Calls.Should().Be(0);
        }
        finally { Activity.Current = previousActivity; }
    }

    [Fact]
    public async Task SubmissionCannotReopenACancelledHandshakeWhileTerminalReadbackIsPending()
    {
        await using var fixture = new Fixture(fresh: false);
        using var root = fixture.Root();
        using var cancellation = new CancellationTokenSource();
        var admission = await fixture.Workflow.AdmitAsync(fixture.Target, () => fixture.Eligible, cancellation.Token);
        await using var turn = admission.Turn ?? throw new InvalidOperationException("Missing admission.");
        var terminalEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTerminal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Store.BeforeTerminalRead = async () =>
        {
            terminalEntered.TrySetResult();
            await releaseTerminal.Task.WaitAsync(Token);
        };
        try
        {
            await cancellation.CancelAsync();
            await terminalEntered.Task.WaitAsync(Token);
            var submit = () => turn.RunAsync(fixture.Context(), Token);

            await submit.Should().ThrowAsync<InvalidOperationException>().WithMessage("*submission is unavailable*");

            turn.Completion.IsCompleted.Should().BeFalse();
            fixture.Model.Calls.Should().Be(0);
        }
        finally { releaseTerminal.TrySetResult(); }

        var receipt = await turn.Completion;
        receipt.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        receipt.DurableState.Should().Be(HostTaskState.Unknown);
        receipt.Answer.Should().BeNull();
        fixture.Store.Tasks.Last().State.Should().Be(HostTaskState.Unknown);
        (await turn.RunAsync(fixture.Context(), Token)).Reason.Should().Be(LocalContextTurnReason.TargetAlreadyUsed);
    }
}
