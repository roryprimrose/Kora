using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AwesomeAssertions;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionInstructionScopeWorkbenchTests
{
    [Theory]
    [InlineData("complete")]
    [InlineData("hide")]
    [InlineData("minimize")]
    [InlineData("review-tab")]
    [InlineData("selection")]
    [InlineData("read-hide")]
    [InlineData("read-owner-close")]
    [InlineData("stale-confirm")]
    [InlineData("stale-stop")]
    [InlineData("stale-review")]
    [InlineData("stale-refresh")]
    public async Task HostedScopePinsOriginalSessionRetiresUnconfirmedReadsAndKeepsConfirmedApplicability(string action)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await SessionInstructionScopeWorkflowTests.Fixture.CreateAsync();
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            var shell = new ResponseWindow(native.Main);
            var windows = new List<Window>();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => windows.Add(window));
            var failures = new List<string>();
            var controller = new SessionInstructionScopeController(static () => static () => true,
                () => native.Main.CanRevealPrivatePresentation, failures.Add, f.Commands, f.Scopes,
                () => f.Model, () => shell);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var held = action is "read-hide" or "read-owner-close";
            if (held) { f.Source.BeforeRead = async () => { entered.TrySetResult(); await release.Task; }; }
            try
            {
                var target = f.Model.SelectedSessionRecord!;
                controller.Open();
                var view = controller.View!;
                view.Sources.SelectedItem = f.Commands.Choices.Single();
                f.Source.Reads.Should().Be(0);
                view.Review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (held) { await entered.Task; }
                else
                {
                    await controller.Operation;
                    view.Preview.Text.Should().Contain(f.Source.Snapshot!.Document.Text);
                    view.Confirm.IsEnabled.Should().BeTrue();
                    f.Adapter.Contexts.Should().BeEmpty();
                }
                if (action is "complete")
                {
                    view.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await controller.Operation;
                    f.Scopes.Status(target).Should().StartWith("Applying example");
                }
                var reads = f.Source.Reads;
                if (action is "selection")
                {
                    var other = await f.Workspace.CreateAsync(new("Other conversation"), RequestOrigin.LocalUi, f.Storage.Token);
                    await f.Model.RefreshAsync();
                    await f.Model.SelectAsync(f.Model.Sessions.Single(row => row == other));
                    view.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await controller.Operation;
                    f.Scopes.Status(other).Should().StartWith("No session instruction applies.");
                }
                else if (action is "review-tab")
                {
                    var other = new TextBlock { Text = "Another exact review" };
                    shell.ShowNativeReview(other, "Another review", other, static () => true, _ => { });
                }
                else if (action is "minimize")
                { shell.WindowState = WindowState.Minimized; shell.WindowState = WindowState.Normal; }
                else if (action is "read-owner-close") { shell.Close(); }
                else { shell.Hide(); shell.Show(); shell.Activate(); }
                if (held)
                {
                    controller.Operation.IsCompleted.Should().BeFalse("an ignoring source read remains owned after presentation ends");
                    controller.CloseOperation.IsCompleted.Should().BeFalse();
                    release.TrySetResult();
                    Func<Task> read = async () => await controller.Operation;
                    await read.Should().ThrowAsync<OperationCanceledException>();
                }
                await controller.CloseOperation;
                Dispatcher.UIThread.RunJobs();
                view.Confirm.IsEnabled.Should().BeFalse();
                view.Preview.Text.Should().NotContain(f.Source.Snapshot!.Document.Text);
                f.Commands.Pending.Should().BeNull();
                f.Source.Reads.Should().Be(reads, "presentation changes must not repeat exact-source inspection");
                f.Storage.Count("conversation_messages").Should().Be(0);
                f.Storage.Count("scoped_grants").Should().Be(0);
                if (action is "complete")
                {
                    f.Scopes.Status(target).Should().StartWith("Applying example", "hiding the controls is not Stop");
                    var guided = await f.Conversation.SubmitAsync(target, "Exact original scoped request", static () => true, f.Storage.Token);
                    guided.Message.AssistantResponse!.Body.Should().Be("Genuine guided local answer");
                    controller.Open();
                    controller.View.Should().NotBeSameAs(view);
                    controller.View!.IsNativePresented.Should().BeTrue();
                    controller.View.Stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await controller.Operation;
                    failures.Should().BeEmpty(controller.View?.Preview.Text);
                    f.Scopes.Status(target).Should().StartWith("No session instruction applies.");
                    var bare = await f.Conversation.SubmitAsync(target, "After explicit Stop", static () => true, f.Storage.Token);
                    bare.Message.AssistantResponse!.Body.Should().Be("Genuine context-free answer");
                    f.Adapter.Guidance.Should().Equal(true, false);
                    failures.Should().BeEmpty();
                }
                else
                {
                    f.Scopes.Status(target).Should().StartWith("No session instruction applies.");
                    f.Adapter.Contexts.Should().BeEmpty();
                    if (action is not ("selection" or "read-owner-close"))
                    {
                        if (action.StartsWith("stale-", StringComparison.Ordinal))
                        {
                            var other = await f.Workspace.CreateAsync(new("Fresh scope conversation"), RequestOrigin.LocalUi, f.Storage.Token);
                            await f.Model.RefreshAsync();
                            await f.Model.SelectAsync(f.Model.Sessions.Single(row => row == other));
                        }
                        controller.Open();
                        controller.View.Should().NotBeSameAs(view);
                        controller.View!.Confirm.IsEnabled.Should().BeFalse();
                        f.Source.Reads.Should().Be(reads, "reopening is not source-read consent");
                        if (action.StartsWith("stale-", StringComparison.Ordinal))
                        {
                            var fresh = controller.View!;
                            var freshTarget = f.Model.SelectedSessionRecord!;
                            fresh.Sources.SelectedItem = f.Commands.Choices.Single();
                            fresh.Review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            await controller.Operation;
                            var pending = f.Commands.Pending;
                            var reviewedReads = f.Source.Reads;
                            var retiredAction = action switch
                            {
                                "stale-confirm" => view.Confirm,
                                "stale-stop" => view.Stop,
                                "stale-review" => view.Review,
                                "stale-refresh" => view.Refresh,
                                _ => throw new InvalidOperationException("Unknown stale control."),
                            };
                            retiredAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            await controller.Operation;
                            pending.Should().NotBeNull();
                            f.Commands.Pending.Should().BeSameAs(pending, "a retired control cannot consume or retire another session's fresh review");
                            f.Source.Reads.Should().Be(reviewedReads);
                            fresh.Confirm.IsEnabled.Should().BeTrue();
                            f.Scopes.Status(freshTarget).Should().StartWith("No session instruction applies.");
                            fresh.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            await controller.Operation;
                            f.Scopes.Status(freshTarget).Should().StartWith("Applying example");
                            view.Stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            await controller.Operation;
                            f.Scopes.Status(freshTarget).Should().StartWith("Applying example", "retired Stop cannot target the fresh session");
                            f.Scopes.Status(target).Should().StartWith("No session instruction applies.");
                        }
                    }
                }
                windows.Distinct().Should().Equal(shell);
            }
            finally { release.TrySetResult(); await controller.ShutdownAsync(); shell.Close(); }
        });
    }
}
