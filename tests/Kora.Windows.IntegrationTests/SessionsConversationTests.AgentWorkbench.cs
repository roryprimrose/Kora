using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Core.Dependencies;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("invoke", 1120)]
    [InlineData("invoke", 640)]
    [InlineData("hide", 1120)]
    [InlineData("minimize", 640)]
    [InlineData("review-tab", 1120)]
    [InlineData("hide-running", 1120)]
    [InlineData("minimize-running", 640)]
    [InlineData("review-tab-running", 1120)]
    public async Task HostedAgentRunRequiresFreshCompleteReviewAndRetainsOnlyExactConfirmedAnswer(string action, int width)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var running = action.EndsWith("-running", StringComparison.Ordinal);
            if (running) { action = action[..^"-running".Length]; }
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new HistoryAdapter();
            var held = running ? new IgnoringAgentAdapter() : null;
            await using var composed = new Composition(f, adapter: held is null ? adapter : held);
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            var reader = new NativeAgentReader();
            await using var workflow = new BoundedAgentWorkflow(reader, f.Store, composed.Access,
                composed.Conversation, composed.Turns, composed.Audit, f.Time);
            var controller = new AgentRunController(native.Main, workflow, composed.Workspace,
                static () => true, static () => 1, NullLogger<AgentRunController>.Instance);
            using var responses = new ResponseWindowController(native.Main, NullLogger<ResponseWindowController>.Instance);
            controller.BindWorkbench(responses.PrepareWorkbenchOwner, view =>
            {
                var owner = responses.PrepareWorkbenchOwner();
                owner.ShowSessions(expanded: true);
                owner.ShowNativeReview(view, "One reviewed local bounded agent", view,
                    () => native.Main.CanRevealPrivatePresentation, _ => { });
            });
            var windows = new List<Window>();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => windows.Add(window));
            controller.Bind();
            try
            {
                native.Main.CommandText = "review agent";
                await native.Main.RunTypedCommand.ExecuteAsync();
                var view = controller.View!;
                var shell = responses.PrepareWorkbenchOwner();
                shell.Width = width;
                var buttons = view.GetVisualDescendants().OfType<Button>().ToArray();
                buttons.Single(button => Equals(button.Content, "Refresh existing Active conversations"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.ActiveOperation.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
                view.Targets.SelectedIndex = 0;
                view.Source.Text = @"C:\Users\owner\existing.agent.md";
                view.Input.Text = "Original exact hosted request.";
                buttons.Single(button => Equals(button.Content, "Read source and review exact invocation"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.ActiveOperation.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
                view.Review.Text.Should().Contain(reader.Text).And.Contain(view.Input.Text);
                view.Confirm.IsEnabled.Should().BeTrue(view.Status.Text);
                adapter.Calls.Should().Be(0);
                if (held is not null)
                {
                    view.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
                }
                switch (action)
                {
                    case "invoke": view.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); break;
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
                if (held is not null)
                {
                    controller.ActiveOperation.IsCompleted.Should().BeFalse();
                    view.Review.Text.Should().BeEmpty();
                    held.Reply.TrySetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                        new("Exact retained hosted answer.", null)));
                }
                await controller.ActiveOperation.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
                Dispatcher.UIThread.RunJobs();
                var messages = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages;
                if (held is not null)
                {
                    messages.Should().ContainSingle().Which.AssistantResponse!.Body.Should().Be("Exact retained hosted answer.");
                    view.Result.Text.Should().BeEmpty("presentation loss does not republish the admitted task answer on return");
                }
                else if (action is "invoke")
                {
                    adapter.Calls.Should().Be(1);
                    messages.Should().ContainSingle().Which.Body.Should().Be("Original exact hosted request.");
                    view.Result.Text.Should().Be("earlier ASSISTANT");
                }
                else
                {
                    view.Review.Text.Should().BeEmpty();
                    view.Confirm.IsEnabled.Should().BeFalse();
                    view.Input.Text.Should().Be("Original exact hosted request.");
                    view.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await controller.ActiveOperation;
                    (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Should().BeEmpty();
                    adapter.Calls.Should().Be(0);
                }
                windows.Distinct().Should().Equal(shell);
            }
            finally
            {
                held?.Reply.TrySetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                    new("Released synthetic answer.", null)));
                await native.Main.ExitAsync();
            }
        });
    }
}
