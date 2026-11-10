using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Skills;
using Kora.Core.Dependencies;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

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
    public async Task HostedSharedSkillPreservesConsentAndExactAnswerWithoutRevivingHiddenReview(string action)
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
            var reader = new InvocationReader();
            var discovery = new SharedSkillDiscoveryService(new LocalSharedSkillPreferences(native), reader, native.SharedAdmission,
                composed.Audit, NullLogger<SharedSkillDiscoveryService>.Instance);
            await discovery.LoadSourcesAsync(static () => true, native.Token);
            await discovery.RegisterAsync("native source selection", static () => true, native.Token);
            await using var admission = new SharedSkillUseAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            var workflow = new SharedSkillInvocationWorkflow(discovery, admission, composed.Access, f.Store, composed.Workspace,
                composed.Conversation, composed.Audit, f.Time);
            using var responses = new ResponseWindowController(native.Main, NullLogger<ResponseWindowController>.Instance);
            using var packages = new SkillPackagesWindowController(native.Main, static () => true,
                NullLogger<SkillPackagesWindowController>.Instance, new(NullLogger<NativeDetailRenderer>.Instance),
                discovery, NullLogger<SharedSkillSourcesWindowController>.Instance);
            packages.BindWorkbench(responses.PrepareWorkbenchOwner,
                view => responses.ShowHostedView(WorkbenchActivity.SkillPackages, view),
                view => responses.ShowHostedView(WorkbenchActivity.SharedSkills, view));
            using var controller = new SharedSkillInvocationController(native.Main, workflow, packages,
                static () => true, static () => 1, NullLogger<SharedSkillInvocationController>.Instance);
            var shown = new TaskCompletionSource<SharedSkillInvocationView>(TaskCreationOptions.RunContinuationsAsynchronously);
            controller.BindWorkbench(responses.PrepareWorkbenchOwner, view =>
            {
                var owner = responses.PrepareWorkbenchOwner();
                owner.ShowSessions(expanded: true);
                owner.ShowNativeReview(view, "Reviewed shared skill invocation", view,
                    () => native.Main.CanRevealPrivatePresentation, _ => { });
                shown.TrySetResult(view);
            });
            var windows = new List<Window>();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => windows.Add(window));
            controller.Bind();
            try
            {
                packages.TryOpen(SkillReviewSurface.SharedSources, static () => true).Should().BeTrue();
                var inspector = packages.SharedView!;
                var refresh = inspector.FindControl<Button>("SearchSharedPackages")!;
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Ready(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
                { if (refresh.IsEnabled) { ready.TrySetResult(); } }
                refresh.PropertyChanged += Ready;
                try
                {
                    if (refresh.IsEnabled) { ready.TrySetResult(); }
                    await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), native.Token);
                }
                finally { refresh.PropertyChanged -= Ready; }
                inspector.FindControl<ComboBox>("SourceSelector")!.SelectedIndex = 0;
                inspector.SetCatalogue(await discovery.DiscoverAsync(reader.Source, static () => true, native.Token));
                inspector.FindControl<Button>("ReviewInstructionUse")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var view = await shown.Task.WaitAsync(TimeSpan.FromSeconds(10), native.Token);
                var shell = responses.PrepareWorkbenchOwner();
                async Task Press(string text)
                {
                    view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, text))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await controller.ActiveOperation.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
                }
                view.Instructions.Text.Should().Contain(reader.Text);
                await Press("Enable exact reviewed revision");
                await Press("Refresh existing Active conversations");
                view.Targets.SelectedIndex = 0;
                view.Request.Text = "Original hosted shared task.";
                await Press("Review exact invocation");
                view.Confirm.IsEnabled.Should().BeTrue();
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
                        new("Exact retained shared answer.", null)));
                }
                await controller.ActiveOperation.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
                Dispatcher.UIThread.RunJobs();
                var messages = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages;
                if (held)
                {
                    messages.Should().ContainSingle().Which.AssistantResponse!.Body.Should().Be("Exact retained shared answer.");
                    view.Status.Text.Should().NotContain("Exact retained shared answer.");
                }
                else if (action is "invoke") { messages.Should().ContainSingle().Which.Body.Should().Be("Original hosted shared task."); }
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
                reply.TrySetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("Released shared answer.", null)));
                await native.Main.ExitAsync();
            }
        });
    }
}
