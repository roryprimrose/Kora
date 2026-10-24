using Avalonia.Controls;
using Avalonia.Interactivity;
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
public sealed class AgentProfileEditorUiTests
{
    [Fact]
    public async Task ActualCommandsNativeConfirmationDurableInventoryAndReadOnlyReopenCompleteOperation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
            var store = new WindowsOwnedAgentProfileStore(f);
            var workflow = new AgentProfileSaveWorkflow(store, admission, f.Audit);
            var controller = new AgentProfileEditorController(f.Main, workflow, () => true, () => 1,
                NullLogger<AgentProfileEditorController>.Instance);
            AgentProfileEditorWindow? editor = null;
            Window? dialog = null;
            TaskCompletionSource<Window>? nextDialog = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window is AgentProfileEditorWindow selected) { editor = selected; }
                else { dialog = window; nextDialog?.TrySetResult(window); }
            });
            controller.Bind();
            async Task Run(string command)
            { f.Main.CommandText = command; await f.Main.RunTypedCommand.ExecuteAsync(); }
            await Run("agent author create example");
            editor.Should().NotBeNull();
            await Run("agent author source " + WindowsOwnedAgentProfileStoreTests.Source);
            await Run("agent author validate");
            var target = Path.Combine(f.RoamingRoot, "OwnedAgents-v1", "example.agent.md");
            File.Exists(target).Should().BeFalse();
            await Run("agent author save as new");
            editor!.Review.Text.Should().Contain(WindowsOwnedAgentProfileStoreTests.Source).And.Contain(target);
            File.Exists(target).Should().BeFalse();
            editor.Confirm.IsEnabled.Should().BeTrue();
            nextDialog = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Click(editor.Confirm);
            (await Task.WhenAny(controller.ActiveOperation, nextDialog.Task)).Should().BeSameAs(nextDialog.Task,
                "native confirmation must open; status: {0}", editor.Status.Text);
            dialog = await nextDialog.Task.WaitAsync(TestContext.Current.CancellationToken);
            File.Exists(target).Should().BeFalse();
            Click(dialog!, "Save as new");
            await controller.ActiveOperation;
            File.ReadAllText(target).Should().Be(WindowsOwnedAgentProfileStoreTests.Source);
            editor.Source.IsReadOnly.Should().BeTrue();
            await Run("agent author list");
            editor.Inventory.SelectedItem.Should().BeNull();
            await Run("agent author inspect example");
            editor.Source.Text.Should().Be(WindowsOwnedAgentProfileStoreTests.Source);
            editor.Source.IsReadOnly.Should().BeTrue();
            editor.Status.Text.Should().Contain("Not selected, enabled or invoked");
            await f.Main.ExitAsync();
            editor.IsVisible.Should().BeFalse();
        });
    }

    [Fact]
    public async Task EditingRetiresReviewAndDirtyCloseCancelDiscardAndSaveRetainNativeChoice()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
            var controller = new AgentProfileEditorController(f.Main, new(new WindowsOwnedAgentProfileStore(f), admission, f.Audit),
                () => true, () => 1, NullLogger<AgentProfileEditorController>.Instance);
            AgentProfileEditorWindow? editor = null;
            Window? dialog = null;
            TaskCompletionSource<Window>? nextDialog = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window is AgentProfileEditorWindow selected) { editor = selected; }
                else { dialog = window; nextDialog?.TrySetResult(window); }
            });
            controller.Bind();
            async Task Run(string command)
            { f.Main.CommandText = command; await f.Main.RunTypedCommand.ExecuteAsync(); }
            await Run("agent author create example");
            editor!.Source.Text = "Invalid source";
            await Run("agent author validate");
            editor.Status.Text.Should().Contain("unavailable");
            editor!.Source.Text = WindowsOwnedAgentProfileStoreTests.Source;
            await Run("agent author save as new");
            editor.Source.Text += "\nEdited.";
            editor.Confirm.IsEnabled.Should().BeFalse();
            editor.Review.Text.Should().BeEmpty();
            var exit = f.Main.ExitAsync();
            Click(dialog!, "Cancel");
            var cancelled = async () => await exit;
            await cancelled.Should().ThrowAsync<OperationCanceledException>();
            editor.IsVisible.Should().BeTrue();
            var savedExit = f.Main.ExitAsync();
            nextDialog = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Click(dialog!, "Save");
            // Save on close still presents the complete separate native new-file confirmation.
            (await Task.WhenAny(savedExit, nextDialog.Task)).Should().BeSameAs(nextDialog.Task,
                "Save on close must open exact native confirmation; status: {0}", editor.Status.Text);
            dialog = await nextDialog.Task;
            dialog!.Title.Should().Be("Confirm Save as new");
            Click(dialog, "Cancel");
            var cancelledSave = async () => await savedExit;
            await cancelledSave.Should().ThrowAsync<OperationCanceledException>();
            File.Exists(Path.Combine(f.RoamingRoot, "OwnedAgents-v1", "example.agent.md")).Should().BeFalse();
            var discarded = f.Main.ExitAsync();
            Click(dialog!, "Discard");
            await discarded;
            editor.IsVisible.Should().BeFalse();
        });
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Click(Window window, string label) =>
        Click(window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, label)));
}
