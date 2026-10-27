using System.ComponentModel;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Visuals;
using Kora.Controls;
using Kora.Core;
using Kora.Core.Presentation;
using Kora.Windows.Presentation;

namespace Kora.Windows.IntegrationTests;

public sealed partial class PresenceRendererTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiveWindowsMotionReachesRealParticlesWithoutChangingSettingsStateFocusOrLabel(bool initiallyEnabled)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var native = new MotionNative { Enabled = initiallyEnabled };
            var dispatch = new MotionDispatch();
            var scheduler = new RecordingScheduler();
            using var control = new PresenceControl(scheduler)
            {
                IsPresentationEligible = true,
                State = AssistantState.Executing,
                MovementSpeedPercent = 25,
                DotSizePercent = 150,
                DotDensityPercent = 200,
                IsSpeechScalingEnabled = true,
            };
            AutomationProperties.SetName(control, "Kora executing");
            var focus = new Button { Content = "Separate review" };
            var window = new Window
            {
                Width = 360, Height = 360, RequestedThemeVariant = ThemeVariant.Light,
                Content = new Grid { Children = { control, focus } },
            };
            using var binding = new PresenceMotionBinding(control, new WindowsMotionPreferenceSource(native),
                dispatch.Post, exception => throw new InvalidOperationException("Unexpected motion failure.", exception));
            binding.SetEligible(true);
            native.Operations.Should().Equal("subscribe", "read");
            control.IsMotionEnabled.Should().Be(initiallyEnabled);
            window.Show();
            window.UpdateLayout();
            focus.Focus().Should().BeTrue();
            scheduler.Advance(TimeSpan.FromSeconds(1));
            var positions = Positions(control);
            scheduler.Advance(TimeSpan.FromMilliseconds(198));
            Positions(control).SequenceEqual(positions).Should().Be(!initiallyEnabled);

            native.Enabled = false;
            native.Notify();
            dispatch.Drain();
            positions = Positions(control);
            scheduler.Advance(TimeSpan.FromSeconds(1));
            Positions(control).Should().Equal(positions);
            scheduler.PendingCount.Should().Be(0);
            control.CanInteract.Should().BeTrue("reduced motion is not a new activation or visibility policy");
            control.IsSpeaking = true;
            control.SpeechOutputLevel = 1;
            scheduler.Advance(TimeSpan.FromMilliseconds(165));
            control.CurrentFrame.Scale.Should().BeGreaterThan(1.108);
            Positions(control).Should().Equal(positions);
            control.IsSpeaking = false;
            scheduler.Advance(TimeSpan.FromSeconds(1));
            control.MovementSpeedPercent = 175;
            native.Enabled = true;
            native.Notify();
            dispatch.Drain();
            scheduler.Advance(TimeSpan.FromMilliseconds(198));
            Positions(control).Should().NotEqual(positions);
            control.State.Should().Be(AssistantState.Executing);
            control.MovementSpeedPercent.Should().Be(175);
            control.DotSizePercent.Should().Be(150);
            control.DotDensityPercent.Should().Be(200);
            control.IsSpeechScalingEnabled.Should().BeTrue();
            control.ActualThemeVariant.Should().Be(ThemeVariant.Light);
            AutomationProperties.GetName(control).Should().Be("Kora executing");
            focus.IsFocused.Should().BeTrue();
            native.ActiveSubscriptions.Should().Be(1);
            binding.Dispose();
            native.ActiveSubscriptions.Should().Be(0);
            window.Close();
        });
    }

    [Fact]
    public async Task RapidNotificationsCoalesceCurrentReadAndOldGenerationsCannotOverwriteRestoredOwner()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var native = new MotionNative { Enabled = true };
            var dispatch = new MotionDispatch();
            var scheduler = new RecordingScheduler();
            using var control = new PresenceControl(scheduler)
            {
                IsPresentationEligible = true, State = AssistantState.Waiting,
            };
            using var binding = new PresenceMotionBinding(control, new WindowsMotionPreferenceSource(native),
                dispatch.Post, exception => throw new InvalidOperationException("Unexpected motion failure.", exception));
            binding.SetEligible(true);
            var window = Show(control);
            scheduler.Advance(TimeSpan.FromSeconds(1));
            native.Enabled = false;
            native.Notify();
            native.Enabled = true;
            native.Notify();
            native.Enabled = false;
            native.Notify();
            native.Reads.Should().Be(1, "notifications never read or mutate the UI on the platform thread");
            dispatch.Drain(reverse: true);
            native.Reads.Should().Be(2);
            control.IsMotionEnabled.Should().BeFalse();

            native.Enabled = true;
            native.Notify();
            control.IsPresentationEligible = false;
            binding.SetEligible(false);
            native.ActiveSubscriptions.Should().Be(0);
            control.CanInteract.Should().BeFalse();
            var positions = Positions(control);
            var invalidations = control.InvalidationCount;
            native.InvokeRetired();
            dispatch.Drain();
            scheduler.Advance(TimeSpan.FromHours(1));
            scheduler.InvokeRetired();
            Positions(control).Should().Equal(positions);
            control.InvalidationCount.Should().Be(invalidations);
            native.Reads.Should().Be(2);
            var drawing = new DrawingGroup();
            using (var context = drawing.Open()) { control.Render(context); }
            drawing.Children.Should().BeEmpty("private withdrawal must not draw even a frozen presence");

            native.Enabled = true;
            control.IsPresentationEligible = true;
            binding.SetEligible(true);
            native.Reads.Should().Be(3);
            control.IsMotionEnabled.Should().BeTrue();
            native.Enabled = false;
            native.InvokeRetired();
            dispatch.Drain();
            control.IsMotionEnabled.Should().BeTrue("retired callbacks cannot impersonate the current subscription");
            native.Notify();
            binding.Dispose();
            binding.Dispose();
            native.InvokeRetired();
            dispatch.Drain();
            scheduler.Advance(TimeSpan.FromSeconds(1));
            control.IsMotionEnabled.Should().BeFalse();
            native.Reads.Should().Be(3);
            native.Subscriptions.Should().Be(2);
            native.Disposals.Should().Be(2);
            native.ActiveSubscriptions.Should().Be(0);
            var ticks = control.TickCount;
            var settledPositions = Positions(control);
            scheduler.Advance(TimeSpan.FromHours(1));
            control.TickCount.Should().Be(ticks);
            Positions(control).Should().Equal(settledPositions);
            scheduler.PendingCount.Should().Be(0);
            window.Close();
        });
    }

    [Fact]
    public async Task NativeParticleConsumerUsesTheSameLiveMotionProjectionAndCurrentAppearanceSettings()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var native = new MotionNative { Enabled = false };
            var dispatch = new MotionDispatch();
            using var control = new PresenceControl(new RecordingScheduler())
            {
                State = AssistantState.Failure, DotDensityPercent = 200, DotSizePercent = 150,
                MovementSpeedPercent = 25, IsSpeechScalingEnabled = true, SpeechScaleAmountPercent = 175,
            };
            using var binding = new PresenceMotionBinding(control, new WindowsMotionPreferenceSource(native),
                dispatch.Post, exception => throw new InvalidOperationException("Unexpected motion failure.", exception));
            binding.SetEligible(true);
            var inputs = control.CaptureRenderInputs(true, 360, 360, 1.5);
            inputs.Validate();
            inputs.ReducedMotion.Should().BeTrue();
            var field = new PresenceParticleField();
            var before = new PresenceParticle[300];
            var after = new PresenceParticle[300];
            field.Project(before, inputs.Density, inputs.DotSize, inputs.Width, inputs.Height, 1);
            field.Advance(inputs.State, TimeSpan.FromMilliseconds(198), inputs.Density, inputs.MovementSpeed, inputs.ReducedMotion)
                .Should().BeFalse();
            field.Project(after, inputs.Density, inputs.DotSize, inputs.Width, inputs.Height, 1);
            after.Should().Equal(before);
            native.Enabled = true;
            native.Notify();
            dispatch.Drain();
            var moving = control.CaptureRenderInputs(true, 360, 360, 1.5);
            moving.Should().Be(inputs with { ReducedMotion = false });
            field.Advance(moving.State, TimeSpan.FromMilliseconds(198), moving.Density, moving.MovementSpeed, moving.ReducedMotion)
                .Should().BeTrue();
            field.Project(after, moving.Density, moving.DotSize, moving.Width, moving.Height, 1);
            after.Should().NotEqual(before);
            native.Enabled = false;
            native.Notify();
            dispatch.Drain();
            inputs = control.CaptureRenderInputs(true, 360, 360, 1.5);
            field.Advance(inputs.State, TimeSpan.FromHours(1), inputs.Density, inputs.MovementSpeed, inputs.ReducedMotion)
                .Should().BeFalse();
            field.Project(before, inputs.Density, inputs.DotSize, inputs.Width, inputs.Height, 1);
            before.Should().Equal(after, "motion freezes in place rather than substituting a static arrangement");
        });
    }

    [Fact]
    public async Task ShowRefreshReadsCurrentPreferenceEvenWithoutANotification()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var native = new MotionNative { Enabled = false };
            using var control = new PresenceControl(new RecordingScheduler());
            using var binding = new PresenceMotionBinding(control, new WindowsMotionPreferenceSource(native),
                action => action(), exception => throw new InvalidOperationException("Unexpected motion failure.", exception));
            binding.SetEligible(false);
            native.Reads.Should().Be(0);
            native.ActiveSubscriptions.Should().Be(0);
            control.IsMotionEnabled.Should().BeFalse();
            binding.Refresh();
            binding.SetEligible(true);
            native.Reads.Should().Be(1);
            control.IsMotionEnabled.Should().BeFalse();
            native.Enabled = true;
            binding.Refresh();
            native.Reads.Should().Be(2);
            control.IsMotionEnabled.Should().BeTrue();
            binding.SetEligible(true);
            native.Subscriptions.Should().Be(1);
            binding.Dispose();
            binding.Refresh();
            binding.SetEligible(true);
            native.Reads.Should().Be(2);
            native.ActiveSubscriptions.Should().Be(0);
        });
    }

    [Fact]
    public async Task NotificationDuringSubscriptionOrFirstReadCannotPublishAnOlderMotionState()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var native = new MotionNative { Enabled = true };
            var dispatch = new MotionDispatch();
            using var control = new PresenceControl(new RecordingScheduler());
            native.OnSubscribe = () => native.Notify();
            native.OnRead = () =>
            {
                native.Enabled = false;
                native.Notify();
            };
            using var binding = new PresenceMotionBinding(control, new WindowsMotionPreferenceSource(native),
                dispatch.Post, exception => throw new InvalidOperationException("Unexpected motion failure.", exception));
            binding.SetEligible(true);
            control.IsMotionEnabled.Should().BeFalse("the first enabled read was invalidated before publication");
            native.OnRead = null;
            dispatch.Drain();
            control.IsMotionEnabled.Should().BeFalse();
            native.Reads.Should().Be(2);
            native.ActiveSubscriptions.Should().Be(1);
        });
    }

    [Theory]
    [InlineData("subscribe")]
    [InlineData("first-read")]
    [InlineData("notification-read")]
    public async Task NativeReadAndSubscriptionFailuresAreExplicitRetireResourcesAndNeverBecomeReady(string failureStage)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var native = new MotionNative { Enabled = true };
            var dispatch = new MotionDispatch();
            var failures = new List<Exception>();
            using var control = new PresenceControl(new RecordingScheduler()) { IsPresentationEligible = true };
            if (string.Equals(failureStage, "subscribe", StringComparison.Ordinal)) { native.SubscriptionFailure = new Win32Exception(5); }
            if (string.Equals(failureStage, "first-read", StringComparison.Ordinal)) { native.ReadFailure = new Win32Exception(5); }
            using var binding = new PresenceMotionBinding(control, new WindowsMotionPreferenceSource(native),
                dispatch.Post, failures.Add);
            binding.SetEligible(true);
            if (string.Equals(failureStage, "notification-read", StringComparison.Ordinal))
            {
                control.IsMotionEnabled.Should().BeTrue();
                native.ReadFailure = new Win32Exception(5);
                native.Notify();
                dispatch.Drain();
            }
            failures.Should().ContainSingle().Which.Should().BeOfType<InvalidOperationException>()
                .Which.InnerException.Should().BeSameAs(native.SubscriptionFailure ?? native.ReadFailure);
            control.IsMotionEnabled.Should().BeFalse();
            native.ActiveSubscriptions.Should().Be(0);
            native.ReadFailure = null;
            native.SubscriptionFailure = null;
            native.InvokeRetired();
            dispatch.Drain();
            binding.SetEligible(false);
            binding.SetEligible(true);
            failures.Should().ContainSingle();
            control.IsMotionEnabled.Should().BeFalse("recovery requires a fresh owner, not silent retries");
            native.ActiveSubscriptions.Should().Be(0);
        });
    }

    [Fact]
    public async Task PlatformThreadNotificationsApplyOnlyAfterUiDispatchAndNeverAfterDisposal()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var native = new MotionNative { Enabled = true };
            using var control = new PresenceControl(new RecordingScheduler());
            var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var binding = new PresenceMotionBinding(control, new WindowsMotionPreferenceSource(native),
                action => Dispatcher.UIThread.Post(() =>
                {
                    Dispatcher.UIThread.CheckAccess().Should().BeTrue();
                    action();
                    applied.SetResult();
                }), exception => throw new InvalidOperationException("Unexpected motion failure.", exception));
            binding.SetEligible(true);
            native.Enabled = false;
            await Task.Run(native.Notify, TestContext.Current.CancellationToken);
            await applied.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            control.IsMotionEnabled.Should().BeFalse();
            native.ReadThreads.Should().OnlyContain(thread => thread == Environment.CurrentManagedThreadId);
            binding.Dispose();
            var reads = native.Reads;
            await Task.Run(native.InvokeRetired, TestContext.Current.CancellationToken);
            native.Reads.Should().Be(reads);
            native.ActiveSubscriptions.Should().Be(0);
        });
    }

    private static (double X, double Y)[] Positions(PresenceControl control) =>
        control.Particles.ToArray().Select(particle => (particle.X, particle.Y)).ToArray();

    private sealed class MotionDispatch
    {
        private readonly List<Action> actions = [];
        public void Post(Action action) => actions.Add(action);
        public void Drain(bool reverse = false)
        {
            var queued = actions.ToArray();
            actions.Clear();
            if (reverse) { Array.Reverse(queued); }
            foreach (var action in queued) { action(); }
        }
    }

    private sealed class MotionNative : IWindowsMotionPreferenceNative
    {
        private Action? changed;
        private readonly List<Action> retired = [];
        public bool Enabled { get; set; }
        public Exception? ReadFailure { get; set; }
        public Exception? SubscriptionFailure { get; set; }
        public Action? OnSubscribe { get; set; }
        public Action? OnRead { get; set; }
        public int Reads { get; private set; }
        public int Subscriptions { get; private set; }
        public int Disposals { get; private set; }
        public int ActiveSubscriptions { get; private set; }
        public List<string> Operations { get; } = [];
        public List<int> ReadThreads { get; } = [];

        public bool IsAnimationEnabled()
        {
            Reads++;
            Operations.Add("read");
            ReadThreads.Add(Environment.CurrentManagedThreadId);
            if (ReadFailure is { } failure) { throw failure; }
            var value = Enabled;
            OnRead?.Invoke();
            return value;
        }

        public IDisposable Subscribe(Action callback)
        {
            Operations.Add("subscribe");
            if (SubscriptionFailure is { } failure) { throw failure; }
            changed = callback;
            Subscriptions++;
            ActiveSubscriptions++;
            OnSubscribe?.Invoke();
            return new MotionSubscription(() =>
            {
                Disposals++;
                ActiveSubscriptions--;
                retired.Add(callback);
                changed = null;
            });
        }

        public void Notify() => changed?.Invoke();
        public void InvokeRetired()
        {
            foreach (var callback in retired) { callback(); }
        }

        private sealed class MotionSubscription(Action dispose) : IDisposable
        {
            private bool disposed;
            public void Dispose()
            {
                if (disposed) { return; }
                disposed = true;
                dispose();
            }
        }
    }
}
