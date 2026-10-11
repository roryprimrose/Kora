using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Core.Artifacts;
using Kora.Core.Commands;
using Kora.Definitions.Artifacts;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    private static readonly string[] CopySelections = ["example", "other"];
    [Theory]
    [InlineData(ArtifactKind.Instruction, "confirmed")]
    [InlineData(ArtifactKind.Prompt, "confirmed")]
    [InlineData(ArtifactKind.Instruction, "cancel")]
    [InlineData(ArtifactKind.Prompt, "draft")]
    [InlineData(ArtifactKind.Instruction, "destination")]
    [InlineData(ArtifactKind.Prompt, "selection")]
    [InlineData(ArtifactKind.Instruction, "kind")]
    [InlineData(ArtifactKind.Prompt, "owner")]
    [InlineData(ArtifactKind.Instruction, "input")]
    public async Task NativeCopyReviewRetainsOriginalDraftAndRejectsChangedReviewInputs(ArtifactKind kind, string condition)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
            var instructionStore = new WindowsOwnedInstructionStore(f);
            var promptStore = new WindowsOwnedInstructionStore(f, ArtifactKind.Prompt);
            var instructions = new InstructionRevisionCatalogue(f);
            var prompts = new InstructionRevisionCatalogue(f, ArtifactKind.Prompt);
            var catalogue = EmbeddedArtifactCatalogue.Load();
            var controls = new BuiltInCommandRouter(new BuiltInCommandCatalog());
            var instructionActions = new OwnedInstructionEditorActions(instructionStore, admission, instructions, catalogue, f.Audit, controls);
            var promptActions = new OwnedInstructionEditorActions(promptStore, admission, prompts, catalogue, f.Audit, controls, ArtifactKind.Prompt);
            var preferences = new LocalArtifactDirectoryPreferences(f);
            using var reload = new ArtifactCatalogueReloadService(catalogue, f, preferences,
                new ArtifactDirectoryConfigurationService(preferences, f.Audit),
                new McpServerConfigurationService(f, preferences, Path.Combine(f.LocalRoot, "no-profile")),
                NullLogger<ArtifactCatalogueReloadService>.Instance, instructions, instructionStore,
                Path.Combine(f.LocalRoot, "no-profile"), prompts, promptStore);
            var ownership = 1L;
            using var controller = new OwnedInstructionEditorController(f.Main, instructionActions, new(instructionActions), reload, f,
                () => true, () => ownership, NullLogger<OwnedInstructionEditorController>.Instance, promptActions, new(promptActions));
            var store = kind == ArtifactKind.Instruction ? instructionStore : promptStore;
            var source = (await store.Write("example", null, Document(kind, "Original source"), () => true, f.Token)).Current!;
            reload.ReconcileSavedCopy(source).Should().BeTrue();
            OwnedInstructionEditorWindow? editor = null;
            Window? dialog = null;
            TaskCompletionSource<Window>? reviewOpened = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window is OwnedInstructionEditorWindow selected) { editor = selected; }
                else { dialog = window; reviewOpened?.TrySetResult(window); }
            });
            controller.Bind();
            async Task Run(string command)
            {
                f.Main.CommandText = command;
                await f.Main.RunTypedCommand.ExecuteAsync();
            }
            var prefix = kind == ArtifactKind.Instruction ? "instructions" : "prompts";
            await Run(prefix + " inspect example");
            editor.Should().NotBeNull();
            var draft = source.Document.Text + "\r\nUnsaved exact draft.";
            editor!.Source.Text = draft;
            reviewOpened = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var copy = Run("Kora, " + prefix + " copy separate");
            (await Task.WhenAny(copy, reviewOpened.Task)).Should().BeSameAs(reviewOpened.Task);
            dialog = await reviewOpened.Task;
            dialog.GetVisualDescendants().OfType<TextBox>().Select(text => text.Text).Should()
                .Contain(draft.Replace("name: example", "name: separate", StringComparison.Ordinal));
            switch (condition)
            {
                case "draft": editor.Source.Text += "Later draft change."; draft = editor.Source.Text; break;
                case "destination": editor.CopyNameInput.Text = "other"; break;
                case "selection": editor.Files.ItemsSource = CopySelections; editor.Files.SelectedItem = "other"; break;
                case "kind": editor.KindInput.SelectedItem = kind == ArtifactKind.Instruction ? ArtifactKind.Prompt : ArtifactKind.Instruction; break;
                case "owner": ownership++; break;
                case "input": await Run(prefix + " list"); break;
            }
            dialog.GetVisualDescendants().OfType<Button>().Single(button =>
                Equals(button.Content, condition is "cancel" ? "Cancel" : "Confirm new copy"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await copy;
            editor.Source.Text.Should().Be(draft);
            var originalPath = Path.Combine(f.RoamingRoot, OwnedDefinitionKind.DirectoryName(kind), source.CanonicalFileName);
            File.ReadAllBytes(originalPath).Should().Equal(source.Document.CopyBytes());
            var destination = Path.Combine(f.RoamingRoot, OwnedDefinitionKind.DirectoryName(kind), "separate" + OwnedDefinitionKind.Extension(kind));
            File.Exists(destination).Should().Be(condition is "confirmed");
            if (condition is "confirmed")
            {
                var saved = await store.Read("separate", f.Token);
                saved!.Document.Text.Should().Be(draft.Replace("name: example", "name: separate", StringComparison.Ordinal));
                catalogue.Candidates.Single(item => item.Kind == kind && item.OwnedInstructionName is "separate").IsUseEnabled.Should().BeFalse();
                var closing = f.Main.TryPrepareHandoffAsync();
                dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Cancel"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                (await closing).Should().BeFalse();
                editor.Source.Text.Should().Be(draft);
            }
        });
    }
}
