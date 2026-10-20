using Avalonia.Controls;

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
    [Theory]
    [InlineData(ArtifactKind.Instruction, false)]
    [InlineData(ArtifactKind.Prompt, false)]
    [InlineData(ArtifactKind.Instruction, true)]
    [InlineData(ArtifactKind.Prompt, true)]
    public async Task ActualControllerFencesMetadataFindAcrossSourceInputInventoryOwnershipAndDisposalWithoutIo(ArtifactKind kind, bool privacy)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
            var instructionStore = new InventoryStore(new WindowsOwnedInstructionStore(f));
            var promptStore = new InventoryStore(new WindowsOwnedInstructionStore(f, ArtifactKind.Prompt));
            var instructions = new InstructionRevisionCatalogue(f);
            var prompts = new InstructionRevisionCatalogue(f, ArtifactKind.Prompt);
            var catalogue = EmbeddedArtifactCatalogue.Load();
            var router = new BuiltInCommandRouter(new BuiltInCommandCatalog());
            var instructionActions = new OwnedInstructionEditorActions(instructionStore, admission, instructions, catalogue, f.Audit, router);
            var promptActions = new OwnedInstructionEditorActions(promptStore, admission, prompts, catalogue, f.Audit, router, ArtifactKind.Prompt);
            var actions = kind == ArtifactKind.Instruction ? instructionActions : promptActions;
            var store = kind == ArtifactKind.Instruction ? instructionStore : promptStore;
            var preferences = new LocalArtifactDirectoryPreferences(f);
            using var reload = new ArtifactCatalogueReloadService(catalogue, f, preferences,
                new ArtifactDirectoryConfigurationService(preferences, f.Audit),
                new McpServerConfigurationService(f, preferences, Path.Combine(f.LocalRoot, "no-profile")),
                NullLogger<ArtifactCatalogueReloadService>.Instance, instructions, instructionStore.Inner,
                Path.Combine(f.LocalRoot, "no-profile"), prompts, promptStore.Inner);
            var text = "---\nkora-format: 1\nname: example\ndescription: Hidden metadata description\n---\nBodyOnlySecret";
            var saved = await actions.Save("example", null, actions.Validate("example", text, false),
                _ => Task.FromResult(true), () => true, TestContext.Current.CancellationToken);
            reload.ReloadNow().Should().BeTrue();
            var owned = true;
            var ownership = 1L;
            using var controller = new OwnedInstructionEditorController(f.Main, instructionActions, new(instructionActions), reload, f,
                () => owned, () => ownership, NullLogger<OwnedInstructionEditorController>.Instance, promptActions, new(promptActions));
            OwnedInstructionEditorWindow? editor = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            { if (window is OwnedInstructionEditorWindow selected) { editor = selected; } });
            controller.Bind();
            f.Main.CommandText = kind == ArtifactKind.Instruction ? "instructions list" : "prompts list";
            await f.Main.RunTypedCommand.ExecuteAsync();
            var native = editor!;
            native.Source.Text = "Unsaved BodyOnlySecret";
            native.NameInput.Text = "unsaved-name";
            var draft = native.Source.Text;
            var reads = store.Reads;
            var lists = store.Lists;
            var writes = store.Writes;
            var selection = native.Files.ItemsSource!.Cast<OwnedDefinitionInventoryRow>().Single();
            void SelectAndFind()
            {
                native.Files.SelectedItem = null;
                selection = native.Files.ItemsSource!.Cast<OwnedDefinitionInventoryRow>().Single();
                native.Files.SelectedItem = selection;
                native.MetadataFindQuery.IsEnabled.Should().BeTrue();
                FindMetadataText(native, "example");
                native.MetadataFindMatch.Text.Should().Be("example");
            }
            SelectAndFind();
            foreach (var query in MetadataAbsentText)
            {
                FindMetadataText(native, query);
                native.MetadataFindStatus.Text.Should().Contain("0 of 0");
            }
            foreach (var retire in new Action[]
            {
                () => native.Source.Text += " raw edit",
                () => native.Source.IsReadOnly = true,
                () => native.Source.IsEnabled = false,
                () => native.NameInput.Text += "-edit",
                () => native.CopyNameInput.Text = "copy-id",
                () => f.Main.CommandText = "private changed input",
                () => native.QueryInput.Text = "example",
                () => native.DigestInput.Text = selection.Digest,
                () => native.RevisionInput.Text = selection.NativeRevision,
                () => native.ObservedFromInput.Text = selection.ObservedUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                () => native.ObservedUntilInput.Text = selection.ObservedUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                () => native.UseStateInput.SelectedItem = OwnedDefinitionUseFilter.ReviewRequired,
                () => native.OrderInput.SelectedItem = OwnedDefinitionInventoryOrder.FileNameDescending,
                () => native.QueryModeInput.SelectedItem = OwnedDefinitionInventoryQueryMode.ExactOwnedId,
                native.CancelReview,
            })
            {
                SelectAndFind();
                retire();
                AssertMetadataRetired(native);
            }
            native.Source.Text.Should().StartWith(draft!);
            native.Files.SelectedItem.Should().BeSameAs(selection);
            SelectAndFind();
            owned = false;
            ClickMetadata(native.MetadataFindNext);
            AssertMetadataRetired(native);
            owned = true;
            ClickMetadata(native.MetadataFindFirst);
            AssertMetadataRetired(native);
            SelectAndFind();
            ownership++;
            ClickMetadata(native.MetadataFindNext);
            AssertMetadataRetired(native);
            ownership--;
            SelectAndFind();
            catalogue.Publish(catalogue.Candidates);
            ClickMetadata(native.MetadataFindNext);
            AssertMetadataRetired(native);
            catalogue.ReportReloadFailure(new IOException("Synthetic reload unavailable"));
            native.Files.ItemsSource.Should().BeNull();
            native.Source.Text.Should().StartWith(draft!);
            actions.IsReviewedUseEnabled(saved.Current!).Should().BeFalse();
            store.Reads.Should().Be(reads);
            store.Lists.Should().Be(lists);
            store.Writes.Should().Be(writes);
            native.ClearSearch();
            f.Main.CommandText = kind == ArtifactKind.Instruction ? "instructions list" : "prompts list";
            await f.Main.RunTypedCommand.ExecuteAsync();
            SelectAndFind();
            if (privacy)
            {
                f.Main.CloseForObservedPrivacyEvent("Synthetic privacy closure", hidePresentation: true);
                await f.Main.PrivacyClosureTask;
            }
            else { controller.Dispose(); }
            AssertMetadataRetired(native);
            native.IsVisible.Should().BeFalse();
            native.Source.Text.Should().BeEmpty();
        });
    }
}
