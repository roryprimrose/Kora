using Avalonia.Controls;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionRoutingTests
{
    [Theory]
    [InlineData("foreground")]
    [InlineData("other")]
    [InlineData("new")]
    public async Task ForegroundVoiceNativeReviewSkipsAdviceAndRequiresAcknowledgment(string destination)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture { AutoAllowAdvice = false };
            await f.Initialize(expanded: true);
            var foreground = new ForegroundConversationTarget(f.Second, static () => true);
            var opening = new TaskCompletionSource<SessionRoutingWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((view, _) => opening.TrySetResult(view));
            var operation = f.Routing.RouteAsync("original voice request", RequestOrigin.ActivatedVoice, static () => true, foreground);
            var window = await AwaitOpening(operation, opening.Task);
            var choices = Find<ComboBox>(window, "RoutingDestinations");
            choices.SelectedItem!.ToString().Should().EndWith(": Beta");
            f.Matches.Should().Be(0);
            Find<Button>(window, "RoutingSubmit").IsEnabled.Should().BeFalse();
            Click(window, "RoutingSubmit");
            operation.IsCompleted.Should().BeFalse();
            f.Admission.Calls.Should().Be(0);
            if (destination is "other")
            {
                Click(window, "RoutingCorrect");
                choices.SelectedIndex = Array.FindIndex(choices.Items.Cast<object>().ToArray(),
                    item => item.ToString()!.EndsWith(": Alpha", StringComparison.Ordinal));
            }
            if (destination is "new") { choices.SelectedIndex = choices.ItemCount - 1; }
            await Choose(window);
            Find<Button>(window, "RoutingSubmit").IsEnabled.Should().BeTrue();
            Click(window, "RoutingSubmit");
            var receipt = (await operation)!;
            receipt.Target.Metadata!.Name.Value.Should().Be(destination is "foreground" ? "Beta"
                : destination is "other" ? "Alpha" : "New conversation");
            receipt.Submission.Message.Body.Should().Be("original voice request");
            receipt.Submission.Message.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
            f.Admission.Calls.Should().Be(1);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForegroundNativeReviewCannotSubmitAfterTargetRetires(bool acknowledged)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture { AutoAllowAdvice = false };
            await f.Initialize(expanded: true);
            var foreground = new ForegroundConversationTarget(f.Second, static () => true);
            var opening = new TaskCompletionSource<SessionRoutingWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((view, _) => opening.TrySetResult(view));
            var operation = f.Routing.RouteAsync("held voice request", RequestOrigin.ActivatedVoice, static () => true, foreground);
            var window = await AwaitOpening(operation, opening.Task);
            if (acknowledged) { await Choose(window); }
            foreground.Retire();
            Click(window, "RoutingSubmit");
            Click(window, "RoutingCancel");
            (await operation).Should().BeNull();
            f.Admission.Calls.Should().Be(0);
            f.Matches.Should().Be(0);
        });
    }
}
