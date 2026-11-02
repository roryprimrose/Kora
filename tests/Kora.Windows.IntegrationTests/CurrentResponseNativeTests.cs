using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Presentation;
using Kora.Application.Visuals;
using Kora.Core;
using Kora.Core.Presentation;
using Kora.NativeUxFixture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class CurrentResponseNativeTests
{
    [Fact]
    public async Task CallSpeechButtonsReviewExactDetailsThenConfirmOnlyOneSilentFixturePlayback()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetFullPath("."), silentCaption: true);
            await f.InitializeAsync(null, callState: new ProtectedCall());
            f.Main.DefaultResponseMode = Kora.Core.Voice.ResponseOutputMode.Hybrid;
            var views = new List<CurrentView>();
            using var details = new DetailWindowController(f.Documents, () => f.Main.CanRevealPrivatePresentation,
                new(NullLogger<NativeDetailRenderer>.Instance), new NoClipboard(),
                (state, _, _) =>
                {
                    var view = new CurrentView(state);
                    views.Add(view);
                    return view;
                }, NullLogger<DetailWindowController>.Instance);
            using var controller = new ResponseWindowController(f.Main, NullLogger<ResponseWindowController>.Instance, details);
            f.Main.CommandText = "what power action is pending";
            await f.Main.RunTypedCommand.ExecuteAsync();
            Dispatcher.UIThread.RunJobs();
            var window = (ResponseWindow)typeof(ResponseWindowController).GetField("window",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
            window.UpdateLayout();
            var text = $"{f.Main.ResponseTitle}. {f.Main.ResponseBody}";
            var reviewed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Main.PropertyChanged += (_, args) =>
            {
                if (string.Equals(args.PropertyName, nameof(f.Main.CanConfirmCallSpeech), StringComparison.Ordinal)
                    && f.Main.CanConfirmCallSpeech) { reviewed.TrySetResult(); }
            };
            var review = window.GetVisualDescendants().OfType<Button>().Single(button =>
                string.Equals(button.Content as string, "Review speak once during call", StringComparison.Ordinal));
            f.Main.CanReviewCallSpeech.Should().BeTrue(f.Main.CallSpeechStatus);
            review.Command.Should().BeSameAs(f.Main.ReviewCallSpeechCommand);
            review.Command!.Execute(null);
            await reviewed.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            views.Should().ContainSingle();
            views[0].State.ActiveText.Should().Contain("EXACT RESPONSE TO SPEAK").And.EndWith(text);
            var inactivity = (PresentationInactivityTimeout)typeof(ResponseWindow).GetField("responseInactivity",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            inactivity.IsScheduled.Should().BeFalse("the separate exact review must not be auto-hidden mid-review");
            f.Main.IsSpeechResponseEnabled.Should().BeFalse();
            f.SilentSpeech!.AdmittedRequests.Should().Be(0);
            var confirm = window.GetVisualDescendants().OfType<Button>().Single(button =>
                string.Equals(button.Content as string, "Confirm speak once", StringComparison.Ordinal));
            confirm.Command.Should().BeSameAs(f.Main.ConfirmCallSpeechCommand);
            var started = f.SilentSpeech.Arm();
            var completion = f.Main.ConfirmCallSpeechCommand.ExecuteAsync();
            var observed = await Task.WhenAny(started, completion);
            observed.Should().BeSameAs(started, f.Main.CallSpeechStatus);
            await started.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            f.SilentSpeech.ExactText.Should().Be(text);
            views[0].Cleared.Should().BeTrue("the consumed review is not reusable native authority");
            f.SilentSpeech.ObservePlayback();
            f.Main.RefreshSpeechPlaybackFrame();
            f.Main.IsSpeechCaptionVisible.Should().BeTrue();
            f.SilentSpeech.Complete();
            await completion;
            f.Main.CallSpeechStatus.Should().Contain("completed once");
            f.Main.CanConfirmCallSpeech.Should().BeFalse();
            f.SilentSpeech.AdmittedRequests.Should().Be(1);
            f.Main.IsProtectedCall.Should().BeTrue();
            await f.Main.ConfirmCallSpeechCommand.ExecuteAsync();
            f.SilentSpeech.AdmittedRequests.Should().Be(1);
        });
    }

    private sealed class ProtectedCall : Kora.Core.Communication.ICallStateService
    {
        public Kora.Core.Communication.CallState CurrentState => Kora.Core.Communication.CallState.Active;
        public event EventHandler<Kora.Core.Communication.CallStateChangedEventArgs>? StateChanged { add { } remove { } }
    }

    [Fact]
    public async Task BackgroundFailureRevealsItsDetailsAndCannotAutoHideBeforeExplicitDismissal()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetFullPath("."), silentCaption: true);
            await f.InitializeAsync();
            using var controller = new ResponseWindowController(f.Main, NullLogger<ResponseWindowController>.Instance);
            f.Main.CommandText = "what power action is pending";
            await f.Main.RunTypedCommand.ExecuteAsync();
            Dispatcher.UIThread.RunJobs();
            var window = (ResponseWindow)typeof(ResponseWindowController).GetField("window",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
            // Inspect the legacy window's timeout without relying on real dispatcher time.
            var inactivity = (PresentationInactivityTimeout)typeof(ResponseWindow).GetField("responseInactivity",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            window.IsVisible.Should().BeTrue();
            inactivity.IsScheduled.Should().BeTrue();
            controller.Dismiss(static () => true).Should().BeTrue();
            window.IsVisible.Should().BeFalse();

            f.Main.ReportHostInteractionFailure("Synthetic background host failure.");
            Dispatcher.UIThread.RunJobs();

            f.Main.State.Should().Be(AssistantState.Failure);
            f.Main.ResponseBody.Should().Be("Synthetic background host failure.");
            window.IsVisible.Should().BeTrue();
            inactivity.IsScheduled.Should().BeFalse("unacknowledged failure details must not time out");

            f.Main.CommandText = "what power action is pending";
            await f.Main.RunTypedCommand.ExecuteAsync();
            Dispatcher.UIThread.RunJobs();
            f.Main.State.Should().Be(AssistantState.Information);
            inactivity.IsScheduled.Should().BeTrue("an informational response resumes the configured timeout");

            f.Main.ReportHostInteractionFailure("Synthetic replacement host failure.");
            inactivity.IsScheduled.Should().BeFalse("a failure also cancels an already scheduled timeout");
            controller.Dismiss(static () => true).Should().BeTrue();
            window.IsVisible.Should().BeFalse("explicit dismissal remains available for failures");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualResponseButtonsAndTypedCommandsShareNativeControllersAndRevokeDismissedDetails(bool dispose)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetFullPath("."), silentCaption: true);
            await f.InitializeAsync();
            using (var activity = Kora.Core.Diagnostics.HostActivity.BeginRoot(
                Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.LocalUi),
                Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request))
            { (await f.Main.SetResponseAlwaysVisibleAsync(true)).Should().BeTrue(); }
            var views = new List<CurrentView>();
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var details = new DetailWindowController(f.Documents, () => f.Main.CanRevealPrivatePresentation,
                new(NullLogger<NativeDetailRenderer>.Instance), new NoClipboard(),
                (state, _, _) =>
                {
                    var view = new CurrentView(state);
                    views.Add(view);
                    opened.TrySetResult();
                    return view;
                },
                NullLogger<DetailWindowController>.Instance);
            using var controller = new ResponseWindowController(f.Main, NullLogger<ResponseWindowController>.Instance, details);
            f.Main.CommandText = "what power action is pending";
            await f.Main.RunTypedCommand.ExecuteAsync();
            Dispatcher.UIThread.RunJobs();
            var window = (ResponseWindow)typeof(ResponseWindowController).GetField("window",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
            window.Should().NotBeNull();
            window.IsVisible.Should().BeTrue();
            window.UpdateLayout();
            var original = f.Main.ResponseBody;
            var button = window.GetVisualDescendants().OfType<Button>().Single(button =>
                string.Equals(button.Content as string, "Current response details", StringComparison.Ordinal));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            views.Should().ContainSingle();
            views[0].State.ActiveText.Should().EndWith(original);
            f.Main.CommandText = "open current response details";
            await f.Main.RunTypedCommand.ExecuteAsync();
            f.Main.Transcript.Should().Be("Exact current response details opened.");
            var dismiss = window.GetVisualDescendants().OfType<Button>().Single(button =>
                string.Equals(button.Content as string, "Dismiss", StringComparison.Ordinal));
            if (dispose) { controller.Dispose(); }
            else { dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            await views[^1].ClosedTask.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            window.IsVisible.Should().BeFalse();
            views.Should().OnlyContain(view => view.Cleared);
            f.Main.CommandText = "open current response details";
            await f.Main.RunTypedCommand.ExecuteAsync();
            f.Main.Transcript.Should().Contain("unavailable");
            f.Main.ResponseBody.Should().Be(original);
            var lifetime = controller.CaptureLifetime();
            lifetime().Should().BeFalse();
            controller.Dispose();
            controller.Dismiss(static () => true).Should().BeFalse();
        });
    }

    private sealed class NoClipboard : IDetailClipboard
    {
        public Task WriteAsync(IDetailView view, string source, string? htmlFragment) =>
            throw new InvalidOperationException("Current response actions must never write the clipboard.");
    }

    private sealed class CurrentView(DetailViewerState state) : IDetailView
    {
        public DetailViewerState State { get; } = state;
        public bool Cleared { get; private set; }
        private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ClosedTask => closed.Task;
        public event EventHandler? Closed;
        public void ShowOwned(Window? owner) => owner.Should().NotBeNull();
        public void Activate() { }
        public void ClearAndClose()
        {
            Cleared = true;
            State.Close();
            Closed?.Invoke(this, EventArgs.Empty);
            closed.TrySetResult();
        }
    }
}
