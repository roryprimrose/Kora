using System.Threading.Channels;

using AwesomeAssertions;

using Kora.Application.Visuals;
using Kora.Core;
using Kora.Core.Presentation;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Visuals;

public sealed partial class PresenceRenderWorkerTests
{
    [Theory]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("scaling")]
    public async Task GeometryChangesDuringDrawDiscardPixelsBeforeDispatchingTheNewGeneration(string component)
    {
        using var draw = new WorkerGate();
        using var target = new FakeTarget { BeforeDrawReturns = draw.BlockOnce };
        var queue = Channel.CreateUnbounded<Action>();
        using var worker = Create(target, queue);
        var next = component switch
        {
            "width" => Inputs() with { Width = 480 },
            "height" => Inputs() with { Height = 600 },
            "scaling" => Inputs() with { Scaling = 2 },
            _ => throw new ArgumentException("Unknown geometry component.", nameof(component)),
        };
        try
        {
            worker.Update(Inputs());
            await draw.Entered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);

            worker.Update(next);
            worker.PresentedOpacity.Should().Be(0);
            queue.Reader.TryRead(out _).Should().BeFalse();
        }
        finally
        {
            draw.Release();
        }

        var fresh = await Next(queue);
        target.Draws.Should().Be(2, "the invalidated in-flight draw must not enqueue a presentation");
        target.LastWidth.Should().Be(next.Width);
        target.LastHeight.Should().Be(next.Height);
        target.LastScaling.Should().Be(next.Scaling);
        fresh();
        target.Presents.Should().Be(1);
        worker.PresentedOpacity.Should().Be(0, "a new size generation starts a fresh visibility transition");
    }

    [Fact]
    public async Task DisposalDuringDrawReleasesResourcesWithoutDispatchingTheInFlightFrame()
    {
        using var draw = new WorkerGate();
        var hidden = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var target = new FakeTarget
        {
            BeforeDrawReturns = draw.BlockOnce,
            OnHide = () => hidden.TrySetResult(),
        };
        var queue = Channel.CreateUnbounded<Action>();
        using var worker = Create(target, queue);
        Task disposing = Task.CompletedTask;
        try
        {
            worker.Update(Inputs());
            await draw.Entered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);

            disposing = Task.Run(worker.Dispose, TestContext.Current.CancellationToken);
            await hidden.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            target.Released.Should().BeFalse();
            worker.HasVisibleFrame.Should().BeFalse();
        }
        finally
        {
            draw.Release();
            await disposing.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        }

        target.Draws.Should().Be(1);
        target.Releases.Should().Be(1);
        target.ReleaseThread.Should().Be(target.DrawingThread);
        target.Presents.Should().Be(0);
        queue.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task DisposalTimeoutDoesNotClaimReleasedResourcesAndCanBeRetriedAfterDrawUnblocks()
    {
        using var draw = new WorkerGate();
        using var target = new FakeTarget { BeforeDrawReturns = draw.BlockOnce };
        var queue = Channel.CreateUnbounded<Action>();
        using var worker = Create(target, queue);
        try
        {
            worker.Update(Inputs());
            await draw.Entered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);

            var dispose = () => worker.Dispose();

            dispose.Should().Throw<TimeoutException>()
                .WithMessage("The native presence drawing worker did not release its resources.");
            target.Released.Should().BeFalse();
            worker.HasVisibleFrame.Should().BeFalse();
        }
        finally
        {
            draw.Release();
            worker.Dispose();
        }

        target.Releases.Should().Be(1);
        target.ReleaseThread.Should().Be(target.DrawingThread);
        queue.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public void SuspensionAndRepeatedDisposalAreNoOpsAfterResourcesHaveBeenReleased()
    {
        using var target = new FakeTarget();
        var queue = Channel.CreateUnbounded<Action>();
        using var worker = Create(target, queue);
        worker.Dispose();

        worker.Suspend();
        worker.Dispose();

        target.Hides.Should().Be(1);
        target.Releases.Should().Be(1);
        worker.PresentedOpacity.Should().Be(0);
        var update = () => worker.Update(Inputs());
        update.Should().Throw<ObjectDisposedException>();
        queue.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task HiddenStatePreservesTheParticleArrangementWhileAPositiveOpacityFrameFadesOut()
    {
        using var secondDraw = new WorkerGate();
        var draws = 0;
        using var target = new FakeTarget
        {
            BeforeDrawReturns = () =>
            {
                if (Interlocked.Increment(ref draws) == 2)
                {
                    secondDraw.BlockOnce();
                }
            },
        };
        var queue = Channel.CreateUnbounded<Action>();
        using var worker = Create(target, queue);
        PresenceParticle[] beforeHide;
        try
        {
            worker.Update(Inputs());
            (await Next(queue))();
            await secondDraw.Entered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            beforeHide = target.LastParticles;

            worker.Update(Inputs() with { State = AssistantState.Hidden });
        }
        finally
        {
            secondDraw.Release();
        }

        (await Next(queue))();
        worker.HasVisibleFrame.Should().BeTrue();
        var hidden = await Next(queue);
        target.LastParticles.Should().Equal(beforeHide);
        hidden();
        worker.PresentedOpacity.Should().BeGreaterThan(0, "the hidden state retains a fading, motionless frame");
        worker.Dispose();
        target.Releases.Should().Be(1);
    }

    [Theory]
    [InlineData(false, 244, 156, 156)]
    [InlineData(true, 134, 86, 86)]
    public async Task ThemeFactorIsAppliedToDrawnColorWithoutChangingFrameOpacity(
        bool lightTheme, byte red, byte green, byte blue)
    {
        using var target = new FakeTarget();
        var queue = Channel.CreateUnbounded<Action>();
        using var worker = Create(target, queue);

        worker.Update(Inputs() with { State = AssistantState.Failure, IsLightTheme = lightTheme });
        var present = await Next(queue);

        target.LastRed.Should().Be(red);
        target.LastGreen.Should().Be(green);
        target.LastBlue.Should().Be(blue);
        present();
        worker.PresentedOpacity.Should().Be(0);
    }

    [Fact]
    public async Task DrawingResourceReleaseFailureIsReportedAfterTheWorkerStops()
    {
        using var target = new FakeTarget { ReleaseFailure = new IOException("Synthetic drawing resource release failure.") };
        var queue = Channel.CreateUnbounded<Action>();
        var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var worker = new PresenceRenderWorker(target, action => queue.Writer.TryWrite(action),
            exception => reported.TrySetResult(exception), NullLogger<PresenceRenderWorker>.Instance);
        worker.Update(Inputs());
        var stale = await Next(queue);

        worker.Dispose();
        stale();
        (await Next(queue))();

        (await reported.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken)).Should().BeSameAs(target.ReleaseFailure);
        target.Releases.Should().Be(1);
        target.ReleaseThread.Should().Be(target.DrawingThread);
        target.Presents.Should().Be(0);
        worker.HasVisibleFrame.Should().BeFalse();
        queue.Reader.TryRead(out _).Should().BeFalse();
    }

    internal sealed class WorkerGate : IDisposable
    {
        private readonly ManualResetEventSlim release = new();
        private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        private int calls;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void BlockOnce()
        {
            if (Interlocked.Increment(ref calls) != 1)
            {
                return;
            }
            Entered.TrySetResult();
            // Outlast the worker's real five-second disposal deadline so its timeout path is deterministic.
            if (!release.Wait(TimeSpan.FromSeconds(15), cancellationToken))
            {
                throw new TimeoutException("The test did not release the blocked presence draw.");
            }
        }

        public void Release() => release.Set();
        public void Dispose() => release.Dispose();
    }
}
