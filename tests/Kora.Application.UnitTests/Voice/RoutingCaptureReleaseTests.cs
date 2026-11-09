using AwesomeAssertions;
using Kora.Application.UnitTests.Interaction;
using Kora.Application.Voice;

namespace Kora.Application.UnitTests.Voice;

public sealed class RoutingCaptureReleaseTests
{
    [Fact]
    public async Task EarlierHostStopsPhysicallyJoinBeforeAnyReleaseReceipt()
    {
        var capture = new QuestionReplyFixture.ReplyCapture();
        var owner = new RoutingCaptureRelease(capture, HandsFreeVoiceContinuation.CaptureRelease.Preserved);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.StopHostAsync(() => stopped.Task).Should().BeSameAs(stopped.Task);
        var joining = owner.JoinAsync();
        joining.IsCompleted.Should().BeFalse();
        var reopen = () => owner.BeginCapture();
        reopen.Should().Throw<InvalidOperationException>();

        stopped.SetResult();
        await joining;

        capture.Stops.Should().Be(1);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("during")]
    [InlineData("joining")]
    [InlineData("nested")]
    public async Task HostInvalidationIsAccountedWithoutAcceptingForeignGenerationChanges(string ordering)
    {
        var capture = new QuestionReplyFixture.ReplyCapture();
        var owner = new RoutingCaptureRelease(capture, HandsFreeVoiceContinuation.CaptureRelease.Preserved);
        owner.IsCaptureActive.Should().BeFalse();
        owner.HasFailedRelease.Should().BeFalse();
        owner.BeginCapture();
        await capture.BeginReplyAsync(new("mic", "Synthetic"), [], () => true, TestContext.Current.CancellationToken);
        owner.CaptureStarted();
        owner.IsCaptureActive.Should().BeTrue();
        var original = capture.CaptureGeneration;
        if (ordering is "before") { owner.Invalidate(capture.InvalidateCapture); }
        if (ordering is "during") { capture.BeforeStop = () => owner.Invalidate(capture.InvalidateCapture); }
        if (ordering is "joining" or "nested") { capture.Closing = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        var joining = owner.JoinAsync();
        owner.JoinAsync().Should().BeSameAs(joining);
        if (ordering is "joining" or "nested")
        {
            joining.IsCompleted.Should().BeFalse();
            if (ordering is "nested") { owner.Invalidate(() => owner.Invalidate(capture.InvalidateCapture)); }
            else { owner.Invalidate(capture.InvalidateCapture); }
            capture.Closing!.SetResult();
        }

        (await joining).Should().Be(original + 1);
        capture.Stops.Should().Be(1);
        capture.IsCaptureQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task ReentrantReleaseWaitsForTheHostInvalidationReceipt()
    {
        var capture = new QuestionReplyFixture.ReplyCapture();
        var owner = new RoutingCaptureRelease(capture, HandsFreeVoiceContinuation.CaptureRelease.Preserved);
        Task<long>? joining = null;

        owner.Invalidate(() =>
        {
            joining = owner.JoinAsync();
            capture.Stops.Should().Be(0);
            capture.InvalidateCapture();
        });

        (await joining!).Should().Be(capture.CaptureGeneration);
        capture.Stops.Should().Be(1);
    }

    [Fact]
    public async Task InlineNativeCompletionWaitsForAReentrantHostMutationToFinish()
    {
        await Task.Run(async () =>
        {
            var capture = new QuestionReplyFixture.ReplyCapture();
#pragma warning disable MA0093 // Force an inline native callback while the host invalidation receipt is still being published.
            capture.Closing = new TaskCompletionSource();
#pragma warning restore MA0093
            var owner = new RoutingCaptureRelease(capture, HandsFreeVoiceContinuation.CaptureRelease.Preserved);
            var joining = owner.JoinAsync();

            owner.Invalidate(() =>
            {
                capture.InvalidateCapture();
                capture.Closing.SetResult();
                joining.IsCompleted.Should().BeFalse();
            });

            (await joining).Should().Be(1);
            capture.IsCaptureQuiescent.Should().BeTrue();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ACompletedReleaseCannotReplaceAFreshHostStopOrTheNextCapture()
    {
        var capture = new QuestionReplyFixture.ReplyCapture();
        var owner = new RoutingCaptureRelease(capture, HandsFreeVoiceContinuation.CaptureRelease.Preserved);
        var first = owner.JoinAsync();
        var receipt = await first;
        owner.Invalidate(capture.InvalidateCapture);
        var second = owner.JoinAsync();
        second.Should().NotBeSameAs(first);
        (await second).Should().Be(receipt + 1);
        owner.BeginCapture();
        owner.IsCaptureActive.Should().BeFalse();
        await capture.BeginReplyAsync(new("mic", "Synthetic"), [], () => true, TestContext.Current.CancellationToken);
        owner.CaptureStarted();
        (await owner.JoinAsync()).Should().Be(receipt + 2);
        capture.Stops.Should().Be(3);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("non-quiescent")]
    [InlineData("stop-failure")]
    public async Task UnconfirmedReleaseNeverProvidesAReceiptOrAllowsAnotherCapture(string failure)
    {
        var capture = new QuestionReplyFixture.ReplyCapture();
        var owner = new RoutingCaptureRelease(capture, HandsFreeVoiceContinuation.CaptureRelease.Preserved);
        capture.Closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (failure is "stop-failure") { capture.StopFailure = new IOException("Synthetic stop failure."); }
        var joining = owner.JoinAsync();
        if (failure is "foreign") { capture.AdvanceGeneration(); }
        if (failure is "non-quiescent") { capture.NonQuiescent = true; }
        capture.Closing.SetResult();
        var completion = () => joining;
        await completion.Should().ThrowAsync<Exception>();
        var reopen = () => owner.BeginCapture();
        reopen.Should().Throw<InvalidOperationException>();
        owner.JoinAsync().Should().BeSameAs(joining);
    }

    [Fact]
    public async Task AWaitingReleasePreventsStartingAnotherCapture()
    {
        var capture = new QuestionReplyFixture.ReplyCapture { Closing = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var owner = new RoutingCaptureRelease(capture, HandsFreeVoiceContinuation.CaptureRelease.Preserved);
        var joining = owner.JoinAsync();
        var reopen = () => owner.BeginCapture();
        reopen.Should().Throw<InvalidOperationException>();
        capture.Closing.SetResult();
        await joining;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedHostInvalidationStillStopsButCannotClaimRelease(bool backwards)
    {
        var capture = new QuestionReplyFixture.ReplyCapture();
        capture.SetGeneration(10);
        var owner = new RoutingCaptureRelease(capture, HandsFreeVoiceContinuation.CaptureRelease.Preserved);
        var invalidating = () => owner.Invalidate(() =>
        {
            if (backwards) { capture.SetGeneration(9); }
            else { throw new IOException("Synthetic invalidation failure."); }
        });
        invalidating.Should().Throw<Exception>();
        await owner.Invoking(value => value.JoinAsync()).Should().ThrowAsync<InvalidOperationException>();
        capture.Stops.Should().Be(1);
        capture.IsCaptureQuiescent.Should().BeTrue();
        var reopen = () => owner.BeginCapture();
        reopen.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostStopRequiresItsActualTask(bool missing)
    {
        var capture = new QuestionReplyFixture.ReplyCapture();
        var owner = new RoutingCaptureRelease(capture, HandsFreeVoiceContinuation.CaptureRelease.Preserved);
        if (missing)
        {
            var stop = () => owner.StopHostAsync(MissingHostStop);
            await stop.Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            var stopping = capture.StopAsync(TestContext.Current.CancellationToken);
            owner.StopHostAsync(() => stopping).Should().BeSameAs(stopping);
            await stopping;
        }
    }

#pragma warning disable MA0022, VSTHRD114 // Deliberately violate the host stop contract to verify explicit failure.
    private static Task MissingHostStop() => null!;
#pragma warning restore MA0022, VSTHRD114

    [Fact]
    public async Task TheDeclaredNativeStopAdvanceRemainsRequired()
    {
        var capture = new QuestionReplyFixture.ReplyCapture();
        capture.BeforeStop = capture.InvalidateCapture;
        var owner = new RoutingCaptureRelease(capture, HandsFreeVoiceContinuation.CaptureRelease.InvalidatedOnce);

        (await owner.JoinAsync()).Should().Be(1);

        capture.Stops.Should().Be(1);
    }

    [Fact]
    public async Task AnExplicitRetiredCloseCanRetryButCannotReopenTheOriginal()
    {
        var capture = new QuestionReplyFixture.ReplyCapture { StopFailure = new IOException("Synthetic first stop failure.") };
        var owner = new RoutingCaptureRelease(capture, HandsFreeVoiceContinuation.CaptureRelease.Preserved);
        await owner.Invoking(value => value.JoinAsync()).Should().ThrowAsync<IOException>();
        owner.HasFailedRelease.Should().BeTrue();
        capture.StopFailure = null;

        await owner.JoinRetiredAsync();

        owner.HasFailedRelease.Should().BeFalse();
        var reopen = () => owner.BeginCapture();
        reopen.Should().Throw<InvalidOperationException>();
        capture.Stops.Should().Be(2);
    }
}
