using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Core.Agents;
using Kora.Core.Auditing;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;
using Kora.Windows.IntegrationTests.ArtifactDiscovery;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class AgentProfileEditorUiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualCommandsNativeConfirmationDurableInventoryAndReadOnlyReopenCompleteOperation(bool combined)
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
            var source = WindowsOwnedAgentProfileStoreTests.Source;
            if (combined)
            {
                source = source.Replace("kora-agent-format: 1", "kora-agent-format: 4", StringComparison.Ordinal)
                    .Replace("references: none", "references: disk.kora-profile.instruction.example", StringComparison.Ordinal)
                    .Replace("tools: none", "tools: application.get_version", StringComparison.Ordinal)
                    .Replace("maximum-calls: 0", "maximum-calls: 1", StringComparison.Ordinal);
            }
            await Run("agent author source " + source);
            await Run("agent author validate");
            var target = Path.Combine(f.RoamingRoot, "OwnedAgents-v1", "example.agent.md");
            File.Exists(target).Should().BeFalse();
            await Run("agent author save as new");
            editor!.Review.Text.Should().Contain(source).And.Contain(target);
            if (combined)
            {
                editor.Review.Text.Should().Contain("Declared references: disk.kora-profile.instruction.example")
                    .And.Contain("Declared tools: application.get_version").And.Contain("Host clamps: calls 1");
            }
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
            File.ReadAllText(target).Should().Be(source);
            editor.Source.IsReadOnly.Should().BeTrue();
            await Run("agent author list");
            editor.Inventory.SelectedItem.Should().BeNull();
            await Run("agent author inspect example");
            editor.Source.Text.Should().Be(source);
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

    [Theory]
    [InlineData(false, "Overwrite with Kora changes")]
    [InlineData(true, "Overwrite with Kora changes")]
    [InlineData(false, "Reload from disk (abandon draft)")]
    [InlineData(false, "Cancel (keep draft)")]
    public async Task ExistingOriginalEditReviewConflictRecoverySaveReadbackAndReopenAreNativeAndInert(bool combined, string recovery)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
            var store = new WindowsOwnedAgentProfileStore(f);
            var original = WindowsOwnedAgentProfileStoreTests.Source;
            if (combined)
            {
                original = original.Replace("kora-agent-format: 1", "kora-agent-format: 4", StringComparison.Ordinal)
                    .Replace("references: none", "references: disk.kora-profile.instruction.example", StringComparison.Ordinal)
                    .Replace("tools: none", "tools: application.get_version", StringComparison.Ordinal)
                    .Replace("maximum-calls: 0", "maximum-calls: 1", StringComparison.Ordinal);
            }
            var destination = await store.PrepareNew("example", f.Token);
            _ = await store.Create(destination, OwnedAgentProfileDocument.Parse("example", original), () => true,
                operation => Task.FromResult(operation()), f.Token);
            var audit = new ObservedAudit(f.Audit);
            var controller = new AgentProfileEditorController(f.Main, new(store, admission, audit),
                () => true, () => 1, NullLogger<AgentProfileEditorController>.Instance);
            AgentProfileEditorWindow? editor = null;
            TaskCompletionSource<Window> nextDialog = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window is AgentProfileEditorWindow selected) { editor = selected; }
                else { nextDialog.TrySetResult(window); }
            });
            controller.Bind();
            async Task Run(string command)
            { f.Main.CommandText = command; await f.Main.RunTypedCommand.ExecuteAsync(); }
            async Task<Window> Dialog()
            {
                (await Task.WhenAny(nextDialog.Task, controller.ActiveOperation)).Should().BeSameAs(nextDialog.Task,
                    "the native decision must open; status: {0}", editor!.Status.Text);
                var result = await nextDialog.Task;
                nextDialog = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return result;
            }
            await Run("agent author inspect example");
            editor!.Source.IsReadOnly.Should().BeTrue();
            Click(editor, "Edit existing original");
            await controller.ActiveOperation;
            editor.Source.IsReadOnly.Should().BeFalse();
            editor.NameInput.IsReadOnly.Should().BeTrue();
            var draft = original + "\nNative edited guidance.";
            editor.Source.Text = draft;
            await Run("agent author validate");
            File.ReadAllText(destination.DisplayPath).Should().Be(original);
            await Run("agent author save as new");
            editor.Confirm.IsEnabled.Should().BeFalse("updating cannot masquerade as creating a new copy");
            Click(editor, "Review Save (new or existing original)");
            await controller.ActiveOperation;
            editor.Review.Text.Should().Contain(original).And.Contain(draft).And.Contain(destination.DisplayPath)
                .And.Contain("BEFORE: COMPLETE ORIGINAL SOURCE").And.Contain("AFTER: COMPLETE PROPOSED SOURCE");
            File.ReadAllText(destination.DisplayPath).Should().Be(original);
            Click(editor.Confirm);
            Click(await Dialog(), "Cancel");
            await controller.ActiveOperation;
            File.ReadAllText(destination.DisplayPath).Should().Be(original);
            editor.Source.Text.Should().Be(draft);
            Click(editor, "Review Save (new or existing original)");
            await controller.ActiveOperation;
            editor.Source.Text += "\nInvalidates exact review";
            draft = editor.Source.Text!;
            editor.Confirm.IsEnabled.Should().BeFalse();
            Click(editor, "Review Save (new or existing original)");
            await controller.ActiveOperation;
            var external = original + "\nExternal concurrent edit.";
            await File.WriteAllTextAsync(destination.DisplayPath, external, f.Token);
            Click(editor.Confirm);
            Click(await Dialog(), "Save existing original");
            var conflict = await Dialog();
            conflict.Title.Should().Be("Existing original changed");
            File.ReadAllText(destination.DisplayPath).Should().Be(external);
            Click(conflict, recovery);
            if (recovery is "Reload from disk (abandon draft)")
            {
                Click(await Dialog(), "Discard");
                await controller.ActiveOperation;
                editor.Source.Text.Should().Be(external);
                editor.Source.IsReadOnly.Should().BeTrue();
            }
            else if (recovery is "Cancel (keep draft)")
            {
                await controller.ActiveOperation;
                editor.Source.Text.Should().Be(draft);
                File.ReadAllText(destination.DisplayPath).Should().Be(external);
                var closing = f.Main.ExitAsync();
                Click(await Dialog(), "Cancel");
                var cancelled = async () => await closing;
                await cancelled.Should().ThrowAsync<OperationCanceledException>();
                editor.Source.Text.Should().Be(draft);
                var discard = f.Main.ExitAsync();
                Click(await Dialog(), "Discard");
                await discard;
                return;
            }
            else
            {
                await controller.ActiveOperation;
                editor.Confirm.IsEnabled.Should().BeTrue("overwrite prepares another review, not a write");
                editor.Review.Text.Should().Contain(external).And.Contain(draft);
                File.ReadAllText(destination.DisplayPath).Should().Be(external);
                await File.WriteAllTextAsync(destination.DisplayPath, external + "\nChanged again.", f.Token);
                Click(editor.Confirm);
                Click(await Dialog(), "Save existing original");
                Click(await Dialog(), "Overwrite with Kora changes");
                await controller.ActiveOperation;
                File.ReadAllText(destination.DisplayPath).Should().Be(external + "\nChanged again.");
                Click(editor.Confirm);
                Click(await Dialog(), "Save existing original");
                await controller.ActiveOperation;
                File.ReadAllText(destination.DisplayPath).Should().Be(draft);
                editor.Source.IsReadOnly.Should().BeTrue();
                await Run("agent author inspect example");
                editor.Source.Text.Should().Be(draft);
                editor.Status.Text.Should().Contain("Not selected, enabled or invoked");
                audit.Events.Where(item => string.Equals(item.ActionId, "definition.agent.update", StringComparison.Ordinal)).Select(item => item.Outcome)
                    .Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed,
                        SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed,
                        SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
            }
            (await store.List(f.Token)).Should().Equal("example");
            await f.Main.ExitAsync();
            editor.IsVisible.Should().BeFalse();
        });
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Click(Window window, string label) =>
        Click(window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, label)));

    private sealed class ObservedAudit(ISecurityAuditLog inner) : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent auditEvent)
        {
            inner.Write(auditEvent);
            Events.Add(auditEvent);
        }
    }
}
