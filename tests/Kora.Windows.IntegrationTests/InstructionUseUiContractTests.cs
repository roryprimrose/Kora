using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Dependencies;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class InstructionUseUiContractTests
{
    [Theory]
    [InlineData("stop")]
    [InlineData("close")]
    [InlineData("privacy")]
    [InlineData("exit")]
    public async Task NativeStopClosePrivacyAndExitRetainOwnershipUntilCancellationIgnoringModelTerminates(string action)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await InstructionUseWorkflowTests.Fixture.CreateAsync();
            await using var controller = new InstructionUseController(f.Native.Main, f.Workflow,
                () => f.Native.Access.Open, () => f.Native.Access.ControlRevision, NullLogger<InstructionUseController>.Instance);
            InstructionUseWindow? opened = null;
            using var windows = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            { if (window is InstructionUseWindow instruction) { opened = instruction; } });
            controller.Bind();
            f.Native.Main.CommandText = "instructions request review \"example\" in \"Instruction target\" -- Original.";
            await f.Native.Main.RunTypedCommand.ExecuteAsync();
            var native = opened!;
            var reply = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Adapter.Pending = reply.Task;
            native.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var submitting = controller.ActiveOperation;
            await Task.WhenAny(submitting, f.Adapter.Entered.Task);
            if (submitting.IsCompleted) { await submitting; }
            f.Adapter.Calls.Should().Be(1);
            Task stopping;
            if (action is "stop")
            {
                f.Native.Main.CommandText = "instructions request stop";
                stopping = f.Native.Main.RunTypedCommand.ExecuteAsync();
            }
            else if (action is "exit") { stopping = f.Native.Main.ExitAsync(); }
            else
            {
                if (action is "close") { native.Close(); }
                else { f.Native.Access.SetOpen(false); await f.Native.Main.PrivacyClosureTask; }
                stopping = controller.DisposeAsync().AsTask();
            }
            stopping.IsCompleted.Should().BeFalse();
            native.Result.Text.Should().BeEmpty();
            native.Review.Text.Should().BeEmpty();
            reply.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("Late answer must not publish.", null)));
            await stopping;
            await submitting;
            (await f.Messages()).Messages.Should().ContainSingle().Which.AssistantResponse.Should().BeNull();
            native.Result.Text.Should().BeEmpty();
            native.IsVisible.Should().BeFalse();
        });
    }

    [Theory]
    [InlineData("confirm")]
    [InlineData("typed-confirm")]
    [InlineData("cancel")]
    [InlineData("close")]
    [InlineData("privacy")]
    [InlineData("exit")]
    public async Task RealTypedRouteAndNativeControlsShareCompleteReviewAndSeparateSingleUseSubmission(string action)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await InstructionUseWorkflowTests.Fixture.CreateAsync();
            await using var controller = new InstructionUseController(f.Native.Main, f.Workflow,
                () => f.Native.Access.Open, () => f.Native.Access.ControlRevision, NullLogger<InstructionUseController>.Instance);
            InstructionUseWindow? opened = null;
            using var windows = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            { if (window is InstructionUseWindow instruction) { opened = instruction; } });
            controller.Bind();
            f.Native.Main.CommandText = "instructions request review \"example\" in \"Instruction target\" -- Original exact request.";
            await f.Native.Main.RunTypedCommand.ExecuteAsync();
            opened.Should().NotBeNull();
            var native = opened!;
            native.Review.Text.Should().Contain(f.Source.Document.Text).And.Contain("Original exact request.")
                .And.Contain("Instruction target").And.Contain("LocalUi").And.Contain("Local-only");
            native.Confirm.IsEnabled.Should().BeTrue();
            (await f.Messages()).Messages.Should().BeEmpty();
            f.Adapter.Calls.Should().Be(0);
            if (action is "confirm") { native.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await controller.ActiveOperation; }
            if (action is "typed-confirm")
            {
                f.Native.Main.CommandText = "instructions request confirm";
                await f.Native.Main.RunTypedCommand.ExecuteAsync();
            }
            if (action is "cancel") { native.Cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await controller.ActiveOperation; }
            if (action is "close") { native.Close(); }
            if (action is "privacy") { f.Native.Access.SetOpen(false); await f.Native.Main.PrivacyClosureTask; }
            if (action is "exit") { await f.Native.Main.ExitAsync(); }
            await controller.ActiveOperation;
            if (action is "confirm" or "typed-confirm")
            {
                native.Result.Text.Should().Contain("Genuine local ASSISTANT").And.Contain("A genuine synthetic local answer.");
                native.Confirm.IsEnabled.Should().BeFalse();
                (await f.Messages()).Messages.Should().ContainSingle(message => message.Body == "Original exact request."
                    && message.AssistantResponse != null);
                f.Native.Main.CommandText = "instructions request confirm";
                await f.Native.Main.RunTypedCommand.ExecuteAsync();
                f.Adapter.Calls.Should().Be(1);
            }
            else
            {
                f.Adapter.Calls.Should().Be(0);
                native.Review.Text.Should().BeEmpty();
                native.Result.Text.Should().BeEmpty();
                native.IsVisible.Should().BeFalse();
            }
        });
    }
}
