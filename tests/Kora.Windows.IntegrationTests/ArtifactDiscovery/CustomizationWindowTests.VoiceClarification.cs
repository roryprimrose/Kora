using System.Text;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Core.Customizations;
using Kora.Definitions.Artifacts;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class CustomizationWindowTests
{
    [Theory]
    [InlineData("refine")]
    [InlineData("discuss")]
    [InlineData("cancel")]
    [InlineData("invalid-reply")]
    [InlineData("draft")]
    [InlineData("request")]
    [InlineData("component")]
    [InlineData("source-modified")]
    [InlineData("source-deleted")]
    [InlineData("generation")]
    [InlineData("withdrawal")]
    [InlineData("close")]
    [InlineData("late-model")]
    [InlineData("source-after-disclosure")]
    [InlineData("source-before-adoption")]
    public Task Spoken_editor_clarification_completes_only_original_reviewed_operation(string state) =>
        HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent, syntheticVoice: true);
            await f.InitializeAsync();
            f.Main.BindVoiceOwnershipGate(static () => true);
            await f.Main.SetVoiceConsentAsync(true);
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
            var profile = Path.Combine(f.LocalRoot, "clarification-profile");
            var store = new WindowsCustomizationStore(f, profile, Path.Combine(f.LocalRoot, "clarification-user"));
            using var catalogue = EmbeddedArtifactCatalogue.Load();
            var management = new CustomizationManagementService(store, admission, new ArtifactUsePreferences(f), catalogue, f.Audit);
            var directories = new LocalArtifactDirectoryPreferences(f);
            using var reload = new ArtifactCatalogueReloadService(catalogue, f, directories,
                new ArtifactDirectoryConfigurationService(directories, f.Audit),
                new McpServerConfigurationService(f, directories, profile),
                NullLogger<ArtifactCatalogueReloadService>.Instance, profileRoamingRoot: profile);
            var authoring = new Authoring { Intent = CustomizationVoiceIntent.Clarify, PreserveFiles = state is "discuss" };
            using var controller = new CustomizationWindowController(f.Main, management, authoring, catalogue, reload,
                static () => true, static () => 1, NullLogger<CustomizationWindowController>.Instance);
            var seed = await store.Create(CustomizationKind.Instruction, CustomizationScope.Kora, "example", f.Token);
            var original = CustomizationLayout.Template(CustomizationKind.Instruction, "example");
            await store.Save(seed, null, [new(seed.PrimaryFile, Encoding.UTF8.GetBytes(original))],
                static () => true, operation => Task.FromResult(operation()), f.Token);
            CustomizationWindow? editor = null;
            var next = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((view, _) =>
            {
                if (view is CustomizationWindow selected) { editor = selected; }
                else if (view.Title?.StartsWith("Review", StringComparison.Ordinal) == true || view.Title is "Choose voice target")
                { next.TrySetResult(view); }
            });
            static void Click(Window view, string caption) => view.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<Window> Next()
            {
                var continued = await Task.WhenAny(next.Task, finished.Task).WaitAsync(TimeSpan.FromSeconds(20), f.Token);
                continued.Should().BeSameAs(next.Task, editor!.Status.Text);
                return await next.Task;
            }
            controller.Bind();
            f.Main.CommandText = "edit example instruction";
            await f.Main.RunTypedCommand.ExecuteAsync();
            await ActivateEditor(editor!);
            await f.Main.BeginPushToTalkAsync();
            await f.Main.EndPushToTalkAsync();
            f.Main.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(f.Main.IsBusy) && !f.Main.IsBusy) { finished.TrySetResult(); }
            };
            f.Voice.PublishTranscript("make this clearer");
            var routing = await Next();
            next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Click(routing, "Interpret editor intent");
            var question = await Next();
            question.Title.Should().Be("Choose voice target");
            var voiceButton = question.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Answer by voice"));
            voiceButton.IsEnabled.Should().BeTrue(f.Main.ListeningStatus);
            Click(question, "Answer by voice");
            await f.Voice.ReplyStarted.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
            f.Voice.ReplyCalls.Should().Be(1);
            authoring.Calls.Should().Be(0);
            next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            switch (state)
            {
                case "draft": editor!.Editor.Text = original + "\nIndependent draft."; break;
                case "request": editor!.Request.Text = "Independent request"; break;
                case "component": editor!.Files.SelectedItem = null; break;
                case "source-modified": File.AppendAllText(seed.Location, "\nExternal change."); break;
                case "source-deleted": File.Delete(seed.Location); break;
                case "generation": f.Voice.InvalidateCapture(); break;
                case "withdrawal": await f.Main.SetVoiceConsentAsync(false); break;
                case "close": controller.Dispose(); break;
            }
            f.Voice.PublishTranscript(state switch
            {
                "discuss" => "Discuss this artifact",
                "cancel" => "Cancel",
                "invalid-reply" => "save and enable everything",
                _ => "Refine this artifact",
            });
            if (state is "refine" or "discuss" or "late-model" or "source-after-disclosure" or "source-before-adoption")
            {
                var disclosure = await Next();
                disclosure.Title.Should().Be("Review model authoring disclosure");
                f.Voice.IsCaptureQuiescent.Should().BeTrue("no original authoring may run before reply capture joins");
                disclosure.GetVisualDescendants().OfType<TextBox>().Single().Text.Should().Contain("make this clearer");
                if (state is "source-after-disclosure") { File.AppendAllText(seed.Location, "\nExternal change."); }
                if (state is "late-model")
                {
                    authoring.Pause = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    authoring.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Click(disclosure, "Send for local authoring");
                if (state is "late-model")
                {
                    await authoring.Started!.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
                    editor!.Request.Text = "Newer request";
                    authoring.Pause!.SetResult();
                }
                else if (state is not "source-after-disclosure")
                {
                    var proposal = await Next();
                    if (state is "source-before-adoption") { File.AppendAllText(seed.Location, "\nExternal change."); }
                    Click(proposal, "Use proposed draft");
                }
            }
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
            await controller.WaitForOperation();
            if (state is "component") { editor!.Files.SelectedItem = seed.PrimaryFile; }
            f.Voice.PublishTranscript("Refine this artifact");
            f.Voice.IsCaptureQuiescent.Should().BeTrue();
            authoring.InterpretCalls.Should().Be(1);
            authoring.Calls.Should().Be(state is "refine" or "discuss" or "late-model" or "source-before-adoption" ? 1 : 0);
            if (state is "refine") { editor!.Editor.Text.Should().Contain("Model proposal."); }
            else if (state is not ("draft" or "close")) { editor!.Editor.Text.Should().Be(original); }
            if (state is not ("source-modified" or "source-deleted" or "source-after-disclosure" or "source-before-adoption"))
            { File.ReadAllText(seed.Location).Should().Be(original, "authoring never implicitly saves"); }
            if (state is "refine" or "discuss")
            {
                editor!.Request.Text.Should().Be("make this clearer");
                authoring.Context!.Mode.Should().Be(state is "discuss" ? CustomizationAuthoringMode.Discuss : CustomizationAuthoringMode.Edit);
            }
        });
}
