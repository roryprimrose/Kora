using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionRoutingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VoiceAdviceAndDestinationSurviveUnrelatedFocusWithoutActivatingTheirOwner(bool hosted)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture { AutoAllowAdvice = false };
            await f.Initialize(expanded: true);
            var original = f.Window!;
            HeadlessSession.SetActivation(original, true);
            if (hosted) { f.Routing.BindWorkbench(() => original); }
            var adviceReady = new TaskCompletionSource<SessionRoutingAdviceView>(TaskCreationOptions.RunContinuationsAsynchronously);
            var destinationReady = new TaskCompletionSource<SessionRoutingView>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var adviceOpened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingAdviceWindow>(
                (window, _) => adviceReady.TrySetResult(window.View));
            using var routeOpened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>(
                (window, _) => destinationReady.TrySetResult(window.View));
            void Layout(object? sender, EventArgs args)
            {
                if (original.GetVisualDescendants().OfType<SessionRoutingAdviceView>().FirstOrDefault() is { } advice)
                { adviceReady.TrySetResult(advice); }
                if (original.GetVisualDescendants().OfType<SessionRoutingView>().FirstOrDefault() is { } destination)
                { destinationReady.TrySetResult(destination); }
            }
            original.LayoutUpdated += Layout;
            var unrelated = new Window { ShowActivated = false };
            try
            {
                var operation = f.Routing.RouteAsync("unchanged original voice body", RequestOrigin.ActivatedVoice, static () => true);
                await Task.WhenAny(operation, adviceReady.Task).WaitAsync(TimeSpan.FromSeconds(15), f.Native.Token);
                operation.IsCompleted.Should().BeFalse();
                var advice = await adviceReady.Task;
                original.IsActive.Should().BeTrue();
                if (!hosted)
                {
                    advice.Owner.ShowActivated.Should().BeFalse();
                    advice.Owner.IsActive.Should().BeFalse();
                }
                unrelated.Show();
                HeadlessSession.SetActivation(original, false);
                HeadlessSession.SetActivation(unrelated, true);
                Dispatcher.UIThread.RunJobs();
                advice.Content.Should().NotBeNull();
                f.Matches.Should().Be(0);
                advice.GetVisualDescendants().OfType<Button>().Single(button => button.Name is "RoutingAdviceAllow")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.WhenAny(operation, destinationReady.Task).WaitAsync(TimeSpan.FromSeconds(15), f.Native.Token);
                operation.IsCompleted.Should().BeFalse();
                var destination = await destinationReady.Task;
                unrelated.IsActive.Should().BeTrue("showing the next voice review cannot reclaim focus");
                destination.Owner.IsActive.Should().BeFalse();
                if (!hosted) { destination.Owner.ShowActivated.Should().BeFalse(); }
                destination.GetVisualDescendants().OfType<Button>().Single(button => button.Name is "RoutingChoose")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await destination.ChoiceOperation;
                var submit = destination.GetVisualDescendants().OfType<Button>().Single(button => button.Name is "RoutingSubmit");
                submit.IsEnabled.Should().BeTrue();
                HeadlessSession.SetActivation(unrelated, false);
                HeadlessSession.SetActivation(destination.Owner, true);
                submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var receipt = await operation.WaitAsync(TimeSpan.FromSeconds(15), f.Native.Token);
                receipt!.Submission.Message.Body.Should().Be("unchanged original voice body");
                receipt.Submission.Message.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
                f.Admission.Calls.Should().Be(1);
                advice.Content.Should().BeNull();
                destination.Content.Should().BeNull();
            }
            finally
            {
                original.LayoutUpdated -= Layout;
                unrelated.Close();
            }
        });
    }
}
