using AwesomeAssertions;
using Avalonia.Controls;
using Avalonia.Interactivity;

using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionInstructionScopeUiTests
{
    [Fact]
    public async Task NativeRefreshAfterTargetDisappearsReportsUnavailableWithoutAnUnhandledEventFailure()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await SessionInstructionScopeWorkflowTests.Fixture.CreateAsync();
            SessionsViewModel? selected = f.Model;
            var failures = new List<string>();
            var controller = new SessionInstructionScopeController(static () => static () => true, static () => true,
                failures.Add, f.Commands, f.Scopes, () => selected, static () => null);
            controller.Open();
            var window = controller.Window!;
            selected = null;
            window.Refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Confirm.IsEnabled.Should().BeFalse();
            window.Preview.Text.Should().StartWith("Unconfirmed:");
            failures.Should().ContainSingle().Which.Should().Contain("Active session");
            f.Commands.Pending.Should().BeNull();
            await controller.ShutdownAsync();
        });
    }

    [Fact]
    public async Task CloseJoinsCancellationIgnoringReadAndCannotPublishLateReviewOrBinding()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await SessionInstructionScopeWorkflowTests.Fixture.CreateAsync();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Source.BeforeRead = async () => { entered.TrySetResult(); await release.Task; };
            var failures = new List<string>();
            var controller = new SessionInstructionScopeController(static () => static () => true, static () => true,
                failures.Add, f.Commands, f.Scopes, () => f.Model, static () => null);
            var read = controller.ExecuteAsync(new(SessionCommandOperation.InstructionScopeReview)
            { ScopeInstructionName = "example" }, RequestOrigin.ActivatedVoice, static () => true, f.Storage.Token);
            await entered.Task;
            var window = controller.Window!;
            window.Close();
            var joined = controller.CloseOperation;
            joined.IsCompleted.Should().BeFalse();
            f.Storage.Count("conversation_messages").Should().Be(0);
            release.SetResult();
            await read.Invoking(async task => await task).Should().ThrowAsync<OperationCanceledException>();
            await joined;
            window.Preview.Text.Should().BeEmpty();
            f.Commands.Pending.Should().BeNull();
            f.Scopes.Status(f.Model.SelectedSessionRecord!).Should().StartWith("No session instruction applies.");
            f.Adapter.Guidance.Should().BeEmpty();
            failures.Should().ContainSingle();
            await controller.ShutdownAsync();
        });
    }
}
