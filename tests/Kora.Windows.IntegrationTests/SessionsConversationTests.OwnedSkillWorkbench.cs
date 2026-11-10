using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

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
    public async Task HostedOwnedSkillSeparatesEditorPresentationFromExactConfirmedInvocation(string action)
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
            var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "Literal inert guidance");
            await store.Publish(document, static () => true, commit => Task.FromResult(commit()), native.Token);
            await using var admission = new OwnedSkillControlAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            var useWorkflow = new OwnedSkillInvocationWorkflow(store, admission, composed.Access, f.Store, composed.Workspace,
                composed.Conversation, native.Audit, f.Time, new ArtifactUsePreferences(f.Paths));
            var use = new OwnedSkillInvocationController(native.Main, useWorkflow, static () => true, static () => 1,
                NullLogger<OwnedSkillInvocationController>.Instance);
            use.Bind();
            await using var editor = new OwnedSkillEditorController(native.Main, new(store, admission, native.Audit), admission,
                static () => true, static () => 1, NullLogger<OwnedSkillEditorController>.Instance, use);
            using var responses = new ResponseWindowController(native.Main, NullLogger<ResponseWindowController>.Instance);
            editor.BindWorkbench(responses.PrepareWorkbenchOwner,
                view => responses.ShowHostedView(WorkbenchActivity.SkillEditor, view));
            use.BindWorkbench(responses.PrepareWorkbenchOwner,
                view =>
                {
                    var owner = responses.PrepareWorkbenchOwner();
                    owner.ShowSessions(expanded: true, preservePresentation: true);
                    owner.ShowNativeReview(view, "Review one use of a saved owned skill", view,
                        () => native.Main.CanRevealPrivatePresentation, _ => { });
                },
                view =>
                {
                    var owner = responses.PrepareWorkbenchOwner();
                    owner.ShowSessions(expanded: true, preservePresentation: true);
                    owner.ShowNativeReview(view, "Reviewed owned MCP skill", view,
                        () => native.Main.CanRevealPrivatePresentation, _ => { });
                });
            var windows = new List<Window>();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => windows.Add(window));
            editor.Bind();
            async Task Command(string text)
            {
                native.Main.CommandText = text;
                await native.Main.RunTypedCommand.ExecuteAsync();
            }
            try
            {
                await Command("owned skill list");
                await Command("owned skill reopen example 1.0.0");
                await Command("owned skill use example 1.0.0");
                var view = use.View!;
                var shell = responses.PrepareWorkbenchOwner();
                view.Instructions.Text.Should().Contain(document.ManifestText).And.Contain(document.Guidance);
                await Command("owned skill enable");
                await Command("owned skill targets");
                view.Targets.SelectedIndex = 0;
                await Command("owned skill request: Original exact owned task.");
                await Command("owned skill review-use");
                view.Confirm.IsEnabled.Should().BeTrue(view.Status.Text);
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
                    use.ActiveOperation.IsCompleted.Should().BeFalse();
                    reply.TrySetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                        new("Exact retained owned answer.", null)));
                }
                await use.ActiveOperation.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
                Dispatcher.UIThread.RunJobs();
                var messages = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages;
                if (held)
                {
                    messages.Should().ContainSingle().Which.AssistantResponse!.Body.Should().Be("Exact retained owned answer.");
                    view.Status.Text.Should().NotContain("Exact retained owned answer.");
                }
                else if (action is "invoke") { messages.Should().ContainSingle().Which.Body.Should().Be("Original exact owned task."); }
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
                reply.TrySetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("Released owned answer.", null)));
                await use.CloseAndWait();
                await native.Main.ExitAsync();
            }
        });
    }
}
