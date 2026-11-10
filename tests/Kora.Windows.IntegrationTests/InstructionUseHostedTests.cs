using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Dependencies;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class InstructionUseHostedTests
{
    [Theory]
    [InlineData("confirm")]
    [InlineData("hide")]
    [InlineData("minimize")]
    [InlineData("leave")]
    public async Task HostedInstructionReviewRequiresSeparateConfirmationAndNeverRestoresHiddenAuthority(string action)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await InstructionUseWorkflowTests.Fixture.CreateAsync();
            await using var controller = new InstructionUseController(f.Native.Main, f.Workflow,
                () => f.Native.Access.Open, () => f.Native.Access.ControlRevision, NullLogger<InstructionUseController>.Instance);
            using var responses = new ResponseWindowController(f.Native.Main, NullLogger<ResponseWindowController>.Instance);
            controller.BindWorkbench(responses.PrepareWorkbenchOwner, view =>
            {
                var owner = responses.PrepareWorkbenchOwner();
                owner.ShowSessions(expanded: true);
                owner.ShowNativeReview(view, "Review one owned instruction request", view,
                    () => f.Native.Main.CanRevealPrivatePresentation, _ => { });
            });
            var windows = new List<Window>();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => windows.Add(window));
            controller.Bind();
            f.Native.Main.CommandText = "instructions request review \"example\" in \"Instruction target\" -- Original exact request.";
            await f.Native.Main.RunTypedCommand.ExecuteAsync();
            var view = controller.View!;
            var shell = responses.PrepareWorkbenchOwner();
            view.Review.Text.Should().Contain(f.Source.Document.Text).And.Contain("Original exact request.");
            view.Confirm.IsEnabled.Should().BeTrue();
            f.Adapter.Calls.Should().Be(0);
            (await f.Messages()).Messages.Should().BeEmpty();
            switch (action)
            {
                case "confirm": view.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); break;
                case "hide": shell.Hide(); shell.Show(); shell.Activate(); break;
                case "minimize":
                    shell.WindowState = WindowState.Minimized;
                    shell.WindowState = WindowState.Normal;
                    break;
                case "leave":
                    var other = new TextBlock { Text = "Another exact review" };
                    shell.ShowNativeReview(other, "Another review", other, static () => true, _ => { });
                    break;
            }
            await controller.ActiveOperation.WaitAsync(TimeSpan.FromSeconds(10), f.Native.Token);
            Dispatcher.UIThread.RunJobs();
            if (action is "confirm")
            {
                (await f.Messages()).Messages.Should().ContainSingle(message => message.Body == "Original exact request."
                    && message.AssistantResponse != null);
                view.Result.Text.Should().Contain("A genuine synthetic local answer.");
            }
            else
            {
                view.Review.Text.Should().BeEmpty();
                view.Confirm.IsEnabled.Should().BeFalse();
                view.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.ActiveOperation;
                f.Adapter.Calls.Should().Be(0);
                (await f.Messages()).Messages.Should().BeEmpty();
            }
            windows.Distinct().Should().Equal(shell);
        });
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("minimize")]
    [InlineData("leave")]
    public async Task HidingConfirmedHostedInstructionWorkDoesNotStopOrPublishItsLateAnswer(string action)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await InstructionUseWorkflowTests.Fixture.CreateAsync();
            await using var controller = new InstructionUseController(f.Native.Main, f.Workflow,
                () => f.Native.Access.Open, () => f.Native.Access.ControlRevision, NullLogger<InstructionUseController>.Instance);
            using var responses = new ResponseWindowController(f.Native.Main, NullLogger<ResponseWindowController>.Instance);
            controller.BindWorkbench(responses.PrepareWorkbenchOwner, view =>
            {
                var owner = responses.PrepareWorkbenchOwner();
                owner.ShowSessions(expanded: true);
                owner.ShowNativeReview(view, "Review one owned instruction request", view,
                    () => f.Native.Main.CanRevealPrivatePresentation, _ => { });
            });
            controller.Bind();
            f.Native.Main.CommandText = "instructions request review \"example\" in \"Instruction target\" -- Original.";
            await f.Native.Main.RunTypedCommand.ExecuteAsync();
            var view = controller.View!;
            var shell = responses.PrepareWorkbenchOwner();
            var reply = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Adapter.Pending = reply.Task;
            view.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var submitting = controller.ActiveOperation;
            try
            {
                await f.Adapter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Native.Token);
                switch (action)
                {
                    case "hide": shell.Hide(); shell.Show(); shell.Activate(); break;
                    case "minimize":
                        shell.WindowState = WindowState.Minimized;
                        shell.WindowState = WindowState.Normal;
                        break;
                    case "leave":
                        var other = new TextBlock { Text = "Another exact review" };
                        shell.ShowNativeReview(other, "Another review", other, static () => true, _ => { });
                        break;
                }
                submitting.IsCompleted.Should().BeFalse();
                view.Review.Text.Should().BeEmpty();
            }
            finally
            {
                reply.TrySetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("Retained exact late answer.", null)));
            }
            await submitting.WaitAsync(TimeSpan.FromSeconds(10), f.Native.Token);
            (await f.Messages()).Messages.Should().ContainSingle().Which.AssistantResponse!.Body.Should().Be("Retained exact late answer.");
            view.Result.Text.Should().BeEmpty("a hidden old review must not publish after restoration");
            view.Confirm.IsEnabled.Should().BeFalse();
        });
    }
}
