using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Core.Artifacts;
using Kora.Core.Commands;
using Kora.NativeUxFixture;
using Kora.Definitions.Artifacts;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed partial class OwnedDefinitionEditorWindowTests
{
    [Fact]
    public async Task ActualPromptCommandsReuseOneControllerAndDirtyKindSwitchShutdownAndPrivacyRetireDrafts()
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
            using var controller = new OwnedInstructionEditorController(f.Main, instructionActions, new(instructionActions), reload, f,
                () => true, () => 1, NullLogger<OwnedInstructionEditorController>.Instance, promptActions, new(promptActions));
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
            await Run("prompts create example");
            editor.Should().NotBeNull();
            editor!.KindInput.SelectedItem.Should().Be(ArtifactKind.Prompt);
            editor.Source.Text.Should().Contain("literal saved user request");
            editor.SourceSizeStatus.Text.Should().Contain($"{ArtifactFileDocument.MeasureSourceBuffer(editor.Source.Text!).Utf8Bytes} UTF-8 bytes");
            reviewOpened = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var save = Run("prompts save");
            (await Task.WhenAny(save, reviewOpened.Task)).Should().BeSameAs(reviewOpened.Task,
                "the admitted save must reach exact review; surface status: {0}",
                string.Join("\n", editor.GetVisualDescendants().OfType<TextBlock>().Select(item => item.Text)));
            var saveReview = await reviewOpened.Task;
            editor.Source.Text += "Changed while exact review was open.";
            saveReview.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Confirm save"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await save;
            editor.Source.Text.Should().Contain("Changed while exact review was open.");
            File.Exists(Path.Combine(f.RoamingRoot, "Prompts", "example.prompt.md")).Should().BeFalse();
            var draft = editor.Source.Text;
            var switchKind = Run("instructions create other");
            dialog!.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Cancel"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await switchKind;
            editor.KindInput.SelectedItem.Should().Be(ArtifactKind.Prompt);
            editor.Source.Text.Should().Be(draft);
            editor.SourceSizeStatus.Text.Should().Contain($"{ArtifactFileDocument.MeasureSourceBuffer(draft!).Utf8Bytes} UTF-8 bytes");
            var closing = f.Main.TryPrepareHandoffAsync();
            dialog!.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Cancel"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            (await closing).Should().BeFalse();
            editor.IsVisible.Should().BeTrue();
            var discard = f.Main.TryPrepareHandoffAsync();
            dialog!.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Discard"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            (await discard).Should().BeTrue();
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            editor.Closed += (_, _) => closed.TrySetResult();
            f.Main.CloseForObservedPrivacyEvent("Synthetic privacy closure", hidePresentation: true);
            await closed.Task.WaitAsync(TestContext.Current.CancellationToken);
            await f.Main.PrivacyClosureTask;
            editor.Source.Text.Should().BeEmpty();
            editor.SourceSizeStatus.Text.Should().BeEmpty();
            editor.IsVisible.Should().BeFalse();
            File.Exists(Path.Combine(f.RoamingRoot, "Prompts", "example.prompt.md")).Should().BeFalse();
        });
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction, InstructionConflictChoice.Reload)]
    [InlineData(ArtifactKind.Instruction, InstructionConflictChoice.Overwrite)]
    [InlineData(ArtifactKind.Instruction, InstructionConflictChoice.Cancel)]
    [InlineData(ArtifactKind.Prompt, InstructionConflictChoice.Reload)]
    [InlineData(ArtifactKind.Prompt, InstructionConflictChoice.Overwrite)]
    [InlineData(ArtifactKind.Prompt, InstructionConflictChoice.Cancel)]
    public async Task BothKindsUseTheSameNativeExactSourceAndAllThreeConflictChoices(ArtifactKind kind, InstructionConflictChoice choice)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var actions = new List<string>();
            var window = new OwnedInstructionEditorWindow(actions.Add);
            window.Show();
            try
            {
                window.SelectKind(kind);
                actions.Should().BeEmpty();
                var proposed = Document(kind, "Kora draft");
                var original = new OwnedInstructionSnapshot("example", "a", "b", Document(kind, "External"));
                Window? dialog = null;
                using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((candidate, _) =>
                {
                    if (!ReferenceEquals(candidate, window)) { dialog = candidate; }
                });
                var review = new NativeInstructionMutationReview(window,
                    @"Q:\fixture\" + OwnedDefinitionKind.DirectoryName(kind), () => true, kind);
                var conflict = review.Conflict(original, proposed, TestContext.Current.CancellationToken);
                dialog.Should().NotBeNull();
                var texts = dialog!.GetVisualDescendants().OfType<TextBox>().Select(item => item.Text).ToArray();
                texts.Should().Equal(original.Document.Text, proposed.Text);
                var buttons = dialog.GetVisualDescendants().OfType<Button>().ToArray();
                buttons.Select(button => button.Content as string).Should().Equal(
                    "Reload from disk (abandon changes)", "Overwrite with Kora changes", "Cancel (keep draft)");
                buttons[(int)choice].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                (await conflict).Should().Be(choice);
                texts.Should().NotContain(string.Empty);
                window.Source.Text = "Private draft";
                window.ClearPrivateText();
                window.Source.Text.Should().BeEmpty();
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task ExactNativeSaveReviewShowsKindSourceDigestsAndCancellationKeepsDraft(ArtifactKind kind)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var window = new OwnedInstructionEditorWindow(_ => { });
            window.Show();
            try
            {
                Window? dialog = null;
                using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((candidate, _) =>
                {
                    if (!ReferenceEquals(candidate, window)) { dialog = candidate; }
                });
                var original = new OwnedInstructionSnapshot("example", "a", "b", Document(kind, "Original"));
                var proposed = Document(kind, "Draft");
                var review = new NativeInstructionMutationReview(window, @"Q:\fixture\" + OwnedDefinitionKind.DirectoryName(kind), () => true, kind);
                var confirmation = review.Confirm("example", original, proposed, TestContext.Current.CancellationToken);
                var explanation = dialog!.GetVisualDescendants().OfType<TextBlock>().First(item => item.Text?.StartsWith("Owned kind:", StringComparison.Ordinal) == true).Text;
                explanation.Should().Contain(kind.ToString()).And.Contain(original.Document.Definition.Digest)
                    .And.Contain(proposed.Definition.Digest).And.Contain(OwnedDefinitionKind.Extension(kind));
                window.Source.Text = proposed.Text;
                window.CancelReview();
                (await confirmation).Should().BeFalse();
                window.Source.Text.Should().Be(proposed.Text);
            }
            finally { window.ClearPrivateText(); window.Close(); }
        });
    }

    private static ArtifactFileDocument Document(ArtifactKind kind, string body) =>
        ArtifactFileDocument.Parse(ArtifactFileDocument.Encode($"---\nkora-format: 1\nname: example\ndescription: Test\n---\n{body}", false),
            kind, "kora-profile", "example");
}
