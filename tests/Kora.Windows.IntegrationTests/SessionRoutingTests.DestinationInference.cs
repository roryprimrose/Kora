using Avalonia.Controls;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Conversation.SessionRouting;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionRoutingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeLastSelectedChatIsPublishedWithoutForegroundFocus(bool replace)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture(syntheticVoice: true) { StandingAdvice = false };
            await f.Initialize(expanded: true);
            if (replace)
            { await f.Model.SelectAsync(f.Model.Sessions.Single(row => row.Authority.SessionId == f.Second.Authority.SessionId)); }
            HeadlessSession.SetActivation(f.Window!, false);
            var native = f.Native;
            native.Main.BindVoiceOwnershipGate(static () => true);
            await native.Main.SetVoiceConsentAsync(true);
            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            native.Main.BindDestinationSessionRouting((body, origin, admitted, foreground, last) =>
            {
                body.Should().Be("compare original plans");
                origin.Should().Be(RequestOrigin.ActivatedVoice);
                foreground.Should().BeNull();
                last.Should().NotBeNull();
                last!.Entry.Should().Be(replace ? f.Second : f.First);
                last.IsResourceCurrent.Should().BeTrue();
                admitted().Should().BeTrue();
                reached.TrySetResult();
                return Task.FromResult<Kora.Application.Hosting.SessionRouting.SessionRoutingReceipt?>(null);
            });
            await native.Main.BeginPushToTalkCommand.ExecuteAsync();
            await native.Main.EndPushToTalkCommand.ExecuteAsync();
            native.Voice.PublishTranscript("Kora, compare original plans");
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(15), native.Token);
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Theory]
    [InlineData("inferred")]
    [InlineData("unavailable")]
    [InlineData("ambiguous")]
    [InlineData("off")]
    public async Task NonforegroundVoiceNamesPreselectionBasisAndCannotSubmitBeforeAcknowledgment(string mode)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture { AutoAllowAdvice = false, StandingAdvice = mode is not "off" };
            await f.Initialize(expanded: true);
            f.Match = mode switch
            {
                "unavailable" => SessionRouteMatch.Unavailable,
                "ambiguous" => SessionRouteMatch.Ambiguous,
                _ => SessionRouteMatch.Clear,
            };
            var advice = 0;
            using var adviceOpened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingAdviceWindow>((_, _) => advice++);
            var opening = new TaskCompletionSource<SessionRoutingWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((view, _) => opening.TrySetResult(view));
            var last = new ForegroundConversationTarget(f.Second, static () => true);
            last.ReleaseForeground();
            var operation = f.Routing.RouteAsync("original inferred voice", RequestOrigin.ActivatedVoice,
                static () => true, lastSelected: last);
            var window = await AwaitOpening(operation, opening.Task);
            var target = mode is "inferred" ? "Alpha" : "Beta";
            Find<ComboBox>(window, "RoutingDestinations").SelectedItem!.ToString().Should().EndWith(": " + target);
            var basis = mode is "inferred" ? "inferred" : "last selected";
            window.GetVisualDescendants().OfType<TextBlock>().Should().Contain(item => item.Text != null
                && item.Text.Contains(basis, StringComparison.Ordinal));
            advice.Should().Be(0);
            f.Matches.Should().Be(mode is "off" ? 0 : 1);
            Find<Button>(window, "RoutingSubmit").IsEnabled.Should().BeFalse();
            Click(window, "RoutingSubmit");
            operation.IsCompleted.Should().BeFalse();
            f.Admission.Calls.Should().Be(0);
            await Choose(window);
            Click(window, "RoutingSubmit");
            var receipt = (await operation)!;
            receipt.Target.Metadata!.Name.Value.Should().Be(target);
            receipt.Submission.Message.Body.Should().Be("original inferred voice");
            f.Admission.Calls.Should().Be(1);
        });
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("denied")]
    public async Task FailedRealLocalInferenceShowsFailureAndLastSelectedWithoutSubmitting(string fault)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new LocalComposition();
            await f.Initialize(expanded: true);
            f.Adapter.Fault = fault;
            var opening = new TaskCompletionSource<SessionRoutingWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((view, _) => opening.TrySetResult(view));
            var operation = f.Routing.RouteAsync("unchanged", RequestOrigin.ActivatedVoice,
                static () => true, lastSelected: new(f.Ui.Second, static () => true));
            var window = await AwaitOpening(operation, opening.Task);
            Find<ComboBox>(window, "RoutingDestinations").SelectedItem!.ToString().Should().EndWith(": Beta");
            window.GetVisualDescendants().OfType<TextBlock>().Should().Contain(item => item.Text != null
                && item.Text.Contains("inference failed", StringComparison.Ordinal));
            f.Turns.IsQuiescent.Should().BeTrue();
            f.Adapter.Calls.Should().Be(1);
            Click(window, "RoutingCancel");
            (await operation).Should().BeNull();
            (await f.Ui.Native.Interactions.ReadConversationPageAsync(f.Ui.Second.Authority.SessionId,
                null, 50, f.Ui.Native.Token)).Messages.Should().BeEmpty();
        });
    }
}
