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
        public Task WritePlainTextAsync(IDetailView view, string source) =>
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
