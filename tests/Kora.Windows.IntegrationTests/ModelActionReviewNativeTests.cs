using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Presentation;
using Kora.Application.ViewModels;
using Kora.Controls;
using Kora.Core;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.NativeUxFixture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ModelActionReviewNativeTests
{
    [Fact]
    public Task ExactReviewTrayAndNativeSurfaceKeepOriginalControlsAcrossReplacement() => HeadlessSession.RunAsync(async () =>
    {
        using var f = new NativeUxFixtureSession(Path.GetFullPath("."));
        var reasoner = new ReviewReasoner();
        await f.InitializeAsync(null, modelReasoner: reasoner, modelProbe: reasoner);
        using var responses = new ResponseWindowController(f.Main, NullLogger<ResponseWindowController>.Instance);
        using var tray = new SystemTrayController(f.Main, NullLogger<SystemTrayController>.Instance);
        var first = await ProposeAsync(f);
        var menu = TrayIcon.GetIcons(Avalonia.Application.Current!)!.Single().Menu!;
        var firstItem = menu.Items.OfType<NativeMenuItem>().Single(item =>
            string.Equals(item.Header, "Review pending action", StringComparison.Ordinal));
        var window = (ResponseWindow)typeof(ResponseWindowController).GetField("window",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(responses)!;
        window.UpdateLayout();
        var controls = window.GetVisualDescendants().OfType<ModelActionReviewButton>().ToArray();
        controls.Select(button => button.Content).Should().Equal("Once", "This session", "Always", "Reject");
        controls.Should().OnlyContain(button => ReferenceEquals(button.DataContext, first));
        controls[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        f.Main.PendingModelActionReview.Should().BeSameAs(first, "a carried or synthetic click is not fresh confirmation");

        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnActivated(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MainViewModel.ModelActionReviewStatus)
                && f.Main.ModelActionReviewStatus.Contains("opened", StringComparison.Ordinal))
            { activated.TrySetResult(); }
        }
        f.Main.PropertyChanged += OnActivated;
        ((INativeMenuItemExporterEventsImplBridge)firstItem).RaiseClicked();
        await activated.Task.WaitAsync(f.Token);
        f.Main.PropertyChanged -= OnActivated;
        window.IsVisible.Should().BeTrue();
        window.IsExpanded.Should().BeFalse();
        window.FindControl<TextBox>("CommandInput")!.IsFocused.Should().BeTrue();
        f.Main.PresenceState.Should().Be(AssistantState.Waiting);
        var second = await ProposeAsync(f);
        window.UpdateLayout();
        controls.Should().OnlyContain(button => !ReferenceEquals(button.DataContext, second) && button.CapturedTarget == null,
            "retired controls must not acquire the replacement target");
        window.GetVisualDescendants().OfType<ModelActionReviewButton>().Should()
            .OnlyContain(button => ReferenceEquals(button.DataContext, second));
        Action? delayed = null;
        SystemTrayController.RunAfterNativeMenuCloses(
            () => f.Main.ActivateModelActionReview(first), continuation => delayed = continuation);
        delayed.Should().NotBeNull();
        delayed!();
        f.Main.ModelActionReviewStatus.Should().Contain("unavailable");
        f.Main.PendingModelActionReview.Should().BeSameAs(second);
        ((INativeMenuItemExporterEventsImplBridge)firstItem).RaiseClicked();
        tray.Dispose();
        responses.Dispose();
        f.Main.ActivateModelActionReview(second).Should().BeFalse("a disposed native owner cannot reveal");
        await f.Main.RejectModelActionReviewAsync(second);
        f.Main.PendingModelActionReview.Should().BeNull();
        f.Main.PresenceState.Should().Be(f.Main.State);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ExactReviewKeyboardAndPointerRequireFreshArmedGesture(bool useMouse) => HeadlessSession.RunAsync(async () =>
    {
        using var f = new NativeUxFixtureSession(Path.GetFullPath("."));
        var reasoner = new ReviewReasoner();
        await f.InitializeAsync(null, modelReasoner: reasoner, modelProbe: reasoner);
        var first = await ProposeAsync(f);
        var clock = new ReviewClock();
        var button = new ModelActionReviewButton(clock) { Content = "Reject", DataContext = first };
        var window = new Window { Content = button, Width = 300, Height = 120 };
        ModelActionReviewTarget? clicked = null;
        button.Click += (_, _) => clicked = button.CapturedTarget;
        window.Show();
        window.UpdateLayout();
        try
        {
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            button.Focus();
            void Press()
            {
                if (useMouse) { window.MouseDown(point, MouseButton.Left); }
                else { window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); }
            }
            void Release()
            {
                if (useMouse) { window.MouseUp(point, MouseButton.Left); }
                else { window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); }
            }
            Press();
            clock.Advance(499);
            Release();
            clicked.Should().BeNull();
            clock.Advance(1);
            Release();
            clicked.Should().BeNull("key-up alone cannot confirm");
            Press();
            Release();
            clicked.Should().BeSameAs(first);
            await f.Main.RejectModelActionReviewAsync(clicked);
            f.Main.PendingModelActionReview.Should().BeNull();
            var second = await ProposeAsync(f);
            button.DataContext = second;
            button.CapturedTarget.Should().BeNull("rebinding cannot promote the old press");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ExactReviewPresenceGestureIsDragOnlyForControlLeftAndNeverFollowsReplacement() => HeadlessSession.RunAsync(async () =>
    {
        using var f = new NativeUxFixtureSession(Path.GetFullPath("."));
        var reasoner = new ReviewReasoner();
        await f.InitializeAsync(null, modelReasoner: reasoner, modelProbe: reasoner);
        using var responses = new ResponseWindowController(f.Main, NullLogger<ResponseWindowController>.Instance);
        var first = await ProposeAsync(f);
        var gesture = new PresenceReviewGesture(f.Main);
        gesture.Press(true, KeyModifiers.Control, MouseButton.Left).Should().Be(PresencePointerIntent.Drag);
        gesture.Release(true, KeyModifiers.Control, MouseButton.Left, true).Should().BeNull();
        f.Main.PendingModelActionReview.Should().BeSameAs(first);
        foreach (var (intercepts, modifiers, button) in new[]
        {
            (false, KeyModifiers.Control, MouseButton.Right), (true, KeyModifiers.None, MouseButton.Right),
            (true, KeyModifiers.Control, MouseButton.Middle),
        })
        {
            gesture.Press(intercepts, modifiers, button).Should().Be(PresencePointerIntent.None);
            gesture.Release(true, KeyModifiers.Control, MouseButton.Right, true).Should().BeNull();
        }
        gesture.Press(true, KeyModifiers.Control, MouseButton.Right).Should().Be(PresencePointerIntent.Review);
        var captured = gesture.Release(true, KeyModifiers.Control, MouseButton.Right, true);
        captured.Should().BeSameAs(first);
        f.Main.ActivateModelActionReview(captured).Should().BeTrue();
        gesture.Press(true, KeyModifiers.Control, MouseButton.Right);
        var second = await ProposeAsync(f);
        f.Main.ActivateModelActionReview(gesture.Release(true, KeyModifiers.Control, MouseButton.Right, true)).Should().BeFalse();
        f.Main.PendingModelActionReview.Should().BeSameAs(second);
        gesture.Press(true, KeyModifiers.Control, MouseButton.Right);
        gesture.Cancel();
        gesture.Release(true, KeyModifiers.Control, MouseButton.Right, true).Should().BeNull();
        gesture.Press(true, KeyModifiers.Control, MouseButton.Right);
        gesture.Release(true, KeyModifiers.Control, MouseButton.Right, false).Should().BeNull();
        gesture.Press(true, KeyModifiers.Control, MouseButton.Right);
        gesture.Release(true, KeyModifiers.None, MouseButton.Right, true).Should().BeNull();
        f.Access.SetOpen(false);
        gesture.Press(true, KeyModifiers.Control, MouseButton.Right).Should().Be(PresencePointerIntent.None);
        gesture.Release(true, KeyModifiers.Control, MouseButton.Right, true).Should().BeNull();
    });

    private static async Task<ModelActionReviewTarget> ProposeAsync(NativeUxFixtureSession f)
    {
        f.Main.CommandText = "please secure this workstation";
        await f.Main.RunTypedCommand.ExecuteAsync();
        await f.Main.ActiveReasoningTask!;
        Dispatcher.UIThread.RunJobs();
        return f.Main.CaptureModelActionReview()!;
    }

    private sealed class ReviewClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => timestamp;
        internal void Advance(long milliseconds) => timestamp += milliseconds;
    }

    private sealed class ReviewReasoner : ILocalModelReasoner, ISetupDependencyProbe
    {
        public string TaskId => "local.inference";
        public string TaskName => "Synthetic model-action review";
        public ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new DependencyStatus(TaskId, TaskName, DependencyReadiness.Ready, "Synthetic implementation seam only."));
        public Task<LocalModelResponse> ReasonAsync(string request, LocalModelContext context, LocalModelArtifact? artifact,
            CancellationToken cancellationToken) => Task.FromResult(new LocalModelResponse(null, BuiltInAction.LockMachine));
    }
}
