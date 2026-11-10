using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Application.Voice;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class PromptParameterWindowTests
{
    [Theory]
    [InlineData("hide")]
    [InlineData("minimize")]
    [InlineData("review-tab")]
    [InlineData("submit")]
    public async Task HostedPromptPreparationRetainsOnlyExplicitSubmissionAndDiscardsValuesOnLeave(string action)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var store = new WindowsOwnedInstructionStore(f, ArtifactKind.Prompt);
            var revisions = new InstructionRevisionCatalogue(f, ArtifactKind.Prompt);
            var document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                "---\r\nkora-format: 2\r\nname: example\r\ndescription: Test\r\n---\r\nExact ${input:topic}\r\nAgain ${input:topic}\r\n", true),
                ArtifactKind.Prompt, "kora-profile", "example");
            var saved = (await store.Write("example", null, document, static () => true, f.Token)).Current!;
            f.ArtifactCatalogue.Publish(revisions.Reconcile([saved.Document.Definition with { OwnedInstructionRevision = saved.Revision }]));
            var actions = new OwnedInstructionEditorActions(store, admission, revisions, f.ArtifactCatalogue, f.Audit,
                new(new()), ArtifactKind.Prompt);
            (await actions.Activate(saved, _ => Task.FromResult(true), static () => true, f.Token)).Should().BeTrue();
            f.Main.BindOwnedInstructionFreshnessWithCancellation((candidate, token) =>
                actions.IsCurrent(candidate, f.Main.CaptureInstructionEditorEligibility(), token));
            using var bootstrapper = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(bootstrapper);
            var registry = new ReadOnlyCapabilityRegistry(f.Access, new(), new(), new(new Kora.Application.AssemblyApplicationInfo()),
                new(bootstrapper), new(runtime), new(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            await using var modes = new AudioControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var policy = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(f), modes, f.Audit);
            await using var models = new ModelTurnHost(f.Interactions, f.Access, f.Access, registry, f.Audit,
                NullLogger<ModelTurnHost>.Instance, TimeProvider.System,
                [new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.None, DateTimeOffset.MinValue, null)], policy);
            var conversation = new SessionConversationService(f.Interactions, f.Interactions, f.Sessions, f.Access, models,
                TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            var target = await f.Sessions.CreateAsync(new("Exact prompt target"), Kora.Core.Hosting.RequestOrigin.LocalUi, f.Token);
            await using var controller = new PromptParameterController(f.Main, f.ArtifactCatalogue,
                new(admission, actions, models, f.Audit, TimeProvider.System), new(f.Sessions, conversation),
                static () => true, static () => 1, NullLogger<PromptParameterController>.Instance);
            using var responses = new ResponseWindowController(f.Main, NullLogger<ResponseWindowController>.Instance);
            controller.BindWorkbench(responses.PrepareWorkbenchOwner, view =>
            {
                var owner = responses.PrepareWorkbenchOwner();
                owner.ShowSessions(expanded: true);
                owner.ShowNativeReview(view, "Required prompt inputs and exact request review", view,
                    () => f.Main.CanRevealPrivatePresentation, _ => { });
            });
            var windows = new List<Window>();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => windows.Add(window));
            controller.Bind();
            async Task Prepare()
            {
                f.Main.CommandText = "/prompt example";
                await f.Main.RunTypedCommand.ExecuteAsync();
            }
            await Prepare();
            var view = controller.View!;
            var shell = responses.PrepareWorkbenchOwner();
            view.Inputs["topic"].Text = "Private literal";
            async Task ClickHosted(Button button)
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.ActiveOperation.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
            }
            await ClickHosted(view.Preview);
            var expanded = view.Expanded.Text;
            expanded.Should().Be("Exact Private literal\r\nAgain Private literal\r\n");
            await ClickHosted(view.RefreshTargets);
            view.Targets.SelectedItem = view.Targets.ItemsSource!.Cast<PromptConversationTarget>()
                .Single(item => item.Entry.Authority.SessionId == target.Authority.SessionId);
            await ClickHosted(view.ReviewSubmit);
            view.ConfirmSubmit.IsEnabled.Should().BeTrue();
            (await f.Interactions.ReadConversationPageAsync(target.Authority.SessionId, null, 25, f.Token)).Messages.Should().BeEmpty();
            switch (action)
            {
                case "submit": await ClickHosted(view.ConfirmSubmit); break;
                case "hide": shell.Hide(); shell.Show(); shell.Activate(); break;
                case "minimize":
                    shell.WindowState = WindowState.Minimized;
                    shell.WindowState = WindowState.Normal;
                    break;
                case "review-tab":
                    var other = new TextBlock { Text = "Another exact review" };
                    shell.ShowNativeReview(other, "Another review", other, static () => true, _ => { });
                    break;
            }
            Dispatcher.UIThread.RunJobs();
            await controller.ActiveOperation;
            var messages = (await f.Interactions.ReadConversationPageAsync(target.Authority.SessionId, null, 25, f.Token)).Messages;
            if (action is "submit") { messages.Should().ContainSingle().Which.Body.Should().Be(expanded); }
            else
            {
                controller.View.Should().BeNull();
                view.Inputs["topic"].Text.Should().BeEmpty();
                view.Expanded.Text.Should().BeEmpty();
                view.RetentionReview.Text.Should().BeEmpty();
                view.ConfirmSubmit.IsEnabled.Should().BeFalse();
                messages.Should().BeEmpty();
                await Prepare();
                controller.View.Should().NotBeSameAs(view);
                controller.View!.Inputs["topic"].Text.Should().BeNullOrEmpty();
                controller.View.ConfirmSubmit.IsEnabled.Should().BeFalse();
            }
            (await store.Read("example", f.Token))!.Document.CopyBytes().Should().Equal(document.CopyBytes());
            windows.Distinct().Should().Equal(shell);
            await controller.CloseAndWait();
        });
    }
}
