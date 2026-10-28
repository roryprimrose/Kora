using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Visuals;
using Kora.Controls;
using Kora.Core;
using Kora.Core.Presentation;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Presentation;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class PresenceStateAccessibilityTests
{
    private static readonly bool[] Renderers = [false, true];

    [Theory]
    [InlineData(AssistantState.Hidden, "Kora presence: unavailable")]
    [InlineData(AssistantState.Listening, "Kora presence: listening; speech playback inactive")]
    [InlineData(AssistantState.Calculating, "Kora presence: calculating; speech playback inactive")]
    [InlineData(AssistantState.Waiting, "Kora presence: waiting; speech playback inactive")]
    [InlineData(AssistantState.Executing, "Kora presence: executing; speech playback inactive")]
    [InlineData(AssistantState.Success, "Kora presence: success; speech playback inactive")]
    [InlineData(AssistantState.Failure, "Kora presence: failure; speech playback inactive")]
    [InlineData(AssistantState.Information, "Kora presence: information; speech playback inactive")]
    public async Task BothAttachedPresentersExposeEveryTypedStateWithOneSemanticUpdate(
        AssistantState state, string expected)
    {
        await HeadlessSession.RunAsync(() =>
        {
            foreach (var native in Renderers)
            {
                using var fixture = new Fixture(native);
                fixture.SetState(AssistantState.Listening);
                fixture.Changes.Clear();
                fixture.SetState(state == AssistantState.Listening ? AssistantState.Waiting : state);
                if (state == AssistantState.Listening)
                {
                    fixture.Changes.Clear();
                    fixture.SetState(state);
                }
                fixture.Peer.GetName().Should().Be(expected);
                fixture.Peer.GetAutomationControlType().Should().Be(AutomationControlType.Text);
                if (state == AssistantState.Hidden)
                {
                    fixture.AssertUnavailable();
                }
                else
                {
                    fixture.Changes.Should().ContainSingle();
                    fixture.Changes[0].NewValue.Should().Be(expected);
                    fixture.Peer.GetLiveSetting().Should().Be(AutomationLiveSetting.Polite);
                    fixture.Peer.IsControlElement().Should().BeTrue();
                    fixture.Peer.IsContentElement().Should().BeTrue();
                }
                fixture.Failures.Should().BeEmpty();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlaybackIsSeparateFromSynthesisAndTaskStateAndFramesDoNotFlood(bool native)
    {
        await HeadlessSession.RunAsync(() =>
        {
            using var fixture = new Fixture(native);
            foreach (var state in new[] { AssistantState.Calculating, AssistantState.Waiting, AssistantState.Executing })
            {
                fixture.SetState(state);
                fixture.Changes.Clear();
                if (fixture.Default is { } control)
                {
                    control.IsSpeechWorkActive = true;
                    control.IsMotionEnabled = true;
                }
                fixture.Refresh();
                fixture.Peer.GetName().Should().Be($"Kora presence: {state.ToString().ToLowerInvariant()}; speech playback inactive");
                fixture.Changes.Should().BeEmpty();
                fixture.SetSpeaking(true);
                fixture.Changes.Should().ContainSingle();
                fixture.Peer.GetName().Should().Be($"Kora presence: {state.ToString().ToLowerInvariant()}; speech playback active");
                for (var index = 0; index < 20; index++)
                {
                    fixture.SetLevel(index / 20d);
                    fixture.SetState(state);
                    fixture.Scheduler.Tick();
                }
                fixture.Changes.Should().ContainSingle();
                fixture.SetSpeaking(false);
                fixture.Changes.Should().HaveCount(2);
                fixture.Peer.GetName().Should().EndWith("speech playback inactive");
            }
            fixture.SampleCalls.Should().Be(0);
            fixture.Failures.Should().BeEmpty();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VisibilityEligibilityAndLifecycleRetireStateAndSuppressLateCallbacks(bool native)
    {
        await HeadlessSession.RunAsync(() =>
        {
            using var fixture = new Fixture(native);
            foreach (var retire in new Action[]
            {
                () => fixture.Control.IsVisible = false,
                () => fixture.Parent.IsVisible = false,
                () => fixture.Control.IsEnabled = false,
                () => fixture.Parent.IsEnabled = false,
                () => fixture.Window.Hide(),
                () => fixture.Window.WindowState = WindowState.Minimized,
                () => fixture.Window.Opacity = 0,
                () => fixture.Parent.Opacity = 0,
                () => fixture.Control.Opacity = 0,
                () => fixture.Control.Arrange(new Rect(0, 0, 0, 0)),
                () => fixture.SetDisplay(false),
                () => fixture.SetEligibility(false),
                () => fixture.Parent.Child = null,
            })
            {
                fixture.SetState(AssistantState.Executing);
                fixture.Changes.Clear();
                retire();
                fixture.AssertUnavailable();
                fixture.SetState(AssistantState.Success);
                fixture.SetLevel(1);
                fixture.Scheduler.InvokeRetired();
                Dispatcher.UIThread.RunJobs();
                fixture.AssertUnavailable();
                fixture.Restore();
                fixture.Peer.GetName().Should().Be("Kora presence: success; speech playback inactive");
            }
            fixture.Failures.Should().BeEmpty();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadsRecheckAuthorityAndDoNotResurrectAnEarlierAttachment(bool native)
    {
        await HeadlessSession.RunAsync(() =>
        {
            using var fixture = new Fixture(native);
            fixture.SetState(AssistantState.Executing);
            fixture.Changes.Clear();
            fixture.SourceAvailable = false;
            fixture.Peer.GetName().Should().Be(PresenceStateAutomationPeer.UnavailableName);
            fixture.SetEligibility(false);
            fixture.AssertUnavailable();
            fixture.Parent.Child = null;
            fixture.SetState(AssistantState.Failure);
            fixture.Parent.Child = fixture.Control;
            fixture.Window.UpdateLayout();
            fixture.Scheduler.InvokeRetired();
            Dispatcher.UIThread.RunJobs();
            fixture.AssertUnavailable();
            fixture.SetEligibility(true);
            fixture.Peer.GetName().Should().Be("Kora presence: failure; speech playback inactive");
            fixture.Changes.Should().ContainSingle();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalPermanentlyRetiresPeerEvenWhenInputsAndCallbacksArrive(bool native)
    {
        await HeadlessSession.RunAsync(() =>
        {
            using var fixture = new Fixture(native);
            fixture.SetState(AssistantState.Executing);
            fixture.Changes.Clear();
            ((IDisposable)fixture.Control).Dispose();
            fixture.AssertUnavailable();
            fixture.SetState(AssistantState.Success);
            fixture.SetSpeaking(true);
            fixture.Refresh();
            fixture.Scheduler.InvokeRetired();
            Dispatcher.UIThread.RunJobs();
            fixture.AssertUnavailable();
            fixture.SampleCalls.Should().Be(0);
        });
    }

    [Fact]
    public async Task NativeSuspensionFailureAndUnavailableSourceFailClosedWithoutNativeResources()
    {
        await HeadlessSession.RunAsync(() =>
        {
            using var fixture = new Fixture(true);
            fixture.SetState(AssistantState.Executing);
            fixture.Changes.Clear();
            fixture.Native!.Suspend();
            fixture.AssertUnavailable();
            Dispatcher.UIThread.RunJobs();
            fixture.AssertUnavailable();
            fixture.Refresh();
            fixture.Peer.GetName().Should().Contain("executing");
            fixture.Changes.Clear();
            fixture.Targets[^1].FailMove = true;
            fixture.Refresh();
            fixture.Failures.Should().ContainSingle().Which.Message.Should().Be("Synthetic native failure.");
            fixture.AssertUnavailable();
            fixture.Targets[^1].FailMove = false;
            fixture.SetState(AssistantState.Success);
            fixture.Refresh();
            Dispatcher.UIThread.RunJobs();
            fixture.AssertUnavailable();
            fixture.SampleCalls.Should().Be(0);
        });
    }

    [Fact]
    public async Task NativePeerReadsNeverCreateResourcesOrCaptureInputsBeforeAttachmentOrAfterStartupFailure()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var captures = 0;
            var creations = 0;
            var samples = 0;
            var failures = new List<Exception>();
            using var control = new NativePresenceHost(
                () =>
                {
                    captures++;
                    throw new InvalidOperationException("Ineligible capture must not run.");
                },
                () => samples++, failures.Add, NullLogger<PresenceRenderWorker>.Instance,
                _ =>
                {
                    creations++;
                    throw new InvalidOperationException("Synthetic startup failure.");
                });
            var peer = ControlAutomationPeer.CreatePeerForElement(control)!;
            var changes = new List<AutomationPropertyChangedEventArgs>();
            peer.PropertyChanged += (_, args) =>
            {
                if (args.Property == AutomationElementIdentifiers.NameProperty) { changes.Add(args); }
            };
            for (var index = 0; index < 10; index++)
            {
                peer.GetName().Should().Be(PresenceStateAutomationPeer.UnavailableName);
            }
            captures.Should().Be(0);
            creations.Should().Be(0);
            var window = new Window { Width = 360, Height = 360, Content = control };
            try
            {
                window.Show();
                window.UpdateLayout();
                failures.Should().ContainSingle().Which.Message.Should().Be("Synthetic startup failure.");
                for (var index = 0; index < 10; index++)
                {
                    control.Refresh(true);
                    peer.GetName().Should().Be(PresenceStateAutomationPeer.UnavailableName);
                    peer.GetLiveSetting().Should().Be(AutomationLiveSetting.Off);
                }
                captures.Should().Be(0);
                samples.Should().Be(0);
                creations.Should().Be(1);
                changes.Should().BeEmpty();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownStateIsExplicitlyUnavailableAndNeverReportedAsIdleOrSuccess(bool native)
    {
        await HeadlessSession.RunAsync(() =>
        {
            using var fixture = new Fixture(native);
            fixture.Changes.Clear();
            fixture.SetState((AssistantState)int.MaxValue);
            fixture.AssertUnavailable();
            if (native)
            {
                fixture.Failures.Should().ContainSingle().Which.Should().BeOfType<InvalidDataException>();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AccessibleReadsAndSemanticUpdatesDoNotSamplePlaybackFocusOrChangePresentation(bool native)
    {
        await HeadlessSession.RunAsync(() =>
        {
            using var fixture = new Fixture(native);
            var separateReview = new Button { Content = "Separate review" };
            fixture.Window.Content = null;
            fixture.Window.UpdateLayout();
            fixture.Window.Content = new Grid { Children = { fixture.Parent, separateReview } };
            fixture.Window.UpdateLayout();
            separateReview.Focus().Should().BeTrue();
            fixture.SetState(AssistantState.Executing);
            var creations = fixture.Targets.Count;
            var moves = fixture.Targets.Sum(target => target.Moves);
            var samples = fixture.SampleCalls;
            var updates = fixture.FrameUpdates;
            var requests = fixture.Scheduler.Requests.Count;
            for (var index = 0; index < 20; index++)
            {
                fixture.Peer.GetName().Should().Be("Kora presence: executing; speech playback inactive");
                fixture.Peer.GetLiveSetting().Should().Be(AutomationLiveSetting.Polite);
            }
            fixture.Targets.Count.Should().Be(creations);
            fixture.Targets.Sum(target => target.Moves).Should().Be(moves);
            fixture.FrameUpdates.Should().Be(updates);
            fixture.Scheduler.Requests.Should().HaveCount(requests);
            fixture.SampleCalls.Should().Be(samples).And.Be(0);
            fixture.SetState(AssistantState.Waiting);
            fixture.SampleCalls.Should().Be(0);
            fixture.Targets.Count.Should().Be(creations);
            separateReview.IsFocused.Should().BeTrue();
            fixture.Control.Focusable.Should().BeFalse();
            fixture.Control.IsHitTestVisible.Should().BeTrue();
            fixture.Failures.Should().BeEmpty();
        });
    }

    private sealed class Fixture : IDisposable
    {
        private PresenceRenderInputs inputs = new(AssistantState.Information, false, 0, true,
            100, 100, 100, 100, false, true, true, 360, 360, 1);
        internal Fixture(bool native)
        {
            if (native)
            {
                Native = new NativePresenceHost(() => SourceAvailable ? inputs : null,
                    () => SampleCalls++, Failures.Add, NullLogger<PresenceRenderWorker>.Instance,
                    _ =>
                    {
                        var target = new Target();
                        Targets.Add(target);
                        return target;
                    });
                Control = Native;
            }
            else
            {
                Default = new PresenceControl(Scheduler) { IsPresentationEligible = true, IsMotionEnabled = false };
                Default.BindPresentationEligibility(() => SourceAvailable);
                Default.FrameUpdating += (_, _) => FrameUpdates++;
                Control = Default;
            }
            Peer = ControlAutomationPeer.CreatePeerForElement(Control)!;
            Peer.GetName().Should().Be(PresenceStateAutomationPeer.UnavailableName);
            Parent = new Border { Child = Control };
            Window = new Window { Width = 360, Height = 360, Content = Parent };
            Window.Show();
            Window.UpdateLayout();
            Refresh();
            Peer.PropertyChanged += (_, args) =>
            {
                Dispatcher.UIThread.CheckAccess().Should().BeTrue();
                if (args.Property == AutomationElementIdentifiers.NameProperty) { Changes.Add(args); }
            };
        }

        internal Control Control { get; }
        internal PresenceControl? Default { get; }
        internal NativePresenceHost? Native { get; }
        internal Border Parent { get; }
        internal Window Window { get; }
        internal AutomationPeer Peer { get; }
        internal Scheduler Scheduler { get; } = new();
        internal List<AutomationPropertyChangedEventArgs> Changes { get; } = [];
        internal List<Exception> Failures { get; } = [];
        internal List<Target> Targets { get; } = [];
        internal bool SourceAvailable { get; set; } = true;
        internal int SampleCalls { get; private set; }
        internal int FrameUpdates { get; private set; }

        internal void Refresh() => Native?.Refresh(false);
        internal void SetState(AssistantState state)
        {
            inputs = inputs with { State = state };
            if (Default is { } control) { control.State = state; }
            Refresh();
        }
        internal void SetSpeaking(bool speaking)
        {
            inputs = inputs with { IsSpeaking = speaking };
            if (Default is { } control) { control.IsSpeaking = speaking; }
            Refresh();
        }
        internal void SetLevel(double level)
        {
            inputs = inputs with { SpeechOutputLevel = level };
            if (Default is { } control) { control.SpeechOutputLevel = level; }
            Refresh();
        }
        internal void SetDisplay(bool visible)
        {
            inputs = inputs with { IsPresenceVisible = visible };
            if (Default is { } control) { control.IsPresenceVisible = visible; }
            Refresh();
        }
        internal void SetEligibility(bool eligible)
        {
            SourceAvailable = eligible;
            if (Default is { } control) { control.IsPresentationEligible = eligible; }
            Refresh();
        }
        internal void AssertUnavailable()
        {
            Peer.GetName().Should().Be(PresenceStateAutomationPeer.UnavailableName);
            Peer.GetLiveSetting().Should().Be(AutomationLiveSetting.Off);
            Peer.IsControlElement().Should().BeFalse();
            Peer.IsContentElement().Should().BeFalse();
            Changes.Should().BeEmpty();
        }
        internal void Restore()
        {
            Control.IsVisible = true;
            Control.IsEnabled = true;
            Control.Opacity = 1;
            Parent.IsVisible = true;
            Parent.IsEnabled = true;
            Parent.Opacity = 1;
            Parent.Child = Control;
            SetDisplay(true);
            SetEligibility(true);
            Window.WindowState = WindowState.Normal;
            Window.Opacity = 1;
            Window.Show();
            Window.UpdateLayout();
            Control.Arrange(new Rect(0, 0, 360, 360));
            Refresh();
        }
        public void Dispose()
        {
            ((IDisposable)Control).Dispose();
            Window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private sealed class Target : NativePresenceHost.IRenderTarget
    {
        public nint Handle => 0;
        public long FramesPresented { get; private set; }
        internal int Moves { get; private set; }
        internal bool FailMove { get; set; }
        public void Move(Control control, TopLevel host)
        {
            Moves++;
            if (FailMove) { throw new InvalidOperationException("Synthetic native failure."); }
        }
        public void Draw(ReadOnlySpan<PresenceParticle> particles, double width, double height, double scaling,
            byte red, byte green, byte blue, double opacity) { }
        public void Present() => FramesPresented++;
        public void Hide() { }
        public void ReleaseDrawingResources() { }
        public void Dispose() { }
    }

    private sealed class Scheduler : IPresenceFrameScheduler
    {
        internal List<Request> Requests { get; } = [];
        public TimeSpan Now { get; private set; }
        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            var request = new Request(callback);
            Requests.Add(request);
            return request;
        }
        internal void Tick()
        {
            Now += TimeSpan.FromMilliseconds(33);
            var request = Requests.LastOrDefault(request => !request.Cancelled);
            request?.Dispose();
            request?.Callback();
        }
        internal void InvokeRetired()
        {
            foreach (var request in Requests.Where(request => request.Cancelled).ToArray()) { request.Callback(); }
        }
        internal sealed class Request(Action callback) : IDisposable
        {
            internal Action Callback { get; } = callback;
            internal bool Cancelled { get; private set; }
            public void Dispose() => Cancelled = true;
        }
    }
}
