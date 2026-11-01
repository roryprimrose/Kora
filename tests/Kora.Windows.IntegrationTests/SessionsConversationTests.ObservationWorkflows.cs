using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using AwesomeAssertions;
using Kora.Application.Skills;
using Kora.Application.Tools;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Core.Tools;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("complete")]
    [InlineData("request")]
    [InlineData("source")]
    [InlineData("cancel")]
    [InlineData("deny")]
    [InlineData("cancel-first")]
    [InlineData("close-first")]
    [InlineData("escape-first")]
    [InlineData("token-first")]
    [InlineData("owner-shutdown")]
    [InlineData("dirty-close")]
    public async Task NativeObservationWorkflowAuthorsSavesDisabledReviewsEnablesAndRunsOnlyDeclaredApprovedSteps(string operation)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            f.Time.UseFrozenTimers = true;
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            var store = new WindowsOwnedDeclarativeSkillStore(native);
            await using var admission = new OwnedSkillControlAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            var save = new OwnedSkillSaveWorkflow(store, admission, native.Audit);
            var sources = new OwnedSkillInvocationWorkflow(store, admission, composed.Access, f.Store, composed.Workspace,
                composed.Conversation, native.Audit, f.Time);
            using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var boundary = new ObservationBoundary();
            var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
            var registry = new ReadOnlyCapabilityRegistry(boundary, new(), new(), new(boundary), new(dependencies),
                new(runtime), new(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            await using var controller = new ObservationWorkflowController(native.Main, save, sources,
                new(sources, admission, registry, f.Store, dependencies, native.Audit, f.Time, store, new ObservationUsePolicy(), f.Store),
                static () => true, static () => 1, NullLogger<ObservationWorkflowController>.Instance);
            controller.Bind();
            await using var editor = new OwnedSkillEditorController(native.Main, save, admission,
                static () => true, static () => 1, NullLogger<OwnedSkillEditorController>.Instance, observations: controller);
            editor.Bind();
            ObservationWorkflowWindow? window = null;
            var reviews = new List<string>();
            var allowDiscard = false;
            var secondApproval = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstApproval = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            const string definition = """
                {"steps":[
                  {"tool":"application.get_version","input":{}},
                  {"tool":"runtime.get_status","input":{"id":"local.inference"}}
                ]}
                """;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((dialog, _) =>
            {
                if (dialog is ObservationWorkflowWindow workflowWindow) { window = workflowWindow; return; }
                if (dialog.Title is "Unsaved observation workflow")
                {
                    var discardButtons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => discardButtons[allowDiscard ? 0 : 1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                    return;
                }
                if (dialog.Title is not ("Review exact workflow package" or "Save disabled exact workflow" or "Approve exact observation step")) { return; }
                reviews.Add(dialog.Title);
                var text = dialog.GetLogicalDescendants().OfType<TextBox>().Single();
                text.IsReadOnly.Should().BeTrue();
                var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();
                if (dialog.Title is not "Approve exact observation step") { text.Text.Should().Contain(definition); }
                if (dialog.Title is "Approve exact observation step")
                {
                    if (text.Text!.Contains("Step 1:", StringComparison.Ordinal))
                    {
                        boundary.VersionReads.Should().Be(0);
                        if (operation is "token-first" or "owner-shutdown") { firstApproval.SetResult(); return; }
                        if (operation is "cancel-first" or "close-first" or "escape-first")
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                if (operation is "cancel-first") { buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                                if (operation is "close-first") { dialog.Close(); }
                                if (operation is "escape-first")
                                {
                                    dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                                }
                            });
                            return;
                        }
                    }
                    if (text.Text!.Contains("Step 2:", StringComparison.Ordinal))
                    {
                        boundary.VersionReads.Should().Be(1);
                        if (operation is "cancel" or "complete") { secondApproval.SetResult(); return; }
                    }
                    if (operation is "source" && text.Text!.Contains("Step 1:", StringComparison.Ordinal))
                    {
                        var path = Path.Combine(store.Destination(window!.Document()), "instructions.md");
                        File.AppendAllText(path, " ");
                    }
                    if (operation is "deny")
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                        return;
                    }
                }
                Avalonia.Threading.Dispatcher.UIThread.Post(() => buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            });
            async Task Command(string command)
            {
                native.Main.CommandText = "owned skill workflow " + command;
                await native.Main.RunTypedCommand.ExecuteAsync();
            }
            await Command("create inspection 1.0.0");
            window.Should().NotBeNull();
            await Command("name: Inspect host readiness");
            await Command("description: Read the version and recorded local runtime");
            await Command("definition: " + definition);
            await Command("validate");
            if (operation is "dirty-close")
            {
                (await controller.PrepareClose()).Should().BeFalse();
                window!.IsVisible.Should().BeTrue();
                window.Definition.Text.Should().Be(definition);
                (await store.List(native.Token)).Should().BeEmpty();
                allowDiscard = true;
                (await controller.PrepareClose()).Should().BeTrue();
                window.IsVisible.Should().BeFalse();
                boundary.VersionReads.Should().Be(0);
                return;
            }
            await Command("save");
            reviews.Should().Equal("Review exact workflow package", "Save disabled exact workflow");
            boundary.VersionReads.Should().Be(0);
            var saved = await store.Read("inspection", "1.0.0", native.Token);
            saved!.Document.IsAvailableForInvocation.Should().BeFalse();
            saved.Document.Guidance.Should().Be(definition);
            await Command("list");
            window!.Saved.SelectedIndex = 0;
            await Command("reopen inspection 1.0.0");
            window.Definition.Text.Should().Be(definition);
            await Command("targets");
            window.Targets.SelectedIndex = 0;
            await Command("request:  unchanged original request  ");
            await Command("review inspection 1.0.0");
            window.ReviewText.Text.Should().Contain(definition).And.Contain(" unchanged original request  ").And.Contain("15 seconds");
            boundary.VersionReads.Should().Be(0);
            await Command("enable");
            boundary.VersionReads.Should().Be(0);
            if (operation is "request") { window.Request.Text = "changed request"; }
            window.Request.Focus().Should().BeTrue();
            var invoke = Command("invoke");
            await invoke;
            if (operation is "token-first" or "owner-shutdown")
            {
                await Task.WhenAny(firstApproval.Task, controller.ActiveOperation);
                firstApproval.Task.IsCompleted.Should().BeTrue("the operation must reach its first independent approval");
                controller.ActiveOperation.IsCompleted.Should().BeFalse();
                if (operation is "token-first") { await Command("cancel"); }
                else { await controller.CloseAndWait(); }
            }
            if (operation is "cancel" or "complete")
            {
                await Task.WhenAny(secondApproval.Task, controller.ActiveOperation);
                secondApproval.Task.IsCompleted.Should().BeTrue("the operation must reach its second explicit approval: " + window.Status.Text);
                controller.ActiveOperation.IsCompleted.Should().BeFalse();
                await Command(operation is "cancel" ? "cancel" : "approve-step");
            }
            await controller.ActiveOperation;
            if (operation is "complete")
            {
                boundary.VersionReads.Should().Be(1);
                window.Status.Text.Should().Contain("Succeeded");
                var versionReply = new CapabilityReply(CapabilityOutcome.Succeeded, "observed",
                    Version: new("1.0.0-native-fixture", "Not observed by the current version provider."));
                var runtimeReply = new CapabilityReply(CapabilityOutcome.Succeeded, "recorded-observation",
                    Runtime: new(ReadOnlyCapabilityCatalog.LocalRuntimeId, RuntimeLocality.Local,
                        CapabilityAvailability.NotObserved, null, "No completed observation is recorded.", null, false));
                window.Result.Text.Should().Be("Source: Kora host observations. No model inference, probes or external actions.\n"
                    + "Step 1 - application.get_version (schema 1)\n" + Encoding.UTF8.GetString(CapabilityReplyWire.Serialize(versionReply)) + "\n"
                    + "Step 2 - runtime.get_status (schema 1)\n" + Encoding.UTF8.GetString(CapabilityReplyWire.Serialize(runtimeReply)) + "\n");
                window.FocusManager!.GetFocusedElement().Should().BeSameAs(window.Request);
                await Command("status");
                window.Status.Text.Should().Contain("Succeeded").And.Contain("joined");
            }
            else
            {
                boundary.VersionReads.Should().Be(operation is "cancel" ? 1 : 0);
                window.Result.Text.Should().BeEmpty();
                window.Status.Text.Should().NotContain("Succeeded");
            }
            f.Count("conversation_messages").Should().Be(0);
            f.Count("host_questions").Should().Be(0);
            f.Count("scoped_grants").Should().Be(0);
            dependencies.Observations.Should().BeEmpty();
            if (operation is not "owner-shutdown") { await Command("close"); }
            window.IsVisible.Should().BeFalse();
        });
    }

    private sealed class ObservationBoundary : ICapabilityHostAccess, IApplicationInfo
    {
        internal int VersionReads { get; private set; }
        public bool IsCurrentHost => true;
        public string Version { get { VersionReads++; return "1.0.0-native-fixture"; } }
    }

    private sealed class ObservationUsePolicy : IArtifactUsePolicy
    {
        public event EventHandler? Changed { add { } remove { } }
        public long Revision => 0;
        public bool IsDisabled(string identity) => false;
        public bool IsRevisionAllowed(string identity, string digest) => true;
    }
}
