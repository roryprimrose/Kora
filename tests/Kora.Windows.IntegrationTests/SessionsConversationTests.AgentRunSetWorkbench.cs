using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("hide", false)]
    [InlineData("minimize", false)]
    [InlineData("review-tab", false)]
    [InlineData("hide", true)]
    [InlineData("minimize", true)]
    [InlineData("review-tab", true)]
    public async Task HostedIndependentRunsRetirePendingReviewsButKeepConfirmedWorkInItsOriginalSessions(string action, bool invoke)
    {
        await WithRunSetAffinity(async ui =>
        {
            var view = ui.View;
            var shell = (ResponseWindow)view.Owner;
            await ui.Review(1, ui.First);
            await ui.Review(2, ui.Second);
            view.Runs.Should().OnlyContain(panel => panel.Confirm.IsEnabled);
            if (invoke)
            {
                await ui.Invoke(1, ui.First);
                await ui.Invoke(2, ui.Second);
            }
            switch (action)
            {
                case "hide": shell.Hide(); shell.Show(); shell.Activate(); break;
                case "minimize":
                    shell.WindowState = WindowState.Minimized;
                    shell.WindowState = WindowState.Normal;
                    break;
                case "review-tab":
                    var other = new TextBlock { Text = "Another exact review" };
                    shell.ShowNativeReview(other, "Another review", other, static () => true, _ => { });
                    break;
            }
            Dispatcher.UIThread.RunJobs();
            view.Runs.Should().OnlyContain(panel => panel.Review.Text == string.Empty && panel.Result.Text == string.Empty
                && !panel.Confirm.IsEnabled);
            if (invoke)
            {
                ui.Controller.ObserveOperation(1).IsCompleted.Should().BeFalse();
                ui.Controller.ObserveOperation(2).IsCompleted.Should().BeFalse();
                ui.Adapter.Inner.Finish(ui.First, "Only first exact session");
                ui.Adapter.Inner.Finish(ui.Second, "Only second exact session");
                await Task.WhenAll(ui.Controller.ObserveOperation(1), ui.Controller.ObserveOperation(2))
                    .WaitAsync(TimeSpan.FromSeconds(10), ui.Storage.Token);
                var first = await ui.Storage.Store.ReadConversationPageAsync(ui.First.Authority.SessionId, null, 25, ui.Storage.Token);
                var second = await ui.Storage.Store.ReadConversationPageAsync(ui.Second.Authority.SessionId, null, 25, ui.Storage.Token);
                first.Messages.Should().ContainSingle().Which.AssistantResponse!.Body.Should().Be("Only first exact session");
                second.Messages.Should().ContainSingle().Which.AssistantResponse!.Body.Should().Be("Only second exact session");
                view.Runs.Should().OnlyContain(panel => panel.Result.Text == string.Empty);
            }
            else
            {
                view.Runs[0].Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await ui.Controller.ObserveOperation(1);
                ui.Adapter.Calls.Should().Be(0);
                var first = await ui.Storage.Store.ReadConversationPageAsync(ui.First.Authority.SessionId, null, 25, ui.Storage.Token);
                var second = await ui.Storage.Store.ReadConversationPageAsync(ui.Second.Authority.SessionId, null, 25, ui.Storage.Token);
                first.Messages.Should().BeEmpty();
                second.Messages.Should().BeEmpty();
            }
        }, hosted: true);
    }
}
