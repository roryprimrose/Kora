using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Infrastructure;
using Kora.Application.Voice;
using Kora.Core.Voice;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SpeechCaptionDisplayTests
{
    [Theory]
    [InlineData(SpeechCaptionPlacement.TopLeft, 1)]
    [InlineData(SpeechCaptionPlacement.TopRight, 1.25)]
    [InlineData(SpeechCaptionPlacement.BottomLeft, 2)]
    [InlineData(SpeechCaptionPlacement.BottomRight, 3)]
    public async Task NativeChoiceBindsActualSourceWorkingAreaAndDpi(SpeechCaptionPlacement corner, double scaling)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var caption = new Caption { SpeechCaptionPlacement = corner };
            var primary = Display(0, 0, 1920, 1080, 1, primary: true);
            var other = Display(-2400, -1200, 2200, 1100, scaling);
            var source = new Displays(primary, other);
            var window = new SpeechCaptionWindow(caption);
            using var controller = new SpeechCaptionWindowController(caption, window, source);
            controller.Refresh();
            window.IsVisible.Should().BeTrue();
            window.Position.X.Should().BeGreaterThanOrEqualTo(0);
            Choose(controller, window, other);
            AssertContained(window, other);
            var margin = (int)Math.Ceiling(24 * scaling);
            if (corner is SpeechCaptionPlacement.TopLeft or SpeechCaptionPlacement.BottomLeft)
            {
                window.Position.X.Should().Be(other.WorkingArea.X + margin);
            }
            else
            {
                window.Position.X.Should().Be(other.WorkingArea.Right - (int)Math.Ceiling(window.Width * scaling) - margin);
            }
            if (corner is SpeechCaptionPlacement.TopLeft or SpeechCaptionPlacement.TopRight)
            {
                window.Position.Y.Should().Be(other.WorkingArea.Y + margin);
            }
            caption.SpeechCaptionText.Should().Be("Observed private utterance.");
            caption.Failures.Should().BeEmpty();
            caption.Retirements.Should().Be(0);
        });
    }

    [Theory]
    [InlineData(1, 1, 3)]
    [InlineData(37, 29, 1.5)]
    [InlineData(280, 180, 2)]
    public async Task TinyWorkingAreasClipAllCornersWithoutSpilling(int width, int height, double scaling)
    {
        await HeadlessSession.RunAsync(() =>
        {
            foreach (var corner in Enum.GetValues<SpeechCaptionPlacement>())
            {
                var caption = new Caption { SpeechCaptionPlacement = corner };
                var target = Display(-width, -height, width, height, scaling, primary: true);
                var window = new SpeechCaptionWindow(caption);
                using var controller = new SpeechCaptionWindowController(caption, window, new Displays(target));
                controller.Refresh();
                window.IsVisible.Should().BeTrue();
                AssertContained(window, target);
                caption.Failures.Should().BeEmpty();
            }
        });
    }

    [Fact]
    public async Task TopologyReordersAndGeometryUpdatesKeepTheChosenLifetimeButExpireOldChoices()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var caption = new Caption();
            var primary = Display(0, 0, 1920, 1080, 1, primary: true);
            var other = Display(-1920, 0, 1920, 1080, 1);
            var source = new Displays(primary, other);
            var window = new SpeechCaptionWindow(caption);
            using var controller = new SpeechCaptionWindowController(caption, window, source);
            controller.Refresh();
            var old = controller.Choices[1];
            Choose(controller, window, other);
            controller.SelectedChoice = old;
            Click(window, reset: false);
            controller.DisplayStatus.Should().Contain("expired");
            AssertContained(window, other);
            var updated = other with { WorkingArea = new(-3000, -400, 2600, 1300), Scaling = 2 };
            source.Change(updated, primary, Display(1920, 0, 1024, 768, 1));
            AssertContained(window, updated);
            controller.Choices.Should().HaveCount(3);
            controller.SelectedChoice = old;
            Click(window, reset: false);
            AssertContained(window, updated);
            caption.Retirements.Should().Be(0);
            Click(window, reset: true);
            AssertContained(window, primary);
            controller.DisplayStatus.Should().Contain("default");
        });
    }

    [Fact]
    public async Task RemovedExplicitTargetRetiresEvenPinnedTextAndNeverFallsBackOrResurrects()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var caption = new Caption { SpeechCaptionPinLabel = "Unpin", SpeechCaptionLabel = "PREVIOUS SPEECH" };
            var primary = Display(0, 0, 1920, 1080, 1, primary: true);
            var other = Display(-1920, 0, 1920, 1080, 1);
            var source = new Displays(primary, other);
            var window = new SpeechCaptionWindow(caption);
            using var controller = new SpeechCaptionWindowController(caption, window, source);
            controller.Refresh();
            Choose(controller, window, other);
            var old = controller.Choices.Single(choice => ReferenceEquals(choice.Display.Lifetime, other.Lifetime));
            source.Change(primary);
            window.IsVisible.Should().BeFalse();
            caption.SpeechCaptionText.Should().BeNull();
            caption.Retirements.Should().Be(1);
            controller.DisplayStatus.Should().Contain("unavailable");
            source.Change(primary, other with { Lifetime = new() });
            caption.ObserveFreshUtterance();
            controller.Refresh();
            caption.SpeechCaptionText.Should().BeNull();
            caption.ObserveLateFrame();
            controller.Refresh();
            window.IsVisible.Should().BeFalse();
            var recovery = new Window();
            try
            {
                recovery.Show();
                controller.SelectedChoice = old;
                controller.SelectFromNative(recovery, reset: false);
                controller.DisplayStatus.Should().Contain("expired");
                controller.SelectFromNative(recovery, reset: true);
                window.IsVisible.Should().BeFalse();
                caption.ObserveFreshUtterance();
                controller.Refresh();
                AssertContained(window, primary);
                window.IsVisible.Should().BeTrue();
            }
            finally { recovery.Close(); }
        });
    }

    [Theory]
    [InlineData("off")]
    [InlineData("unconfirmed")]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("ownership")]
    [InlineData("pending")]
    public async Task NativeSelectionDoesNotEnableOutputOrClearUnderlyingHolds(string hold)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var caption = new Caption { IsSpeechCaptionVisible = false, SpeechCaptionText = null,
                CanChooseSpeechCaptionDisplay = hold is "off" or "unconfirmed" };
            var primary = Display(0, 0, 1920, 1080, 1, primary: true);
            var other = Display(-1920, 0, 1920, 1080, 1);
            var window = new SpeechCaptionWindow(caption);
            using var controller = new SpeechCaptionWindowController(caption, window, new Displays(primary, other));
            var settings = new Window();
            try
            {
                settings.Show();
                controller.Refresh();
                controller.SelectedChoice = controller.Choices[1];
                controller.SelectFromNative(settings, reset: false);
                controller.CanSelect.Should().Be(caption.CanChooseSpeechCaptionDisplay);
                window.IsVisible.Should().BeFalse();
                caption.SpeechCaptionText.Should().BeNull();
                caption.SpeechCaptionPlacement.Should().Be(SpeechCaptionPlacement.BottomRight);
                if (hold is "off")
                {
                    caption.ObserveFreshUtterance();
                    controller.Refresh();
                    AssertContained(window, other);
                }
                else
                {
                    controller.SelectFromNative(settings, reset: true);
                    window.IsVisible.Should().BeFalse();
                }
                caption.Retirements.Should().Be(0);
            }
            finally { settings.Close(); }
        });
    }

    [Theory]
    [InlineData("primary-missing")]
    [InlineData("two-primary")]
    [InlineData("duplicate-lifetime")]
    [InlineData("invalid-dpi")]
    [InlineData("empty-area")]
    [InlineData("overlapping-area")]
    public async Task UnknownOrAmbiguousActualTargetNeverShowsPrivateText(string invalid)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var caption = new Caption();
            var primary = Display(0, 0, 1920, 1080, 1, primary: true);
            var items = invalid switch
            {
                "primary-missing" => new[] { primary with { IsPrimary = false } },
                "two-primary" => new[] { primary, Display(-1920, 0, 1920, 1080, 1, primary: true) },
                "duplicate-lifetime" => new[] { primary, primary },
                "invalid-dpi" => new[] { primary with { Scaling = double.NaN } },
                "overlapping-area" => new[] { primary, Display(0, 0, 1920, 1080, 1) },
                _ => new[] { primary with { WorkingArea = new(0, 0, 0, 0) } },
            };
            var window = new SpeechCaptionWindow(caption);
            using var controller = new SpeechCaptionWindowController(caption, window, new Displays(items));
            controller.Refresh();
            window.IsVisible.Should().BeFalse();
            caption.Retirements.Should().Be(1);
        });
    }

    [Fact]
    public async Task PrivacyClosureDisposalAndLateEventsCannotRestoreTextAndRestartForgetsSelection()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var primary = Display(0, 0, 1920, 1080, 1, primary: true);
            var other = Display(-1920, 0, 1920, 1080, 1);
            var caption = new Caption();
            var source = new Displays(primary, other);
            var window = new SpeechCaptionWindow(caption);
            var controller = new SpeechCaptionWindowController(caption, window, source);
            controller.Refresh();
            Choose(controller, window, other);
            caption.ClosePrivacy();
            window.IsVisible.Should().BeFalse();
            caption.ObserveLateFrame();
            controller.Refresh();
            window.IsVisible.Should().BeFalse();
            controller.Dispose();
            controller.Dispose();
            source.Change(primary);
            controller.Refresh();
            source.Disposed.Should().BeTrue();
            window.DataContext.Should().BeNull();
            controller.Choices.Should().BeEmpty();
            var restartedCaption = new Caption();
            var restarted = new SpeechCaptionWindow(restartedCaption);
            using var next = new SpeechCaptionWindowController(restartedCaption, restarted, new Displays(primary, other));
            next.Refresh();
            AssertContained(restarted, primary);
        });
    }

    [Fact]
    public async Task ActualAvaloniaObservationIsEphemeralAndOwnsItsSubscription()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var window = new SpeechCaptionWindow(new Caption());
            try
            {
                using var source = new AvaloniaCaptionDisplaySource(window.Screens);
                var first = source.Capture();
                first.Displays.Should().HaveCount(window.Screens.All.Count);
                source.Capture().Should().BeSameAs(first);
                for (var index = 0; index < first.Displays.Count; index++)
                {
                    first.Displays[index].WorkingArea.Should().Be(window.Screens.All[index].WorkingArea);
                    first.Displays[index].Scaling.Should().Be(window.Screens.All[index].Scaling);
                }
                source.Dispose();
                source.Invoking(item => item.Capture()).Should().Throw<ObjectDisposedException>();
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task ExplicitPrimaryDoesNotFollowDesignationUntilNativeReset()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var caption = new Caption();
            var first = Display(0, 0, 1920, 1080, 1, primary: true);
            var second = Display(-1920, 0, 1920, 1080, 1);
            var source = new Displays(first, second);
            var window = new SpeechCaptionWindow(caption);
            using var controller = new SpeechCaptionWindowController(caption, window, source);
            controller.Refresh();
            Choose(controller, window, first);
            source.Change(first with { IsPrimary = false }, second with { IsPrimary = true });
            AssertContained(window, first);
            Click(window, reset: true);
            AssertContained(window, second);
        });
    }

    [Fact]
    public async Task NativeClosureExpiresSelectionsAndLateOwnershipLossDeniesMutation()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var caption = new Caption();
            var first = Display(0, 0, 1920, 1080, 1, primary: true);
            var second = Display(-1920, 0, 1920, 1080, 1);
            var source = new Displays(first, second);
            var window = new SpeechCaptionWindow(caption);
            using var controller = new SpeechCaptionWindowController(caption, window, source);
            controller.Refresh();
            var selector = new CaptionDisplaySelector();
            selector.Bind(controller);
            var settings = new Window { Content = selector };
            settings.Show();
            var old = controller.Choices[1];
            settings.Close();
            controller.SelectedChoice = old;
            Click(window, reset: false);
            controller.DisplayStatus.Should().Contain("expired");
            controller.Refresh();
            controller.SelectedChoice = controller.Choices[1];
            source.OnCapture = () => caption.CanChooseSpeechCaptionDisplay = false;
            Click(window, reset: false);
            source.OnCapture = null;
            AssertContained(window, first);
            caption.Retirements.Should().Be(0);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeSourceFailureIsContentFreeAndCannotBeRepairedByDisplayReset(bool duringSelection)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var caption = new Caption();
            var source = new Displays(Display(0, 0, 1920, 1080, 1, primary: true));
            var window = new SpeechCaptionWindow(caption);
            using var controller = new SpeechCaptionWindowController(caption, window, source);
            controller.Refresh();
            source.Failure = new InvalidOperationException("Do not disclose native or caption contents.");
            if (duringSelection) { Click(window, reset: true); }
            else { controller.Refresh(); }
            caption.Failures.Should().Equal(nameof(InvalidOperationException));
            window.IsVisible.Should().BeFalse();
            controller.CanSelect.Should().BeFalse();
            source.Failure = null;
            controller.Refresh();
            controller.SelectFromNative(window, reset: true);
            window.IsVisible.Should().BeFalse();
        });
    }

    [Fact]
    public async Task NativeCaptionClosureRetiresTheSourceAndStopsLateCallbacks()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var caption = new Caption();
            var source = new Displays(Display(0, 0, 1920, 1080, 1, primary: true));
            var window = new SpeechCaptionWindow(caption);
            using var controller = new SpeechCaptionWindowController(caption, window, source);
            controller.Refresh();
            window.Close();
            caption.SpeechCaptionText.Should().BeNull();
            source.Disposed.Should().BeTrue();
            source.Change(Display(0, 0, 1920, 1080, 1, primary: true));
            controller.Refresh();
            window.IsVisible.Should().BeFalse();
            controller.Choices.Should().BeEmpty();
        });
    }

    private static CaptionDisplayObservation Display(int x, int y, int width, int height, double scaling, bool primary = false) =>
        new(new(), new(x, y, width, height), scaling, primary);

    private static void Choose(SpeechCaptionWindowController controller, Window window, CaptionDisplayObservation target)
    {
        controller.SelectedChoice = controller.Choices.Single(choice => ReferenceEquals(choice.Display.Lifetime, target.Lifetime));
        Click(window, reset: false);
    }

    private static void Click(Window window, bool reset)
    {
        var selector = window.FindControl<CaptionDisplaySelector>("CaptionDisplaySelector")!;
        var buttons = ((StackPanel)selector.Content!).Children.OfType<WrapPanel>().Single().Children.OfType<Button>().ToArray();
        buttons[reset ? 1 : 0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void AssertContained(Window window, CaptionDisplayObservation display)
    {
        window.Position.X.Should().BeGreaterThanOrEqualTo(display.WorkingArea.X);
        window.Position.Y.Should().BeGreaterThanOrEqualTo(display.WorkingArea.Y);
        (window.Position.X + Math.Ceiling(window.Width * display.Scaling)).Should().BeLessThanOrEqualTo(display.WorkingArea.Right);
        (window.Position.Y + Math.Ceiling(window.Height * display.Scaling)).Should().BeLessThanOrEqualTo(display.WorkingArea.Bottom);
        window.Width.Should().BePositive();
        window.Height.Should().BePositive();
    }

    private sealed class Displays(params CaptionDisplayObservation[] items) : ICaptionDisplaySource
    {
        private CaptionDisplaySnapshot snapshot = new(1, items);
        public bool Disposed { get; private set; }
        public Exception? Failure { get; set; }
        public Action? OnCapture { get; set; }
        public event EventHandler? Changed;
        public CaptionDisplaySnapshot Capture()
        {
            OnCapture?.Invoke();
            if (Failure is { } failure) { throw failure; }
            return snapshot;
        }
        public void Change(params CaptionDisplayObservation[] updated)
        {
            snapshot = new(snapshot.Revision + 1, updated);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        public void Dispose() { Disposed = true; Changed = null; }
    }

    private sealed class Caption : ObservableObject, ISpeechCaptionPresentation
    {
        public string? SpeechCaptionText { get; set; } = "Observed private utterance.";
        public string SpeechCaptionLabel { get; set; } = "CURRENT PLAYBACK - UTTERANCE";
        public string SpeechCaptionPinLabel { get; set; } = "Pin";
        public AsyncCommand ToggleSpeechCaptionPinCommand { get; } = new(() => Task.CompletedTask, _ => { });
        public SpeechCaptionPlacement? SpeechCaptionPlacement { get; set; } = Kora.Core.Voice.SpeechCaptionPlacement.BottomRight;
        public bool IsSpeechCaptionVisible { get; set; } = true;
        public bool CanChooseSpeechCaptionDisplay { get; set; } = true;
        public event EventHandler? PrivacyClosureRequested;
        public int Retirements { get; private set; }
        public List<string> Failures { get; } = [];
        public void RefreshSpeechPlaybackFrame() { }
        public void ReportSpeechCaptionDisplayUnavailable(bool reportRecovery = true)
        {
            if (reportRecovery) { Retirements++; }
            SpeechCaptionText = null;
            IsSpeechCaptionVisible = false;
        }
        public void ReportSpeechCaptionPresentationFailure(string exceptionType) { Failures.Add(exceptionType); ReportSpeechCaptionDisplayUnavailable(); }
        public void ObserveLateFrame() { }
        public void ObserveFreshUtterance() { SpeechCaptionText = "Fresh observed utterance."; IsSpeechCaptionVisible = true; OnPropertyChanged(nameof(SpeechCaptionText)); }
        public void ClosePrivacy() { ReportSpeechCaptionDisplayUnavailable(); PrivacyClosureRequested?.Invoke(this, EventArgs.Empty); }
    }
}
