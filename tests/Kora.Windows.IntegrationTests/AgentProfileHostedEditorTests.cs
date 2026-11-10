using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;
using Kora.Windows.IntegrationTests.ArtifactDiscovery;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class AgentProfileHostedEditorTests
{
    [Theory]
    [InlineData(true, 1120)]
    [InlineData(false, 1120)]
    [InlineData(true, 640)]
    public async Task HostedAgentEditorWritesOnlyAfterSeparateNativeConfirmationAndKeepsDraftAcrossHide(bool save, int width)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
            var workflow = new AgentProfileSaveWorkflow(new WindowsOwnedAgentProfileStore(f), admission, f.Audit);
            var controller = new AgentProfileEditorController(f.Main, workflow, static () => true, static () => 1,
                NullLogger<AgentProfileEditorController>.Instance);
            using var responses = new ResponseWindowController(f.Main, NullLogger<ResponseWindowController>.Instance);
            controller.BindWorkbench(responses.PrepareWorkbenchOwner,
                view => responses.ShowHostedView(WorkbenchActivity.AgentEditor, view));
            controller.Bind();
            async Task Run(string text)
            {
                f.Main.CommandText = text;
                await f.Main.RunTypedCommand.ExecuteAsync().WaitAsync(TimeSpan.FromSeconds(5), f.Token);
            }
            var extraWindows = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<AgentProfileEditorWindow>((_, _) => extraWindows++);
            await Run("agent author create example");
            var view = controller.View!;
            var shell = responses.PrepareWorkbenchOwner();
            shell.Width = width;
            shell.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var source = WindowsOwnedAgentProfileStoreTests.Source;
            await Run("agent author source " + source);
            await Run("agent author validate");
            await Run("agent author save as new");
            var path = Path.Combine(f.RoamingRoot, "OwnedAgents-v1", "example.agent.md");
            File.Exists(path).Should().BeFalse();
            view.Confirm.IsEnabled.Should().BeTrue(view.Status.Text);
            shell.UpdateLayout();
            var preview = shell.FindControl<ContentControl>("NativeReviewContent")!;
            preview.GetVisualDescendants().Should().Contain(view.Review).And.Contain(view.Confirm);
            view.Confirm.IsEffectivelyVisible.Should().BeTrue("the complete review and its separate action must remain operable in narrow layouts");
            if (!save)
            {
                shell.Hide();
                shell.Show();
                shell.Activate();
                Dispatcher.UIThread.RunJobs();
                view.Confirm.IsEnabled.Should().BeFalse();
                view.Review.Text.Should().BeEmpty();
                view.Source.Text.Should().Be(source);
                File.Exists(path).Should().BeFalse();
            }
            else
            {
                view.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var button = await FindDecision(shell, "Save as new", controller.ActiveOperation, f.Token);
                File.Exists(path).Should().BeFalse("opening final confirmation is not permission to write");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.ActiveOperation.WaitAsync(TimeSpan.FromSeconds(5), f.Token);
                File.ReadAllText(path).Should().Be(source);
                view.Source.IsReadOnly.Should().BeTrue();
                view.Inventory.SelectedItem.Should().BeNull();
                await Run("agent author list");
                await Run("agent author inspect example");
                view.Source.Text.Should().Be(source);
                view.Source.IsReadOnly.Should().BeTrue();
                view.Status.Text.Should().Contain("Not selected, enabled or invoked");
            }
            extraWindows.Should().Be(0);
            var exit = f.Main.ExitAsync();
            if (!save)
            {
                var discard = await FindDecision(shell, "Discard", exit, f.Token);
                discard.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            await exit.WaitAsync(TimeSpan.FromSeconds(5), f.Token);
            controller.View.Should().BeNull();
        });
    }

    private static async Task<Button> FindDecision(ResponseWindow shell, string caption, Task operation, CancellationToken token)
    {
        var ready = new TaskCompletionSource<Button>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check(object? sender, EventArgs args)
        {
            var button = shell.FindControl<ContentControl>("NativeReviewContent")!
                .GetVisualDescendants().OfType<Button>().FirstOrDefault(button => string.Equals(button.Content as string, caption, StringComparison.Ordinal));
            if (button is not null) { ready.TrySetResult(button); }
        }
        shell.LayoutUpdated += Check;
        try
        {
            shell.UpdateLayout();
            Check(null, EventArgs.Empty);
            var next = await Task.WhenAny(ready.Task, operation).WaitAsync(TimeSpan.FromSeconds(5), token);
            if (ReferenceEquals(next, operation))
            { throw new InvalidOperationException("The authoring operation ended before its native decision: " + caption); }
            return await ready.Task;
        }
        finally { shell.LayoutUpdated -= Check; }
    }

    [Fact]
    public async Task HostedAgentDiscardResumesTheOriginalWorkbenchCloseAndDoesNotSave()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
            var workflow = new AgentProfileSaveWorkflow(new WindowsOwnedAgentProfileStore(f), admission, f.Audit);
            var controller = new AgentProfileEditorController(f.Main, workflow, static () => true, static () => 1,
                NullLogger<AgentProfileEditorController>.Instance);
            using var responses = new ResponseWindowController(f.Main, NullLogger<ResponseWindowController>.Instance);
            controller.BindWorkbench(responses.PrepareWorkbenchOwner,
                view => responses.ShowHostedView(WorkbenchActivity.AgentEditor, view));
            controller.Bind();
            await controller.Add("example", string.Empty, Kora.Core.Hosting.RequestOrigin.LocalUi, static () => true);
            controller.View!.Source.Text = WindowsOwnedAgentProfileStoreTests.Source;
            var shell = responses.PrepareWorkbenchOwner();
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            shell.Closed += (_, _) => closed.TrySetResult();
            shell.Close();
            shell.IsVisible.Should().BeTrue("the dirty draft still needs its separate discard decision");
            var discard = await FindDecision(shell, "Discard", closed.Task, f.Token);
            File.Exists(Path.Combine(f.RoamingRoot, "OwnedAgents-v1", "example.agent.md")).Should().BeFalse();
            discard.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5), f.Token);
            controller.View.Should().BeNull();
            shell.IsVisible.Should().BeFalse();
            await f.Main.ExitAsync();
        });
    }
}
