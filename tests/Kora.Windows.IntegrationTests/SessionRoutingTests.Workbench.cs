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
    [InlineData(true, "hide")]
    [InlineData(false, "hide")]
    [InlineData(true, "minimize")]
    [InlineData(false, "minimize")]
    [InlineData(true, "tab")]
    [InlineData(false, "tab")]
    public async Task HostedRoutingWithdrawalCannotAcceptUnseenAdviceOrSubmitAnOldAcknowledgment(bool adviceStage, string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize(expanded: true);
            var shell = f.Window!;
            f.Routing.BindWorkbench(() => shell);
            var seen = new HashSet<SessionRoutingAdviceView>();
            var ready = new TaskCompletionSource<Control>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Layout(object? sender, EventArgs args)
            {
                var advice = shell.GetVisualDescendants().OfType<SessionRoutingAdviceView>().FirstOrDefault();
                if (advice is not null && seen.Add(advice))
                {
                    if (adviceStage) { ready.TrySetResult(advice); }
                    else
                    {
                        Dispatcher.UIThread.Post(() => advice.GetVisualDescendants().OfType<Button>()
                            .Single(button => string.Equals(button.Name, "RoutingAdviceAllow", StringComparison.Ordinal))
                            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                    }
                }
                if (!adviceStage && shell.GetVisualDescendants().OfType<SessionRoutingView>().FirstOrDefault() is { } route)
                { ready.TrySetResult(route); }
            }
            shell.LayoutUpdated += Layout;
            try
            {
                var routing = f.Routing.RouteAsync("Original exact request that must not submit", RequestOrigin.LocalUi, static () => true);
                var reached = await Task.WhenAny(routing, ready.Task).WaitAsync(TimeSpan.FromSeconds(15), f.Native.Token);
                reached.Should().BeSameAs(ready.Task);
                var view = await ready.Task;
                Button? late;
                if (view is SessionRoutingView route)
                {
                    route.GetVisualDescendants().OfType<Button>().Single(button =>
                        string.Equals(button.Name, "RoutingChoose", StringComparison.Ordinal))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await route.ChoiceOperation;
                    late = route.GetVisualDescendants().OfType<Button>().Single(button =>
                        string.Equals(button.Name, "RoutingSubmit", StringComparison.Ordinal));
                    late.IsEnabled.Should().BeTrue();
                }
                else
                {
                    late = view.GetVisualDescendants().OfType<Button>().Single(button =>
                        string.Equals(button.Name, "RoutingAdviceAllow", StringComparison.Ordinal));
                }
                if (boundary is "hide") { shell.Hide(); shell.Show(); shell.Activate(); }
                else if (boundary is "minimize")
                { shell.WindowState = WindowState.Minimized; shell.WindowState = WindowState.Normal; }
                else
                {
                    var other = new TextBlock { Text = "Another exact review" };
                    shell.ShowNativeReview(other, "Another exact review", other, static () => true, _ => { });
                }
                late.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                (await routing.WaitAsync(TimeSpan.FromSeconds(15), f.Native.Token)).Should().BeNull();
                ((ContentControl)view).Content.Should().BeNull();
                f.Admission.Calls.Should().Be(0);
            }
            finally { shell.LayoutUpdated -= Layout; }
        });
    }

    [Theory]
    [InlineData(false, RequestOrigin.LocalUi)]
    [InlineData(true, RequestOrigin.LocalUi)]
    [InlineData(false, RequestOrigin.ActivatedVoice)]
    [InlineData(true, RequestOrigin.ActivatedVoice)]
    public async Task HostedAdviceAndDestinationKeepOriginalChannelAndSubmitOnlyAcknowledgedTarget(bool expanded, RequestOrigin origin)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize(expanded);
            var shell = f.Window!;
            f.Routing.BindWorkbench(() => shell);
            f.Model.ComposerBody = "independent original session input";
            var prior = f.Model.SelectedSessionRecord;
            var seenAdvice = new HashSet<SessionRoutingAdviceView>();
            var ready = new TaskCompletionSource<SessionRoutingView>(TaskCreationOptions.RunContinuationsAsynchronously);
            var windows = new List<Window>();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => windows.Add(window));
            void Layout(object? sender, EventArgs args)
            {
                var advice = shell.GetVisualDescendants().OfType<SessionRoutingAdviceView>().FirstOrDefault();
                if (advice is not null && seenAdvice.Add(advice))
                {
                    Dispatcher.UIThread.Post(() => advice.GetVisualDescendants().OfType<Button>()
                        .Single(button => string.Equals(button.Name, "RoutingAdviceAllow", StringComparison.Ordinal))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                }
                var route = shell.GetVisualDescendants().OfType<SessionRoutingView>().FirstOrDefault();
                if (route is not null) { ready.TrySetResult(route); }
            }
            shell.LayoutUpdated += Layout;
            try
            {
                var routing = f.Routing.RouteAsync("Original exact routed request", origin, static () => true);
                var reached = await Task.WhenAny(routing, ready.Task).WaitAsync(TimeSpan.FromSeconds(15), f.Native.Token);
                reached.Should().BeSameAs(ready.Task, "the admitted route must reach its exact supporting review");
                var view = await ready.Task;
                var choose = view.GetVisualDescendants().OfType<Button>().Single(button =>
                    string.Equals(button.Name, "RoutingChoose", StringComparison.Ordinal));
                var submit = view.GetVisualDescendants().OfType<Button>().Single(button =>
                    string.Equals(button.Name, "RoutingSubmit", StringComparison.Ordinal));
                f.Admission.Calls.Should().Be(0);
                submit.IsEnabled.Should().BeFalse();
                choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await view.ChoiceOperation;
                submit.IsEnabled.Should().BeTrue();
                f.Admission.Calls.Should().Be(0);
                submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var receipt = await routing;
                receipt.Should().NotBeNull();
                receipt!.Target.Authority.SessionId.Should().Be(f.First.Authority.SessionId);
                receipt.Submission.Message.Request.Origin.Should().Be(origin);
                receipt.Submission.Message.Body.Should().Be("Original exact routed request");
                f.Admission.Calls.Should().Be(1);
                f.Model.SelectedSessionRecord.Should().Be(prior);
                f.Model.ComposerBody.Should().Be("independent original session input");
                seenAdvice.Should().ContainSingle();
                seenAdvice.Single().Content.Should().BeNull();
                view.Content.Should().BeNull();
                windows.Should().BeEmpty("the already-open compact/expanded owner is the only interaction window");
            }
            finally { shell.LayoutUpdated -= Layout; }
        });
    }
}
