using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Fact]
    public async Task StopAllCompatibilityReadFinishesRatherThanFakeCancelsAndRetainsExactTerminalFacts()
    {
        using var f = new Fixture { FileReceipts = true };
        await using var queue = f.QueueService();
        await AddMovePair(f, queue);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.OnObserve = () => { entered.TrySetResult(); release.Wait(f.Token); };
        var dispatch = queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueDispatch) with { QueueRevision = f.QueueRevision },
            RequestOrigin.LocalUi, () => true, f.Token);
        await entered.Task;
        using var review = await queue.ReviewApplicationLifecycleAsync(RequestOrigin.LocalUi, () => true, f.Token);
        try
        {
            review.StopAccepted.Should().BeFalse();
            review.TerminalQueues.Should().BeEmpty();
            var joining = queue.ConfirmApplicationLifecycleAsync(review, f.Token);
            review.StopAccepted.Should().BeTrue();
            joining.IsCompleted.Should().BeFalse();
            dispatch.IsCompleted.Should().BeFalse();
            release.Set();
            await joining;
            await dispatch;
            review.Joined.Should().BeTrue();
            review.TerminalQueues.Should().ContainSingle().Which.Entries.Select(item => item.State)
                .Should().BeEquivalentTo([SessionQueueState.Succeeded, SessionQueueState.Cancelled]);
            f.QueueActions.Should().Be(1);
        }
        finally { release.Set(); }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("request")]
    [InlineData("generation")]
    [InlineData("pending")]
    [InlineData("running")]
    public async Task StopAllCannotClaimCompletionFromInvalidCompatibilityTerminalReadback(string fault)
    {
        using var f = new Fixture { FileReceipts = true };
        await using var queue = f.QueueService();
        await queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueEnqueue),
            RequestOrigin.LocalUi, () => true, f.Token);
        var original = f.QueueRows.Single();
        using var review = await queue.ReviewApplicationLifecycleAsync(RequestOrigin.LocalUi, () => true, f.Token);
        f.BeforeTaskWrite = record =>
        {
            if (!record.IsTerminal) { return; }
            f.QueueRows.Clear();
            if (fault is "missing") { return; }
            f.QueueRows.Add(fault switch
            {
                "request" => original with { State = SessionQueueState.Cancelled,
                    Request = new(new(Guid.NewGuid()), original.Request.SessionId, original.Request.TaskId, original.Request.Origin) },
                "generation" => original with { State = SessionQueueState.Cancelled, Generation = new(2) },
                "pending" => original,
                _ => original with { State = SessionQueueState.Running },
            });
        };
        await queue.Invoking(value => value.ConfirmApplicationLifecycleAsync(review, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        review.StopAccepted.Should().BeTrue();
        review.Joined.Should().BeFalse();
        review.TerminalQueues.Should().BeEmpty();
        f.QueueActions.Should().Be(0);
    }
}
