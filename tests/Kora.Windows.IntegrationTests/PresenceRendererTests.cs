using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

using AwesomeAssertions;

using Kora.Controls;
using Kora.Core;
using Kora.Core.Configuration;
using Kora.Windows.Presentation;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class PresenceRendererTests
{
    private static readonly int[] DensityLevels = [25, 100, 200];

    [Fact]
    public void UnknownWindowsMotionPreferenceSurfacesFailureRatherThanEnablingMotion()
    {
        WindowsPresenceMotionPreference.Resolve(true, false, 0).Should().BeFalse();
        WindowsPresenceMotionPreference.Resolve(true, true, 0).Should().BeTrue();
        var read = () => WindowsPresenceMotionPreference.Resolve(false, true, 5);
        read.Should().Throw<System.ComponentModel.Win32Exception>()
            .Which.NativeErrorCode.Should().Be(5);
    }

    [Fact]
    public async Task HiddenDetachedIneligibleAndZeroBoundsDoNoWorkAndRetiredCallbacksStayInert()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var scheduler = new RecordingScheduler();
            using var control = new PresenceControl(scheduler) { IsPresentationEligible = true };
            control.State = AssistantState.Executing;
            scheduler.PendingCount.Should().Be(0);
            control.TickCount.Should().Be(0);
            control.InvalidationCount.Should().Be(0);
            var parent = new Border { Child = control };
            var window = new Window { Content = parent, Width = 360, Height = 360 };
            window.Show();
            window.UpdateLayout();
            scheduler.Advance(TimeSpan.FromSeconds(1));
            control.TickCount.Should().BeGreaterThan(0);

            foreach (var disable in new Action[]
            {
                () => parent.IsVisible = false,
                () => window.Hide(),
                () => control.IsPresentationEligible = false,
                () => parent.Opacity = 0,
                () => control.SetAnimationEnabled(false),
                () => window.WindowState = WindowState.Minimized,
                () => control.Arrange(new Rect(0, 0, 0, 0)),
                () => window.Content = null,
            })
            {
                disable();
                var ticks = control.TickCount;
                var invalidations = control.InvalidationCount;
                scheduler.Advance(TimeSpan.FromHours(1));
                scheduler.InvokeRetired();
                control.State = AssistantState.Waiting;
                control.SpeechOutputLevel = 0.8;
                control.TickCount.Should().Be(ticks);
                control.InvalidationCount.Should().Be(invalidations);
                scheduler.PendingCount.Should().Be(0);

                parent.IsVisible = true;
                parent.Opacity = 1;
                control.IsPresentationEligible = true;
                control.SetAnimationEnabled(true);
                window.Content = parent;
                window.WindowState = WindowState.Normal;
                window.Show();
                window.UpdateLayout();
                control.Arrange(new Rect(0, 0, 360, 360));
                control.State = AssistantState.Executing;
                scheduler.Advance(TimeSpan.FromSeconds(1));
            }
            window.Close();
        });
    }

    [Fact]
    public async Task QuietCadenceIsSixTimesSlowerAndStateAndSpeechWakeImmediately()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var scheduler = new RecordingScheduler();
            using var control = new PresenceControl(scheduler)
            {
                IsPresentationEligible = true,
                State = AssistantState.Waiting,
            };
            var window = Show(control);
            scheduler.Advance(TimeSpan.FromSeconds(1));
            var before = control.InvalidationCount;
            scheduler.Advance(TimeSpan.FromMilliseconds(1980));
            var quiet = control.InvalidationCount - before;
            quiet.Should().Be(10);
            before = control.InvalidationCount;
            control.State = AssistantState.Listening;
            control.InvalidationCount.Should().BeGreaterThan(before);
            scheduler.Advance(TimeSpan.FromSeconds(1));
            before = control.InvalidationCount;
            scheduler.Advance(TimeSpan.FromMilliseconds(1980));
            var active = control.InvalidationCount - before;
            active.Should().Be(60);
            active.Should().Be(quiet * 6);
            before = control.InvalidationCount;
            control.IsSpeaking = true;
            control.SpeechOutputLevel = 1;
            control.InvalidationCount.Should().BeGreaterThan(before);
            scheduler.Advance(TimeSpan.FromMilliseconds(165));
            control.CurrentFrame.Scale.Should().BeGreaterThan(1.108);
            control.IsSpeaking = false;
            scheduler.Advance(TimeSpan.FromSeconds(1));
            control.CurrentFrame.Scale.Should().Be(1);
            window.Close();
        });
    }

    [Fact]
    public async Task SeededPrefixDepthOrderAndBrushesRemainStableAcrossMotionDensitySizeAndSpeech()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var scheduler = new RecordingScheduler();
            using var control = new PresenceControl(scheduler) { IsPresentationEligible = true };
            using var other = new PresenceControl(new RecordingScheduler());
            var order = control.DepthOrder.ToArray();
            order.Should().Equal(other.DepthOrder.ToArray());
            control.Particles.Length.Should().Be(300);
            var random = new Random(104729);
            foreach (var particle in control.Particles)
            {
                particle.X.Should().Be(random.NextDouble() * 2 - 1);
                particle.Y.Should().Be(random.NextDouble() * 2 - 1);
                particle.Z.Should().Be(random.NextDouble() * 2 - 1);
                particle.Dx.Should().Be((random.NextDouble() - 0.5) * 0.012);
                particle.Dy.Should().Be((random.NextDouble() - 0.5) * 0.012);
                particle.Size.Should().Be(0.7 + random.NextDouble() * 1.2);
            }
            foreach (var density in DensityLevels)
            {
                var count = PresenceSettings.GetParticleCount(density);
                order.Where(index => index < count).Should().Equal(
                    Enumerable.Range(0, count).OrderBy(index => control.Particles[index].Z));
            }
            var window = Show(control);
            scheduler.Advance(TimeSpan.FromSeconds(1));
            control.PrepareBrushes();
            var builds = control.ResourceBuildCount;
            var brushes = control.Brushes.ToArray();
            for (var frame = 0; frame < 30; frame++)
            {
                scheduler.Advance(TimeSpan.FromMilliseconds(198));
                control.PrepareBrushes();
            }
            control.DotDensityPercent = 25;
            control.DotSizePercent = 200;
            control.MovementSpeedPercent = 200;
            control.IsSpeaking = true;
            control.SpeechOutputLevel = 1;
            scheduler.Advance(TimeSpan.FromSeconds(1));
            control.PrepareBrushes();
            control.ResourceBuildCount.Should().Be(builds);
            control.Brushes.ToArray().Should().Equal(brushes);
            control.DepthOrder.ToArray().Should().Equal(order);

            window.RequestedThemeVariant = ThemeVariant.Light;
            control.PrepareBrushes();
            control.ResourceBuildCount.Should().Be(builds + 1);
            ((ISolidColorBrush)control.Brushes[0]).Color.R.Should().Be((byte)Math.Round(0xB8 * 0.55));
            control.State = AssistantState.Failure;
            scheduler.Advance(TimeSpan.FromSeconds(1));
            control.PrepareBrushes();
            control.ResourceBuildCount.Should().Be(builds + 2);
            ((ISolidColorBrush)control.Brushes[0]).Color.R.Should().Be((byte)Math.Round(0xF4 * 0.55));
            window.Close();
        });
    }

    [Fact]
    public async Task ResumeUsesCurrentStateWithoutCatchupAndReducedMotionFreezesGeometry()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var scheduler = new RecordingScheduler();
            using var control = new PresenceControl(scheduler)
            {
                IsPresentationEligible = true,
                IsMotionEnabled = false,
            };
            var window = Show(control);
            scheduler.Advance(TimeSpan.FromSeconds(1));
            var positions = control.Particles.ToArray().Select(p => (p.X, p.Y)).ToArray();
            var before = control.TickCount;
            scheduler.Advance(TimeSpan.FromHours(1));
            control.TickCount.Should().Be(before);
            window.Hide();
            control.State = AssistantState.Failure;
            control.IsSpeaking = false;
            scheduler.Advance(TimeSpan.FromHours(1));
            window.Show();
            window.UpdateLayout();
            control.CurrentFrame.Color.Red.Should().Be(0xF4);
            control.CurrentFrame.Scale.Should().Be(1);
            scheduler.InvokeRetired();
            scheduler.Advance(TimeSpan.FromSeconds(1));
            control.Particles.ToArray().Select(p => (p.X, p.Y)).Should().Equal(positions);
            control.CurrentFrame.Opacity.Should().Be(1);
            before = control.TickCount;
            scheduler.Advance(TimeSpan.FromHours(1));
            control.TickCount.Should().Be(before);
            control.Dispose();
            var invalidations = control.InvalidationCount;
            scheduler.InvokeRetired();
            control.State = AssistantState.Executing;
            control.TickCount.Should().Be(before);
            control.InvalidationCount.Should().Be(invalidations);
            scheduler.PendingCount.Should().Be(0);
            window.Close();
        });
    }

    [Fact]
    public void LoopFencesCancellationReentrantStopDisposalAndDelayedOrFrequentWakes()
    {
        var scheduler = new RecordingScheduler();
        var eligible = false;
        var elapsedFrames = new List<(TimeSpan Elapsed, bool Resumed)>();
        using var loop = new PresenceRenderLoop(scheduler, () => eligible,
            (elapsed, resumed, _) => elapsedFrames.Add((elapsed, resumed)),
            () => PresenceRenderLoop.ActiveInterval);
        loop.Wake();
        elapsedFrames.Should().BeEmpty();
        eligible = true;
        loop.Wake();
        elapsedFrames.Should().Equal((TimeSpan.Zero, true));
        scheduler.Jump(TimeSpan.FromHours(1));
        scheduler.FireNext();
        elapsedFrames[^1].Should().Be((PresenceRenderLoop.QuietInterval, false));
        for (var wake = 0; wake < 10; wake++)
        {
            scheduler.Jump(TimeSpan.FromMilliseconds(20));
            loop.Wake();
            elapsedFrames[^1].Should().Be((TimeSpan.FromMilliseconds(20), false));
        }
        var count = elapsedFrames.Count;
        eligible = false;
        scheduler.FireNext();
        elapsedFrames.Should().HaveCount(count);
        loop.Stop();
        eligible = true;
        loop.Wake();
        scheduler.InvokeRetired();
        elapsedFrames.Should().HaveCount(count + 1);
        loop.Dispose();
        scheduler.InvokeRetired();
        loop.Wake();
        scheduler.PendingCount.Should().Be(0);

        PresenceRenderLoop? stopping = null;
        using (stopping = new PresenceRenderLoop(scheduler, () => true,
            (_, _, _) => stopping!.Stop(), () => PresenceRenderLoop.ActiveInterval))
        {
            stopping.Wake();
            scheduler.PendingCount.Should().Be(0);
        }

        var fail = false;
        using var failing = new PresenceRenderLoop(scheduler, () => true,
            (_, _, _) =>
            {
                if (fail) { throw new InvalidOperationException("Synthetic frame failure"); }
            }, () => PresenceRenderLoop.ActiveInterval);
        failing.Wake();
        fail = true;
        Action callback = scheduler.FireNext;
        callback.Should().Throw<InvalidOperationException>().WithMessage("Synthetic frame failure");
        scheduler.PendingCount.Should().Be(0);
        scheduler.InvokeRetired();
    }

    [Fact]
    public async Task AuthoritativeRevocationAndPrivacyLossInsideSpeechCallbackFenceInvalidation()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var scheduler = new RecordingScheduler();
            var eligible = true;
            using var control = new PresenceControl(scheduler) { IsPresentationEligible = true };
            control.BindPresentationEligibility(() => eligible);
            var window = Show(control);
            scheduler.Advance(TimeSpan.FromSeconds(1));
            var ticks = control.TickCount;
            var invalidations = control.InvalidationCount;
            eligible = false;
            scheduler.Advance(TimeSpan.FromHours(1));
            control.TickCount.Should().Be(ticks);
            control.InvalidationCount.Should().Be(invalidations);
            scheduler.PendingCount.Should().Be(0);
            eligible = true;
            control.State = AssistantState.Listening;
            control.FrameUpdating += (_, _) => control.IsPresentationEligible = false;
            invalidations = control.InvalidationCount;
            scheduler.Advance(TimeSpan.FromMilliseconds(33));
            control.InvalidationCount.Should().Be(invalidations);
            scheduler.PendingCount.Should().Be(0);
            scheduler.InvokeRetired();
            control.InvalidationCount.Should().Be(invalidations);
            window.Close();
        });
    }

    [Fact]
    public async Task VisibleHideFadeStopsAtZeroAndReshowCancelsRetiredHideFrames()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var scheduler = new RecordingScheduler();
            using var control = new PresenceControl(scheduler) { IsPresentationEligible = true };
            var window = Show(control);
            scheduler.Advance(TimeSpan.FromSeconds(1));
            control.PrepareBrushes();
            var builds = control.ResourceBuildCount;
            control.IsPresenceVisible = false;
            scheduler.Advance(TimeSpan.FromMilliseconds(99));
            control.PrepareBrushes();
            control.ResourceBuildCount.Should().Be(builds + 1);
            control.CurrentFrame.Opacity.Should().BeInRange(0.1, 0.9);
            control.IsPresenceVisible = true;
            scheduler.InvokeRetired();
            scheduler.Advance(TimeSpan.FromSeconds(1));
            control.CurrentFrame.Opacity.Should().Be(1);
            control.IsPresenceVisible = false;
            scheduler.Advance(TimeSpan.FromSeconds(1));
            control.CurrentFrame.Opacity.Should().Be(0);
            var ticks = control.TickCount;
            var invalidations = control.InvalidationCount;
            scheduler.Advance(TimeSpan.FromHours(1));
            scheduler.InvokeRetired();
            control.TickCount.Should().Be(ticks);
            control.InvalidationCount.Should().Be(invalidations);
            scheduler.PendingCount.Should().Be(0);
            window.Close();
        });
    }

    [Fact]
    public async Task ActualDrawingUsesBoundedSeededPrefixAndCachedBrushesAndRejectsUnknownEligibility()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var scheduler = new RecordingScheduler();
            using var control = new PresenceControl(scheduler);
            var window = Show(control);
            control.TickCount.Should().Be(0);
            control.InvalidationCount.Should().Be(0);
            var hidden = new DrawingGroup();
            using (var context = hidden.Open())
            {
                control.Render(context);
            }
            hidden.Children.Should().BeEmpty();
            control.IsPresentationEligible = true;
            scheduler.Advance(TimeSpan.FromSeconds(1));
            foreach (var density in DensityLevels)
            {
                control.DotDensityPercent = density;
                var drawing = new DrawingGroup();
                using (var context = drawing.Open())
                {
                    control.Render(context);
                    control.Render(context);
                }
                drawing.Children.Should().HaveCount(PresenceSettings.GetParticleCount(density) * 2);
                control.ResourceBuildCount.Should().Be(1);
            }
            control.State = AssistantState.Hidden;
            var invalidations = control.InvalidationCount;
            scheduler.Advance(TimeSpan.FromHours(1));
            scheduler.InvokeRetired();
            control.InvalidationCount.Should().Be(invalidations);
            control.TickCount.Should().BeGreaterThan(0);
            scheduler.PendingCount.Should().Be(0);
            window.Close();
        });
    }

    [Fact]
    public async Task SpeechWorkSamplesAuthoritativeStartWithoutSyntheticScaleOrStaticInvalidations()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var scheduler = new RecordingScheduler();
            using var control = new PresenceControl(scheduler)
            {
                IsPresentationEligible = true,
                State = AssistantState.Failure,
            };
            var window = Show(control);
            scheduler.Advance(TimeSpan.FromSeconds(1));
            scheduler.PendingCount.Should().Be(0);
            var playing = false;
            var samples = 0;
            control.FrameUpdating += (_, _) =>
            {
                samples++;
                control.IsSpeaking = playing;
                control.SpeechOutputLevel = playing ? 1 : 0;
            };
            control.IsSpeechWorkActive = true;
            var ticks = control.TickCount;
            var invalidations = control.InvalidationCount;
            scheduler.Advance(TimeSpan.FromMilliseconds(160));
            control.TickCount.Should().Be(ticks + 10);
            samples.Should().Be(11);
            control.CurrentFrame.Scale.Should().Be(1);
            control.InvalidationCount.Should().Be(invalidations);
            playing = true;
            scheduler.Advance(TimeSpan.FromMilliseconds(16));
            control.IsSpeaking.Should().BeTrue();
            control.InvalidationCount.Should().BeGreaterThan(invalidations);
            scheduler.Advance(TimeSpan.FromMilliseconds(160));
            control.CurrentFrame.Scale.Should().BeGreaterThan(1.108);
            playing = false;
            control.IsSpeechWorkActive = false;
            scheduler.Advance(TimeSpan.FromSeconds(1));
            scheduler.PendingCount.Should().Be(0);
            control.CurrentFrame.Scale.Should().Be(1);
            window.Close();
        });
    }

    private static Window Show(PresenceControl control)
    {
        var window = new Window
        {
            Width = 360, Height = 360, Content = control, RequestedThemeVariant = ThemeVariant.Dark,
        };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private sealed class RecordingScheduler : IPresenceFrameScheduler
    {
        private readonly List<Request> requests = [];
        public TimeSpan Now { get; private set; }
        public int PendingCount => requests.Count(request => !request.Cancelled);

        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            (delay == PresenceRenderLoop.ActiveInterval || delay == PresenceRenderLoop.QuietInterval
                || delay == PresenceRenderLoop.SpeechInterval)
                .Should().BeTrue();
            var request = new Request(Now + delay, callback);
            requests.Add(request);
            return request;
        }

        public void Jump(TimeSpan elapsed) => Now += elapsed;

        public void FireNext()
        {
            var request = requests.First(request => !request.Cancelled);
            request.Dispose();
            request.Callback();
        }

        public void Advance(TimeSpan elapsed)
        {
            var target = Now + elapsed;
            while (requests.FirstOrDefault(request => !request.Cancelled && request.Due <= target) is { } request)
            {
                Now = request.Due;
                request.Dispose();
                request.Callback();
            }
            Now = target;
        }

        public void InvokeRetired()
        {
            foreach (var request in requests.Where(request => request.Cancelled).ToArray())
            {
                request.Callback();
            }
        }

        private sealed class Request(TimeSpan due, Action callback) : IDisposable
        {
            public TimeSpan Due { get; } = due;
            public Action Callback { get; } = callback;
            public bool Cancelled { get; private set; }
            public void Dispose() => Cancelled = true;
        }
    }
}
