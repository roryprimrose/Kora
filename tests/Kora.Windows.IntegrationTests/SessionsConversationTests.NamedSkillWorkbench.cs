using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Skills;
using Kora.Core.Dependencies;
using Kora.Core.Skills;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("invoke")]
    [InlineData("hide")]
    [InlineData("review-tab")]
    [InlineData("hide-running")]
    [InlineData("minimize-running")]
    public async Task HostedNamedSkillKeepsSourceChoiceSeparateAndHidingDoesNotStopConfirmedWork(string action)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var held = action.EndsWith("-running", StringComparison.Ordinal);
            var reply = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var adapter = new HistoryAdapter { Pending = held ? reply.Task : null, Entered = entered };
            await using var composed = new Composition(f, adapter: adapter);
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            var store = new WindowsOwnedDeclarativeSkillStore(native);
            var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "concise-review", "Description", "Literal inert guidance");
            await store.Publish(document, static () => true, commit => Task.FromResult(commit()), native.Token);
            await using var admission = new OwnedSkillControlAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            var owned = new OwnedSkillInvocationWorkflow(store, admission, composed.Access, f.Store, composed.Workspace,
                composed.Conversation, native.Audit, f.Time, new NamedNativeUsePolicy());
            var discovery = new SharedSkillDiscoveryService(new LocalSharedSkillPreferences(native), new InvocationReader(), native.SharedAdmission,
                native.Audit, NullLogger<SharedSkillDiscoveryService>.Instance);
            await discovery.LoadSourcesAsync(static () => true, native.Token);
            await discovery.RegisterAsync("explicit native root selection", static () => true, native.Token);
            await using var sharedAdmission = new SharedSkillUseAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            var shared = new SharedSkillInvocationWorkflow(discovery, sharedAdmission, composed.Access, f.Store, composed.Workspace,
                composed.Conversation, native.Audit, f.Time);
            var catalogue = new NamedSkillCatalogue(store, new(store, admission, native.Audit), discovery,
                composed.Access, new NamedNativeUsePolicy(), f.Time);
            await using var controller = new NamedSkillInvocationController(native.Main, catalogue, owned, shared,
                static () => true, static () => 1, NullLogger<NamedSkillInvocationController>.Instance);
            using var responses = new ResponseWindowController(native.Main, NullLogger<ResponseWindowController>.Instance);
            controller.BindWorkbench(responses.PrepareWorkbenchOwner, view =>
            {
                var owner = responses.PrepareWorkbenchOwner();
                owner.ShowSessions(expanded: true);
                owner.ShowNativeReview(view, "Review instruction-only skill by readable name", view,
                    () => native.Main.CanRevealPrivatePresentation, _ => { });
            });
            var windows = new List<Window>();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => windows.Add(window));
            controller.Bind();
            try
            {
                native.Main.CommandText = "use skill concise-review";
                await native.Main.RunTypedCommand.ExecuteAsync();
                var view = controller.View!;
                var shell = responses.PrepareWorkbenchOwner();
                async Task Press(string text)
                {
                    view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, text))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await controller.ActiveOperation.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
                }
                await Press("Inspect saved and registered skill files locally (read consent)");
                view.Choices.Items.Should().HaveCount(2);
                view.Choices.SelectedIndex.Should().Be(-1);
                native.Main.CommandText = "skill choice 1";
                await native.Main.RunTypedCommand.ExecuteAsync();
                view.Instructions.Text.Should().Contain(document.Guidance);
                await Press("Enable exact reviewed revision once");
                await Press("Refresh existing Active conversations");
                view.Targets.SelectedIndex = 0;
                view.Request.Text = "Original exact hosted named task.";
                await Press("Review exact invocation");
                view.Confirmation.Text.Should().Contain("Original exact hosted named task.");
                adapter.Calls.Should().Be(0);
                if (action is "invoke" || held)
                {
                    view.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (held) { await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token); }
                }
                if (action.StartsWith("hide", StringComparison.Ordinal))
                { shell.Hide(); shell.Show(); shell.Activate(); }
                else if (action is "minimize-running")
                { shell.WindowState = WindowState.Minimized; shell.WindowState = WindowState.Normal; }
                else if (action is "review-tab")
                {
                    var other = new TextBlock { Text = "Another exact review" };
                    shell.ShowNativeReview(other, "Another review", other, static () => true, _ => { });
                }
                if (held)
                {
                    controller.ActiveOperation.IsCompleted.Should().BeFalse();
                    reply.TrySetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                        new("Exact retained named answer.", null)));
                }
                await controller.ActiveOperation.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
                Dispatcher.UIThread.RunJobs();
                var messages = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages;
                if (held)
                {
                    messages.Should().ContainSingle().Which.AssistantResponse!.Body.Should().Be("Exact retained named answer.");
                    view.Status.Text.Should().NotContain("Exact retained named answer.");
                }
                else if (action is "invoke")
                { messages.Should().ContainSingle().Which.Body.Should().Be("Original exact hosted named task."); }
                else
                {
                    view.Instructions.Text.Should().BeEmpty();
                    view.Confirmation.Text.Should().BeEmpty();
                    view.Confirm.IsEnabled.Should().BeFalse();
                    messages.Should().BeEmpty();
                    adapter.Calls.Should().Be(0);
                }
                windows.Distinct().Should().Equal(shell);
            }
            finally
            {
                reply.TrySetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("Released synthetic named answer.", null)));
                await controller.CloseAndWait();
            }
        });
    }
}
