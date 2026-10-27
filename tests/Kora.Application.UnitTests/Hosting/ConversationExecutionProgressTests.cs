using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Hosting;

public sealed class ConversationExecutionProgressTests
{
    [Theory]
    [InlineData("waiting")]
    [InlineData("withdrawn")]
    [InlineData("caller")]
    [InlineData("cancelled")]
    [InlineData("completing")]
    [InlineData("terminal")]
    public async Task OnlyCurrentRunningLeaseCanPublishContentFreeOrderedSteps(string retirement)
    {
        var target = new ConversationCancellationTarget(Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi),
            new(1), new(1), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1));
        var statuses = new List<ConversationExecutionStatus>();
        var current = true;
        using var caller = new CancellationTokenSource();
        await using var lease = new ConversationCancellationLease(target, () => current, statuses.Add, default, caller.Token);
        lease.Status.Progress.Should().BeNull();
        if (retirement is not "waiting")
        {
            lease.Dispatched(new(2));
            lease.Status.Progress.Should().Be(new ConversationExecutionProgress(ConversationExecutionStep.GeneratingAnswer, 0, 0));
            lease.PresentExecutionStep(ConversationExecutionStep.ReadingObservation);
            lease.PresentExecutionStep(ConversationExecutionStep.GeneratingAnswer);
            lease.PresentExecutionStep(ConversationExecutionStep.ReadingObservation);
            statuses.Select(status => status.Progress).Should().Equal(
                new ConversationExecutionProgress(ConversationExecutionStep.ReadingObservation, 1, 1),
                new ConversationExecutionProgress(ConversationExecutionStep.GeneratingAnswer, 1, 2),
                new ConversationExecutionProgress(ConversationExecutionStep.ReadingObservation, 2, 3));
            var dispatched = target with { TaskRevision = new(2) };
            statuses.Should().OnlyContain(status => status.Target == dispatched
                && status.Phase == ConversationExecutionPhase.Running && status.Outcome == null && status.TaskState == null);
        }
        switch (retirement)
        {
            case "withdrawn": current = false; break;
            case "caller": await caller.CancelAsync(); break;
            case "cancelled": await lease.RequestCancellationAsync(static () => { }); break;
            case "completing": lease.BeginCompletion().Should().BeFalse(); break;
            case "terminal": lease.Complete(ModelTurnOutcome.Succeeded, HostTaskState.Succeeded); break;
        }
        var count = statuses.Count;
        var prior = lease.Status;
        lease.PresentExecutionStep(ConversationExecutionStep.GeneratingAnswer);
        lease.PresentExecutionStep(ConversationExecutionStep.ReadingObservation);
        statuses.Should().HaveCount(count);
        lease.Status.Should().Be(prior);
        if (retirement is "cancelled" or "completing" or "terminal") { lease.Status.Progress.Should().BeNull(); }
    }
}
