using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Fact]
    public async Task UnconfirmedExactLocalJoinQuarantinesWithoutOfferingProofSuccessorOrSend()
    {
        using var f = new Fixture();
        var (host, workflow, operation, runtime, presented) = f.VerifiedOperation();
        f.Adapter.OnRun = () => f.Adapter.JoinConfirmed = false;
        f.DetachExpectedFailedHost(host);
        Exception? disposalFailure;
        try
        {
            await Assert.ThrowsAsync<ConversationSubmissionUnconfirmedException>(() =>
                operation.RunAsync(new(f.Session, null), "original", static () => true, Token));
            presented.Task.IsCompleted.Should().BeFalse();
            workflow.Pending().Should().BeEmpty();
            f.HandoffCommits.Should().Be(0);
            f.Retained!.Outcome.Should().BeNull();
            runtime.Calls.Should().Be(0);
            host.IsQuiescent.Should().BeFalse();
        }
        finally
        {
            disposalFailure = await Record.ExceptionAsync(async () => await host.DisposeAsync());
            f.Adapter.JoinConfirmed = true;
        }
        disposalFailure.Should().BeOfType<AggregateException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledExactSuccessorKeepsItsOwnJoinAndSuppressesIgnoredLateHostedAnswer(bool failJoin)
    {
        using var f = new Fixture();
        var (host, workflow, operation, runtime, presented) = f.VerifiedOperation();
        if (failJoin) { f.DetachExpectedFailedHost(host); }
        var running = operation.RunAsync(new(f.Session, null), "original", static () => true, Token);
        await presented.Task.WaitAsync(Token);
        var source = new ModelHandoffPresentation(workflow);
        var offer = (await source.ReadPending(RequestOrigin.LocalUi, static () => true, Token)).Single();
        var review = source.Open(offer, RequestOrigin.LocalUi, static () => true)!;
        await review.Refresh(Token);
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.OnTransmission = () => sent.TrySetResult();
        f.Time.ExpectedDeadline = HostedTurnAuthorization.MaximumDuration;
        f.HandoffReviews.Offered += exact =>
        {
            using var presentation = exact.Presentation();
            f.HandoffReviews.Confirm(exact, true, RequestOrigin.LocalUi).Should().BeTrue();
        };
        var deciding = review.Decide(offer, ModelHandoffDecision.Approve, RequestOrigin.LocalUi, Token);
        try
        {
            await sent.Task.WaitAsync(Token);
            offer.Operation!.Cancel();
            offer.Operation.Cancel();
            runtime.Transmissions.Should().Be(1);
            running.IsCompleted.Should().BeFalse("logical cancellation is not the physical resource join");
            deciding.IsCompleted.Should().BeFalse();
            host.IsQuiescent.Should().BeFalse();
            f.Retained!.AssistantResponse.Should().BeNull();
            runtime.JoinConfirmed = !failJoin;
            runtime.Pending.SetResult(new(ModelTurnOutcome.Succeeded, "ignored private late answer"));
            var failure = await Record.ExceptionAsync(async () => await running);
            failure.Should().NotBeNull();
            if (failJoin) { failure.Should().BeOfType<AggregateException>(); }
            await Assert.ThrowsAnyAsync<Exception>(async () => await deciding);
            await Assert.ThrowsAnyAsync<Exception>(async () => await review.DisposeAsync());
            f.Retained.AssistantResponse.Should().BeNull();
            f.Retained.Outcome.Should().BeNull("cancelled disclosure cannot become an invented successful terminal answer");
            f.Messages.Should().NotContain(item => item.Contains("ignored private late answer", StringComparison.Ordinal));
            f.HandoffCommits.Should().Be(1);
            if (failJoin) { await Assert.ThrowsAsync<AggregateException>(async () => await host.DisposeAsync()); }
            else { await host.WaitForQuiescenceAsync(Token); host.IsQuiescent.Should().BeTrue(); }
        }
        finally
        {
            runtime.Pending.TrySetResult(new(ModelTurnOutcome.Unknown));
        }
    }
}
