using System.Diagnostics;
using System.Threading.Channels;

using AwesomeAssertions;

using Kora.Application.Visuals;
using Kora.Core;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Visuals;

public sealed class PresenceRenderWorkerTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(AssistantState.Information)]
    [InlineData(AssistantState.Waiting)]
    [InlineData(AssistantState.Success)]
    [InlineData(AssistantState.Listening)]
    [InlineData(AssistantState.Calculating)]
    [InlineData(AssistantState.Executing)]
    public void AllNonzeroMovementUsesThirtyThreeMillisecondsIndependentOfSpeed(AssistantState state)
    {
        for (var speed = PresenceSettings.MinimumMovementSpeedPercent;
            speed <= PresenceSettings.MaximumMovementSpeedPercent; speed++)
        {
            var inputs = Inputs() with { State = state, MovementSpeed = speed };
            PresenceRenderWorker.GetCadence(inputs, false, false).Should().Be(TimeSpan.FromMilliseconds(33),
                "speed {0} changes velocity, not the frame interval", speed);
        }
    }

    [Fact]
    public void SettledStaticFramesRetainBackoffWhileVisualAndSpeechChangesRemainPrompt()
    {
        var inputs = Inputs() with { ReducedMotion = true };
        PresenceRenderWorker.GetCadence(inputs, false, false).Should().Be(PresenceParticleField.QuietFrameInterval);
        PresenceRenderWorker.GetCadence(inputs, true, false).Should().Be(PresenceAnimation.FrameInterval);
        PresenceRenderWorker.GetCadence(inputs, false, true).Should().Be(PresenceAnimation.FrameInterval);
        PresenceRenderWorker.GetCadence(inputs with { IsSpeaking = true }, false, false)
            .Should().Be(PresenceAnimation.FrameInterval);
        PresenceRenderWorker.GetCadence(Inputs() with { State = AssistantState.Failure }, false, false)
            .Should().Be(PresenceParticleField.QuietFrameInterval);
    }

    [Fact]
    public async Task WorkerDrawsOffCallerThreadAndDisposesWithoutExecutingQueuedPresentation()
    {
        using var target = new FakeTarget();
        var queue = Channel.CreateUnbounded<Action>();
        using var worker = Create(target, queue);
        var caller = Environment.CurrentManagedThreadId;
        worker.Update(Inputs());
        var callback = await Next(queue);

        target.DrawingThread.Should().NotBe(caller);
        worker.HasVisibleFrame.Should().BeFalse();
        worker.Dispose();
        target.Released.Should().BeTrue();
        target.ReleaseThread.Should().Be(target.DrawingThread);
        callback();
        target.Presents.Should().Be(0);
    }

    [Fact]
    public async Task PrivacySuspensionRejectsQueuedPixelsAndResumeRequestsFreshFrame()
    {
        using var target = new FakeTarget();
        var queue = Channel.CreateUnbounded<Action>();
        using var worker = Create(target, queue);
        var state = Inputs() with { State = AssistantState.Failure, ReducedMotion = true };
        worker.Update(state);
        var stale = await Next(queue);
        worker.Suspend();
        worker.Update(state);
        stale();
        target.Presents.Should().Be(0);
        worker.PresentedOpacity.Should().Be(0);
        var fresh = await Next(queue);
        fresh();
        target.Presents.Should().Be(1);
        worker.HasVisibleFrame.Should().BeFalse("the first freshly admitted frame starts at zero opacity");
        worker.PresentedOpacity.Should().Be(0);
        target.Hides.Should().Be(1);
    }

    [Fact]
    public async Task GeometryChangesRejectQueuedFramesAndCoalesceLatestInputs()
    {
        using var target = new FakeTarget();
        var queue = Channel.CreateUnbounded<Action>();
        using var worker = Create(target, queue);
        worker.Update(Inputs());
        var oldSize = await Next(queue);
        for (var index = 0; index < 100; index++)
        {
            worker.Update(Inputs() with { Width = 600, Height = 600, Scaling = 2, SpeechOutputLevel = index / 100d });
        }
        queue.Reader.TryRead(out _).Should().BeFalse();
        oldSize();
        target.Presents.Should().Be(0);
        var fresh = await Next(queue);
        target.LastWidth.Should().Be(600);
        target.LastScaling.Should().Be(2);
        fresh();
        target.Presents.Should().Be(1);
    }

    [Fact]
    public async Task DrawFailuresAreReportedAndDoNotBecomeSuccessfulFrames()
    {
        using var target = new FakeTarget { DrawFailure = new IOException("synthetic drawing failure") };
        var queue = Channel.CreateUnbounded<Action>();
        var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var worker = new PresenceRenderWorker(target, action => queue.Writer.TryWrite(action),
            exception => failure.TrySetResult(exception), NullLogger<PresenceRenderWorker>.Instance);
        worker.Update(Inputs());
        (await Next(queue))();
        (await failure.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken)).Should().BeSameAs(target.DrawFailure);
        target.Presents.Should().Be(0);
        var retry = () => worker.Update(Inputs());
        retry.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task PresentationFailureRetiresTheWorkerAndIsReported()
    {
        using var target = new FakeTarget { PresentFailure = new IOException("synthetic presentation failure") };
        var queue = Channel.CreateUnbounded<Action>();
        var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var worker = new PresenceRenderWorker(target, action => queue.Writer.TryWrite(action),
            exception => failure.TrySetResult(exception), NullLogger<PresenceRenderWorker>.Instance);
        worker.Update(Inputs());
        (await Next(queue))();
        (await Next(queue))();
        (await failure.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken)).Should().BeSameAs(target.PresentFailure);
        var retry = () => worker.Update(Inputs());
        retry.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task VisibleFrameBecomesNoninteractiveImmediatelyOnSuspension()
    {
        using var target = new FakeTarget();
        var queue = Channel.CreateUnbounded<Action>();
        using var worker = Create(target, queue);
        worker.Update(Inputs());
        (await Next(queue))();
        worker.HasVisibleFrame.Should().BeFalse("first admission starts the visibility transition from zero");
        worker.PresentedOpacity.Should().Be(0);
        (await Next(queue))();
        worker.HasVisibleFrame.Should().BeTrue();
        worker.PresentedOpacity.Should().BeInRange(double.Epsilon, 1);
        worker.Suspend();
        worker.HasVisibleFrame.Should().BeFalse();
        worker.PresentedOpacity.Should().Be(0);
        target.Hides.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Published_opacity_tracks_completed_frames_through_both_fades(bool reducedMotion)
    {
        using var target = new FakeTarget();
        var queue = Channel.CreateUnbounded<Action>();
        using var worker = Create(target, queue);
        var inputs = Inputs() with { ReducedMotion = reducedMotion };
        worker.Update(inputs);
        (await Next(queue))();
        worker.PresentedOpacity.Should().Be(0);
        var intermediate = false;
        for (var frame = 0; frame < 100 && worker.PresentedOpacity < 1; frame++)
        {
            (await Next(queue))();
            worker.PresentedOpacity.Should().Be(target.PresentedOpacity);
            intermediate |= worker.PresentedOpacity is > 0 and < 1;
        }
        intermediate.Should().BeTrue();
        worker.PresentedOpacity.Should().Be(1);
        worker.Update(inputs with { IsPresenceVisible = false });
        intermediate = false;
        for (var frame = 0; frame < 100 && worker.PresentedOpacity > 0; frame++)
        {
            (await Next(queue))();
            worker.PresentedOpacity.Should().Be(target.PresentedOpacity);
            intermediate |= worker.PresentedOpacity is > 0 and < 1;
        }
        intermediate.Should().BeTrue();
        worker.PresentedOpacity.Should().Be(0);
        worker.HasVisibleFrame.Should().BeFalse();
    }

    [Fact]
    public async Task WorkerStartsLinkedActivityWithoutInheritingAnAmbientParent()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        using var target = new FakeTarget();
        var queue = Channel.CreateUnbounded<Action>();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Presentation);
        var rootContext = root.Activity!.Context;
        using var worker = Create(target, queue);
        worker.Update(Inputs());
        _ = await Next(queue);
        target.ActivityContext.TraceId.Should().NotBe(rootContext.TraceId);
        target.Links.Should().ContainSingle().Which.Context.Should().Be(rootContext);
        worker.Dispose();
        root.Complete(HostOperationOutcome.Completed);
    }

    [Fact]
    public async Task FailureLogsUseLatestHostAdmissionRatherThanTheWorkerStartupSession()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        using var target = new FakeTarget { PresentFailure = new IOException("synthetic second-session failure") };
        var queue = Channel.CreateUnbounded<Action>();
        var logger = new AdmissionLogger();
        using var first = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Presentation);
        using var worker = new PresenceRenderWorker(target, action => queue.Writer.TryWrite(action), _ => { }, logger);
        worker.Update(Inputs());
        var callback = await Next(queue);
        using var second = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Presentation);
        worker.Update(Inputs());
        callback();
        logger.Request.Should().BeSameAs(second.Request);
        logger.Links.Should().ContainSingle().Which.Context.Should().Be(second.Activity!.Context);
        (await Next(queue))();
        worker.Dispose();
        second.Complete(HostOperationOutcome.Completed);
        first.Complete(HostOperationOutcome.Completed);
    }

    [Theory]
    [InlineData("unknown-state")]
    [InlineData("level")]
    [InlineData("dimensions")]
    [InlineData("scale")]
    [InlineData("buffer-bound")]
    public void MalformedInputsFailBeforeNativeWork(string kind)
    {
        using var target = new FakeTarget();
        var queue = Channel.CreateUnbounded<Action>();
        using var worker = Create(target, queue);
        var input = kind switch
        {
            "unknown-state" => Inputs() with { State = (AssistantState)999 },
            "level" => Inputs() with { SpeechOutputLevel = double.NaN },
            "dimensions" => Inputs() with { Width = double.PositiveInfinity },
            "scale" => Inputs() with { Scaling = 0 },
            "buffer-bound" => Inputs() with { Scaling = 100 },
            _ => throw new ArgumentException("Unknown test case.", nameof(kind)),
        };
        var update = () => worker.Update(input);
        update.Should().Throw<InvalidDataException>();
        target.Presents.Should().Be(0);
    }

    private static PresenceRenderWorker Create(FakeTarget target, Channel<Action> queue) =>
        new(target, action => queue.Writer.TryWrite(action),
            exception => throw new InvalidOperationException("Unexpected worker failure.", exception),
            NullLogger<PresenceRenderWorker>.Instance);

    private static Task<Action> Next(Channel<Action> queue) =>
        queue.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(Deadline, TestContext.Current.CancellationToken);

    internal static PresenceRenderInputs Inputs() => new(AssistantState.Calculating, false, 0, true, 100,
        100, 100, 100, false, false, true, 360, 360, 1);

    internal sealed class FakeTarget : IPresenceRenderTarget
    {
        public int DrawingThread { get; private set; }
        public int ReleaseThread { get; private set; }
        public bool Released { get; private set; }
        public int Presents { get; private set; }
        public int Hides { get; private set; }
        public double LastWidth { get; private set; }
        public double LastScaling { get; private set; }
        private double drawnOpacity;
        public double PresentedOpacity { get; private set; }
        public Exception? DrawFailure { get; init; }
        public Exception? PresentFailure { get; init; }
        public ActivityContext ActivityContext { get; private set; }
        public ActivityLink[] Links { get; private set; } = [];

        public void Draw(ReadOnlySpan<PresenceParticle> particles, double width, double height, double scaling,
            byte red, byte green, byte blue, double opacity)
        {
            DrawingThread = Environment.CurrentManagedThreadId;
            LastWidth = width;
            LastScaling = scaling;
            drawnOpacity = opacity;
            ActivityContext = Activity.Current?.Context ?? default;
            Links = Activity.Current?.Links.ToArray() ?? [];
            if (DrawFailure is not null)
            {
                throw DrawFailure;
            }
        }

        public void Present()
        {
            if (PresentFailure is not null)
            {
                throw PresentFailure;
            }
            Presents++;
            PresentedOpacity = drawnOpacity;
        }
        public void Hide() => Hides++;
        public void ReleaseDrawingResources()
        {
            ReleaseThread = Environment.CurrentManagedThreadId;
            Released = true;
        }
        public void Dispose() { }
    }

    private sealed class AdmissionLogger : Microsoft.Extensions.Logging.ILogger<PresenceRenderWorker>
    {
        public HostRequest? Request { get; private set; }
        public ActivityLink[] Links { get; private set; } = [];
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id != 0) { return; }
            Request = HostActivity.Current?.Request;
            Links = Activity.Current?.Links.ToArray() ?? [];
        }
    }
}