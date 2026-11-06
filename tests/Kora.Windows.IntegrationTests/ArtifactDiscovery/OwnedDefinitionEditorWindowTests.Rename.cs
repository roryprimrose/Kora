using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Core.Artifacts;
using Kora.Definitions.Artifacts;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    [Theory]
    [InlineData("typed")]
    [InlineData("native")]
    [InlineData("inventory")]
    [InlineData("inventory-recovery")]
    [InlineData("inventory-draft")]
    [InlineData("inventory-destination")]
    [InlineData("inventory-name")]
    [InlineData("inventory-selection")]
    [InlineData("inventory-filter")]
    [InlineData("inventory-input")]
    [InlineData("inventory-owner")]
    [InlineData("inventory-privacy")]
    [InlineData("inventory-late-dependent")]
    [InlineData("inventory-prepared-reload")]
    [InlineData("inventory-saved-restore")]
    [InlineData("inventory-saved-restore-recovery")]
    [InlineData("voice")]
    [InlineData("cancel-review")]
    [InlineData("cancel-confirm")]
    [InlineData("draft")]
    [InlineData("destination")]
    [InlineData("input")]
    [InlineData("owner")]
    [InlineData("source")]
    [InlineData("occupied")]
    [InlineData("privacy")]
    [InlineData("recovery")]
    [InlineData("late-dependent")]
    [InlineData("late-reload")]
    [InlineData("prepared-dependent")]
    [InlineData("prepared-reload")]
    [InlineData("saved-restore")]
    [InlineData("saved-restore-recovery")]
    public async Task CompleteOwnedRenameReviewsConfirmsReconcilesAndReopensExactDisabledSource(string scenario)
    {
        var inventorySelection = scenario is "inventory" || scenario.StartsWith("inventory-", StringComparison.Ordinal);
        if (scenario.StartsWith("inventory-", StringComparison.Ordinal)) { scenario = scenario["inventory-".Length..]; }
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent, syntheticVoice: scenario is "voice");
            await f.InitializeAsync();
            if (scenario is "voice")
            {
                f.Main.BindVoiceOwnershipGate(() => true);
                await f.Main.SetVoiceConsentAsync(true);
            }
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
            var store = new WindowsOwnedInstructionStore(f);
            var revisions = new InstructionRevisionCatalogue(f);
            using var catalogue = EmbeddedArtifactCatalogue.Load();
            var actions = new OwnedInstructionEditorActions(store, admission, revisions, catalogue, f.Audit, new(new()));
            var preferences = new LocalArtifactDirectoryPreferences(f);
            using var reload = new ArtifactCatalogueReloadService(catalogue, f, preferences,
                new ArtifactDirectoryConfigurationService(preferences, f.Audit),
                new McpServerConfigurationService(f, preferences, Path.Combine(f.LocalRoot, "no-profile")),
                NullLogger<ArtifactCatalogueReloadService>.Instance, revisions, store, Path.Combine(f.LocalRoot, "no-profile"));
            var ownership = 1L;
            using var controller = new OwnedInstructionEditorController(f.Main, actions, new(actions), reload, f,
                () => true, () => ownership, NullLogger<OwnedInstructionEditorController>.Instance);
            var source = (await store.Write("example", null, Document(ArtifactKind.Instruction, "Exact original body"), () => true,
                operation => Task.FromResult(operation()), f.Token)).Current!;
            var unrelatedPath = Path.Combine(f.RoamingRoot, "Instructions", "unrelated.txt");
            File.WriteAllBytes(unrelatedPath, [1, 2, 0, 3]);
            reload.ReconcileSavedCopy(source).Should().BeTrue();
            var consequencesChanged = false;
            void ChangeConsequences()
            {
                if (consequencesChanged) { return; }
                consequencesChanged = true;
                if (scenario.EndsWith("dependent", StringComparison.Ordinal))
                {
                    var dependent = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                        $"---\nname: later\ndescription: Later dependent\nkora-agent-format: 2\nreferences: {source.Document.Definition.Id}\n---\nLater guidance",
                        false), ArtifactKind.Agent, "user-profile", "later").Definition;
                    catalogue.Update(items => [.. items, dependent]);
                }
                else { catalogue.ReportReloadFailure(new IOException("Synthetic post-confirmation reload failure")); }
            }
            OwnedInstructionEditorWindow? editor = null;
            var reviewOpened = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var observer = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window is OwnedInstructionEditorWindow selected) { editor = selected; }
                else { reviewOpened.TrySetResult(window); }
            });
            controller.Bind();
            async Task Run(string text)
            {
                f.Main.CommandText = text;
                await f.Main.RunTypedCommand.ExecuteAsync();
            }
            static void Click(Window window, string label) => window.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Run("instructions inspect example");
            editor.Should().NotBeNull();
            var savedRestore = scenario.StartsWith("saved-restore", StringComparison.Ordinal);
            if (savedRestore)
            {
                editor!.Source.Text += "\r\nConfirmed changed body before rename";
                var saving = Run("instructions save");
                Click(await reviewOpened.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token), "Confirm save");
                await saving;
                source = (await store.Read("example", f.Token))!;
                editor.RestorePrevious.IsEnabled.Should().BeTrue();
                editor.Source.Text.Should().Be(source.Document.Text);
                reviewOpened = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            if (inventorySelection)
            {
                Click(editor!, "Refresh");
                await controller.WaitForOperation();
                editor!.Files.SelectedItem = editor.Files.Items.OfType<OwnedDefinitionInventoryRow>()
                    .Single(item => string.Equals(item.Name, "example", StringComparison.Ordinal));
                Click(editor, "Inspect");
                await controller.WaitForOperation();
                editor.Files.SelectedItem.Should().NotBeNull();
                if (savedRestore) { editor.RestorePrevious.IsEnabled.Should().BeFalse("explicit inventory reselection retires the previous Save identity"); }
            }
            Task rename;
            if (scenario is "native" || inventorySelection)
            {
                editor!.RenameNameInput.Text = "renamed";
                Click(editor, "Rename");
                rename = controller.WaitForOperation();
            }
            else if (scenario is "voice")
            {
                await f.Main.BeginPushToTalkCommand.ExecuteAsync();
                await f.Main.EndPushToTalkCommand.ExecuteAsync();
                f.Voice.PublishTranscript("Kora, instructions rename renamed");
                await reviewOpened.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
                rename = controller.WaitForOperation();
            }
            else { rename = Run("instructions rename renamed"); }
            (await Task.WhenAny(rename, reviewOpened.Task)).Should().BeSameAs(reviewOpened.Task,
                "rename must reach the complete source review; editor: {0}",
                string.Join("\n", editor!.GetVisualDescendants().OfType<TextBlock>().Select(item => item.Text)));
            var review = await reviewOpened.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
            review.GetVisualDescendants().OfType<TextBox>().Select(box => box.Text).Should().Contain(source.Document.Text)
                .And.Contain(new OwnedInstructionRename(source, "renamed").Proposed.Text);
            var details = string.Join("\n", review.GetVisualDescendants().OfType<TextBlock>().Select(item => item.Text));
            details.Should().Contain("NOT an atomic").And.Contain("References stay bound").And.Contain("disabled");
            if (scenario is "cancel-review") { Click(review, "Cancel"); }
            else
            {
                reviewOpened = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Click(review, "Reviewed exact rename");
                (await Task.WhenAny(rename, reviewOpened.Task)).Should().BeSameAs(reviewOpened.Task,
                    "separate confirmation must remain current: {0}",
                    string.Join("\n", editor.GetVisualDescendants().OfType<TextBlock>().Select(item => item.Text)));
                var confirmation = await reviewOpened.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
                switch (scenario)
                {
                    case "draft": editor.Source.Text += "New draft"; break;
                    case "destination": editor.RenameNameInput.Text = "different"; break;
                    case "name": editor.NameInput.Text = "different"; break;
                    case "selection": editor.Files.SelectedItem = null; break;
                    case "filter": editor.QueryInput.Text = "different"; break;
                    case "input": f.Main.CommandText = "Unrelated later input"; break;
                    case "owner": ownership++; break;
                    case "source":
                        File.WriteAllBytes(Path.Combine(f.RoamingRoot, "Instructions", source.CanonicalFileName),
                            Document(ArtifactKind.Instruction, "External edit").CopyBytes());
                        break;
                    case "occupied":
                        File.WriteAllBytes(Path.Combine(f.RoamingRoot, "Instructions", "renamed.instructions.md"),
                            Document(ArtifactKind.Instruction, "Occupied").CopyBytes());
                        break;
                    case "privacy":
                        f.Main.CloseForObservedPrivacyEvent("Synthetic rename revocation", hidePresentation: true);
                        await f.Main.PrivacyClosureTask;
                        break;
                    case "recovery":
                    case "saved-restore-recovery":
                        store.AfterRenamePhase = phase =>
                        {
                            if (phase == OwnedInstructionRenamePhase.OriginalHeld) { throw new IOException("Synthetic interruption"); }
                        };
                        break;
                    case "late-dependent":
                    case "late-reload":
                        store.RenameDirectoryFlags = _ => { ChangeConsequences(); return 0; };
                        break;
                    case "prepared-dependent":
                    case "prepared-reload":
                        store.AfterRenamePhase = phase =>
                        {
                            if (phase == OwnedInstructionRenamePhase.Prepared) { ChangeConsequences(); }
                        };
                        break;
                }
                if (scenario is not "privacy") { Click(confirmation, scenario is "cancel-confirm" ? "Cancel" : "Confirm rename"); }
            }
            await rename;
            if (scenario.StartsWith("late-", StringComparison.Ordinal) || scenario.StartsWith("prepared-", StringComparison.Ordinal))
            {
                consequencesChanged.Should().BeTrue();
                var recoveryState = await store.ReadRenameRecovery(f.Token);
                if (scenario.StartsWith("prepared-", StringComparison.Ordinal))
                {
                    recoveryState.Should().NotBeNull();
                    recoveryState!.Phase.Should().Be(OwnedInstructionRenamePhase.Prepared);
                }
                else { recoveryState.Should().BeNull(); }
                editor.Source.Text.Should().Be(source.Document.Text);
                editor.NameInput.Text.Should().Be(source.Name);
                catalogue.Artifacts.Should().NotContain(item => item.OwnedInstructionName == "renamed");
            }
            if (scenario is "recovery" or "saved-restore-recovery")
            {
                var unfinished = (await store.ReadRenameRecovery(f.Token))!;
                unfinished.Phase.Should().Be(OwnedInstructionRenamePhase.OriginalHeld);
                if (savedRestore)
                {
                    editor.RestorePrevious.IsEnabled.Should().BeFalse();
                    editor.Source.Text.Should().Be(source.Document.Text);
                    reviewOpened = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    await Run("instructions restore");
                    reviewOpened.Task.IsCompleted.Should().BeFalse("old saved content must not review or publish through rename recovery");
                    (await store.ReadRenameRecovery(f.Token))!.ObservationRevision.Should().Be(unfinished.ObservationRevision);
                    Directory.GetFiles(Path.Combine(f.RoamingRoot, "Instructions"), "*.instructions.md").Should().BeEmpty();
                    editor.Source.Text.Should().Be(source.Document.Text);
                }
                store.AfterRenamePhase = null;
                reviewOpened = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var recovering = Run("instructions recover-rename");
                var recoveryReview = await reviewOpened.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
                reviewOpened = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Click(recoveryReview, "Reviewed exact rename");
                Click(await reviewOpened.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token), "Confirm rename");
                await recovering;
            }
            var completed = scenario is "typed" or "native" or "inventory" or "voice" or "recovery" || savedRestore;
            var oldPath = Path.Combine(f.RoamingRoot, "Instructions", source.CanonicalFileName);
            File.Exists(oldPath).Should().Be(!completed);
            var newPath = Path.Combine(f.RoamingRoot, "Instructions", "renamed.instructions.md");
            File.Exists(newPath).Should().Be(completed || scenario is "occupied");
            if (completed)
            {
                var saved = await store.Read("renamed", f.Token);
                saved!.Document.CopyBytes().Should().Equal(new OwnedInstructionRename(source, "renamed").Proposed.CopyBytes());
                saved.DirectoryIdentity.Should().Be(source.DirectoryIdentity);
                saved.RootBindingIdentity.Should().Be(source.RootBindingIdentity);
                (await store.ReadRenameRecovery(f.Token)).Should().BeNull();
                Directory.GetFiles(Path.Combine(f.RoamingRoot, "Instructions"), ".instruction-rename-*").Should().BeEmpty();
                catalogue.Candidates.Single(item => item.OwnedInstructionName is "renamed").IsUseEnabled.Should().BeFalse();
                catalogue.Candidates.Should().NotContain(item => item.OwnedInstructionName == "example");
                if (savedRestore)
                {
                    editor.RestorePrevious.IsEnabled.Should().BeFalse();
                    var renamedBytes = File.ReadAllBytes(newPath);
                    reviewOpened = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    await Run("instructions restore");
                    reviewOpened.Task.IsCompleted.Should().BeFalse("rename must not supply a same-source Save restore point");
                    File.ReadAllBytes(newPath).Should().Equal(renamedBytes);
                    File.Exists(oldPath).Should().BeFalse();
                    editor.Source.Text.Should().Be(saved.Document.Text);
                    editor.NameInput.Text.Should().Be("renamed");
                    await Run("instructions reload");
                    editor.Source.Text += "\r\nConfirmed changed body after rename";
                    reviewOpened = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var saving = Run("instructions save");
                    Click(await reviewOpened.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token), "Confirm save");
                    await saving;
                    var changed = (await store.Read("renamed", f.Token))!;
                    editor.RestorePrevious.IsEnabled.Should().BeTrue();
                    reviewOpened = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var restoring = Run("instructions restore");
                    var preview = await reviewOpened.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
                    preview.GetVisualDescendants().OfType<TextBox>().Select(box => box.Text)
                        .Should().Equal(changed.Document.Text, saved.Document.Text);
                    reviewOpened = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    Click(preview, "Review separate Save confirmation");
                    var confirmation = await reviewOpened.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
                    confirmation.GetVisualDescendants().OfType<TextBox>().Select(box => box.Text)
                        .Should().Equal(changed.Document.Text, saved.Document.Text);
                    Click(confirmation, "Confirm save");
                    await restoring;
                    var restored = (await store.Read("renamed", f.Token))!;
                    restored.Document.CopyBytes().Should().Equal(renamedBytes);
                    restored.Revision.Should().NotBe(saved.Revision).And.NotBe(changed.Revision);
                    editor.Source.Text.Should().Be(saved.Document.Text);
                    editor.NameInput.Text.Should().Be("renamed");
                    File.Exists(oldPath).Should().BeFalse();
                    Directory.GetFiles(Path.Combine(f.RoamingRoot, "Instructions")).Should().BeEquivalentTo([newPath, unrelatedPath]);
                    (await store.ReadRenameRecovery(f.Token)).Should().BeNull();
                    catalogue.Candidates.Should().NotContain(item => item.OwnedInstructionName == "example");
                    catalogue.Candidates.Single(item => item.OwnedInstructionName is "renamed").IsUseEnabled.Should().BeFalse();
                }
                editor.NameInput.Text.Should().Be("renamed");
                editor.Source.Text.Should().Be(saved.Document.Text);
                if (inventorySelection && !savedRestore)
                {
                    Click(editor, "Inspect");
                    await controller.WaitForOperation();
                }
                else { await Run("instructions inspect renamed"); }
                editor.NameInput.Text.Should().Be("renamed");
                editor.Source.Text.Should().Be(saved.Document.Text);
                string.Join("\n", editor.GetVisualDescendants().OfType<TextBlock>().Select(item => item.Text)).Should().Contain("Disabled/held");
                (await store.Read("example", f.Token)).Should().BeNull();
            }
            else if (scenario is not ("source" or "privacy"))
            { File.ReadAllBytes(oldPath).Should().Equal(source.Document.CopyBytes()); }
            if (scenario is "draft") { editor.Source.Text.Should().EndWith("New draft"); editor.Source.Text = source.Document.Text; }
            if (scenario is "privacy") { editor.Source.Text.Should().BeEmpty(); editor.RenameNameInput.Text.Should().BeEmpty(); }
            File.ReadAllBytes(unrelatedPath).Should().Equal([1, 2, 0, 3]);
        });
    }
}
