using Avalonia.Controls;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionRoutingTests
{
    [Theory]
    [InlineData(false, RequestOrigin.LocalUi)]
    [InlineData(true, RequestOrigin.ActivatedVoice)]
    public async Task SeparateAdviceConsentThenDestinationReviewCompletesUnchangedOrdinaryRequest(bool expanded, RequestOrigin origin)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new LocalComposition();
            f.Ui.AutoAllowAdvice = false;
            await f.Initialize(expanded);
            var adviceOpening = new TaskCompletionSource<SessionRoutingAdviceWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            var routeOpening = new TaskCompletionSource<SessionRoutingWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var adviceOpened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingAdviceWindow>((window, _) => adviceOpening.TrySetResult(window));
            using var routeOpened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((window, _) => routeOpening.TrySetResult(window));
            const string body = "  Exact e\u0301 original\r\nunchanged  ";
            var operation = f.Routing.RouteAsync(body, origin, static () => true);
            await Task.WhenAny(operation, adviceOpening.Task);
            operation.IsCompleted.Should().BeFalse();
            var advice = await adviceOpening.Task;
            advice.GetVisualDescendants().OfType<TextBlock>().Single(item => item.Name is "RoutingAdviceBody").Text.Should().Be(body);
            advice.GetVisualDescendants().OfType<TextBlock>().Single(item => item.Name is "RoutingAdviceNames").Text.Should()
                .Contain("Alpha").And.Contain("Beta");
            f.Adapter.Calls.Should().Be(0);
            routeOpening.Task.IsCompleted.Should().BeFalse();
            (await f.Ui.Native.Interactions.ReadConversationPageAsync(f.Ui.First.Authority.SessionId, null, 50, f.Ui.Native.Token))
                .Messages.Should().BeEmpty();
            ClickAdvice(advice, "RoutingAdviceAllow");
            var route = await AwaitOpening(operation, routeOpening.Task);
            f.Adapter.Calls.Should().Be(1);
            advice.Content.Should().BeNull();
            await Choose(route);
            f.Adapter.Calls.Should().Be(1, "destination acknowledgment is not task submission or chained advice consent");
            Click(route, "RoutingSubmit");
            var receipt = (await operation)!;
            receipt.Submission.Message.Body.Should().Be(body);
            receipt.Submission.Message.Request.Origin.Should().Be(origin);
            receipt.Submission.Message.AssistantResponse!.Body.Should().Be("Genuine second-turn answer");
            f.Adapter.Calls.Should().Be(2);
            f.Adapter.Originals.Should().Equal(body, body);
        });
    }

    [Fact]
    public async Task SkippingAdviceStillCompletesAnExactExistingDestinationWithSeparateSubmit()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture { AutoAllowAdvice = false };
            await f.Initialize(expanded: true);
            var adviceOpening = new TaskCompletionSource<SessionRoutingAdviceWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            var routeOpening = new TaskCompletionSource<SessionRoutingWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var adviceOpened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingAdviceWindow>((window, _) => adviceOpening.TrySetResult(window));
            using var routeOpened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((window, _) => routeOpening.TrySetResult(window));
            var operation = f.Routing.RouteAsync("unchanged original", RequestOrigin.LocalUi, static () => true);
            var advice = await adviceOpening.Task;
            ClickAdvice(advice, "RoutingAdviceSkip");
            var route = await AwaitOpening(operation, routeOpening.Task);
            f.Matches.Should().Be(0);
            Click(route, "RoutingCorrect");
            var destinations = Find<ComboBox>(route, "RoutingDestinations");
            destinations.SelectedIndex = Array.FindIndex(destinations.Items.Cast<object>().ToArray(),
                item => item.ToString()!.EndsWith(": Beta", StringComparison.Ordinal));
            await Choose(route);
            f.Admission.Calls.Should().Be(0);
            Click(route, "RoutingSubmit");
            var receipt = (await operation)!;
            receipt.Target.Should().Be(f.Second);
            receipt.Submission.Message.Body.Should().Be("unchanged original");
            f.Admission.Calls.Should().Be(1);
            f.Matches.Should().Be(0);
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("source")]
    [InlineData("privacy")]
    [InlineData("rename")]
    [InlineData("dispose")]
    public async Task AdviceDisclosureWithdrawalCannotDiscloseOrAcceptALateConsent(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new LocalComposition();
            f.Ui.AutoAllowAdvice = false;
            await f.Initialize(expanded: false);
            var opening = new TaskCompletionSource<SessionRoutingAdviceWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingAdviceWindow>((window, _) => opening.TrySetResult(window));
            var live = true;
            var operation = f.Routing.RouteAsync("private original", RequestOrigin.ActivatedVoice, () => live);
            var window = await opening.Task;
            var late = window.GetVisualDescendants().OfType<Button>().Single(item => item.Name is "RoutingAdviceAllow");
            if (change is "cancel") { ClickAdvice(window, "RoutingAdviceCancel"); }
            if (change is "source") { live = false; }
            if (change is "privacy") { f.Ui.Native.Main.CloseForObservedPrivacyEvent("Synthetic advice closure", hidePresentation: true); }
            if (change is "rename")
            {
                await f.Ui.Native.Sessions.RenameAsync(f.Ui.First.Authority.SessionId, f.Ui.First.Authority.Generation,
                    f.Ui.First.Metadata!.Revision.Value, new("Changed"), RequestOrigin.LocalUi, f.Ui.Native.Token);
            }
            if (change is "dispose") { f.Routing.Dispose(); }
            late.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            try { (await operation).Should().BeNull(); }
            catch (OperationCanceledException) when (change is not "cancel") { }
            f.Adapter.Calls.Should().Be(0);
            window.Content.Should().BeNull();
            (await f.Ui.Native.Interactions.ReadConversationPageAsync(f.Ui.First.Authority.SessionId, null, 50, f.Ui.Native.Token))
                .Messages.Should().BeEmpty();
        });
    }
}
