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
    private static readonly string[] InventoryNames = ["enabled", "held", "other"];
    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task ActualNativeInventorySearchStateAndSelectionPreserveDirtyDraftAndRefuseStaleOriginal(ArtifactKind kind)
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
            var actions = kind == ArtifactKind.Instruction ? instructionActions : promptActions;
            var preferences = new LocalArtifactDirectoryPreferences(f);
            using var reload = new ArtifactCatalogueReloadService(catalogue, f, preferences,
                new ArtifactDirectoryConfigurationService(preferences, f.Audit),
                new McpServerConfigurationService(f, preferences, Path.Combine(f.LocalRoot, "no-profile")),
                NullLogger<ArtifactCatalogueReloadService>.Instance, instructions, instructionStore,
                Path.Combine(f.LocalRoot, "no-profile"), prompts, promptStore);
            var saved = new List<OwnedInstructionSnapshot>();
            foreach (var name in InventoryNames)
            {
                var text = $"---\nkora-format: 1\nname: {name}\ndescription: Private description\n---\nPrivate {name} body.";
                var document = actions.Validate(name, text, false);
                var result = await actions.Save(name, null, document, _ => Task.FromResult(true), () => true, TestContext.Current.CancellationToken);
                saved.Add(result.Current!);
            }
            reload.ReloadNow().Should().BeTrue();
            await actions.Activate(saved[0], _ => Task.FromResult(true), () => true, TestContext.Current.CancellationToken);
            var ownership = 1L;
            var owned = true;
            using var controller = new OwnedInstructionEditorController(f.Main, instructionActions, new(instructionActions), reload, f,
                () => owned, () => ownership, NullLogger<OwnedInstructionEditorController>.Instance, promptActions, new(promptActions));
            OwnedInstructionEditorWindow? editor = null;
            Window? dialog = null;
            TaskCompletionSource<Window>? reviewOpened = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window is OwnedInstructionEditorWindow selected) { editor = selected; }
                else { dialog = window; reviewOpened?.TrySetResult(window); }
            });
            controller.Bind();
            async Task Run(string operation)
            {
                f.Main.CommandText = (kind == ArtifactKind.Instruction ? "instructions " : "prompts ") + operation;
                await f.Main.RunTypedCommand.ExecuteAsync();
            }
            await Run("list");
            editor.Should().NotBeNull();
            var native = editor!;
            var statusBlock = ((DockPanel)native.Content!).Children.OfType<TextBlock>().Last();
            OwnedDefinitionInventoryRow[] Rows() => native.Files.ItemsSource!.Cast<OwnedDefinitionInventoryRow>().ToArray();
            string Status() => string.Join("\n", native.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
            Rows().Select(row => row.Name).Should().Equal("enabled", "held", "other");
            native.Files.SelectedItem.Should().BeNull();
            native.Source.Text.Should().BeNullOrEmpty();
            native.QueryInput.Focus();
            native.QueryInput.IsFocused.Should().BeTrue();
            native.UseStateInput.SelectedItem = OwnedDefinitionUseFilter.UseEnabled;
            Rows().Should().ContainSingle().Which.Name.Should().Be("enabled");
            native.UseStateInput.SelectedItem = OwnedDefinitionUseFilter.ReviewRequired;
            Rows().Select(row => row.Name).Should().Equal("held", "other");
            native.UseStateInput.SelectedItem = OwnedDefinitionUseFilter.UnavailableOrUnknown;
            Rows().Should().BeEmpty();
            native.UseStateInput.SelectedItem = OwnedDefinitionUseFilter.All;
            native.QueryInput.Text = "OWNED-NOT-AN-ID";
            Rows().Should().BeEmpty();
            native.QueryInput.Text = saved[0].Document.Definition.Id;
            Rows().Should().ContainSingle().Which.Name.Should().Be("enabled");
            native.QueryInput.Text = string.Empty;
            native.Files.SelectedItem = Rows()[0];
            Status().Should().Contain(saved[0].Revision).And.Contain(saved[0].Document.Definition.Digest)
                .And.NotContain("Private description").And.NotContain("Private enabled body");
            var sourceRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            statusBlock.PropertyChanged += (_, change) =>
            { if (change.Property == TextBlock.TextProperty && statusBlock.Text?.StartsWith("Owned kind ", StringComparison.Ordinal) == true) { sourceRead.TrySetResult(); } };
            native.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Inspect"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await sourceRead.Task.WaitAsync(TestContext.Current.CancellationToken);
            await Run("list");
            native.Source.Text += "\nUnsaved private draft.";
            var draft = native.Source.Text;
            var statePath = Path.Combine(f.LocalRoot, "Preferences", kind == ArtifactKind.Instruction ? "instruction-revisions.json" : "prompt-revisions.json");
            var revisionState = File.ReadAllBytes(statePath);
            var sourcePath = Path.Combine(f.RoamingRoot, OwnedDefinitionKind.DirectoryName(kind), saved[0].CanonicalFileName);
            var bytes = File.ReadAllBytes(sourcePath);
            native.Files.SelectedItem = Rows()[0];
            native.QueryInput.Text = "held";
            native.Files.SelectedItem.Should().BeNull();
            native.Source.Text.Should().Be(draft);
            Status().Should().Contain("choice withdrawn").And.Contain("draft remain unchanged");
            File.ReadAllBytes(sourcePath).Should().Equal(bytes);
            File.ReadAllBytes(statePath).Should().Equal(revisionState);
            native.QueryInput.Text = new string('a', 257);
            native.Files.ItemsSource.Should().BeNull();
            Status().Should().Contain("256").And.Contain("No truncation");
            native.Source.Text.Should().Be(draft);
            native.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Clear search"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Rows().Should().HaveCount(3);
            native.Files.SelectedItem.Should().BeNull();
            native.Files.SelectedItem = Rows()[1];
            // Same filename, replaced content after the observation: native Inspect must not show it.
            var heldPath = Path.Combine(f.RoamingRoot, OwnedDefinitionKind.DirectoryName(kind), saved[1].CanonicalFileName);
            File.WriteAllBytes(heldPath, ArtifactFileDocument.Encode(saved[1].Document.Text + "\nReplaced externally.", false));
            reviewOpened = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var refused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            statusBlock.PropertyChanged += (_, change) =>
            { if (change.Property == TextBlock.TextProperty && statusBlock.Text?.Contains("Refresh and reselect", StringComparison.Ordinal) == true) { refused.TrySetResult(); } };
            native.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Inspect"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dialog = await reviewOpened.Task.WaitAsync(TestContext.Current.CancellationToken);
            dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Discard draft and continue"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await refused.Task.WaitAsync(TestContext.Current.CancellationToken);
            native.Source.Text.Should().Be(draft);
            Status().Should().Contain("Refresh and reselect").And.NotContain("Replaced externally.");
            native.QueryInput.Text = string.Empty;
            catalogue.ReportReloadFailure(new IOException("Synthetic permission/enumeration failure"));
            native.Files.ItemsSource.Should().BeNull();
            native.NextPage.IsEnabled.Should().BeFalse();
            Status().Should().Contain("Historical observations").And.Contain("unavailable");
            native.Source.Text.Should().Be(draft);
            native.QueryInput.Text = "private query";
            ownership++;
            owned = false;
            await Run("list");
            native.IsVisible.Should().BeFalse();
            native.QueryInput.Text.Should().BeEmpty();
            native.Source.Text.Should().BeEmpty();
            native.Files.ItemsSource.Should().BeNull();
            catalogue.Publish(catalogue.Candidates);
            native.Files.ItemsSource.Should().BeNull();
        });
    }

    [Fact]
    public async Task ActualWindowUsesExplicitMetadataPageControlsWithoutSelectingFirstRowAndClearsOnPrivateClosure()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var operations = new List<string>();
            var window = new OwnedInstructionEditorWindow(operations.Add);
            window.Show();
            try
            {
                var names = Enumerable.Range(0, 128).Select(index => index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + new string('界', 125)).ToArray();
                var candidates = names.Select((name, index) => new ArtifactDefinition("owned." + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ArtifactKind.Prompt, new string('界', 128), "Private description", "cmd-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["spoken " + index.ToString(System.Globalization.CultureInfo.InvariantCulture)], "kora-profile", "1", new string('a', 64), "Private body")
                    { OwnedInstructionName = name, OwnedInstructionRevision = new string('a', 256), IsUseEnabled = false }).ToArray();
                var snapshot = new OwnedDefinitionInventory(ArtifactKind.Prompt, names, Array.AsReadOnly(candidates), 0, DateTimeOffset.UtcNow);
                window.SelectKind(ArtifactKind.Prompt);
                var first = snapshot.Page("", OwnedDefinitionUseFilter.All);
                window.ShowInventory(first);
                window.Files.SelectedItem.Should().BeNull();
                window.NextPage.IsEnabled.Should().BeTrue();
                window.NextPage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                operations.Should().ContainSingle().Which.Should().Be("Next");
                window.ShowInventory(snapshot.Page("", OwnedDefinitionUseFilter.All, first.NextOffset!.Value));
                window.Files.SelectedItem.Should().BeNull();
                window.QueryInput.Text = "private query";
                window.Source.Text = "private draft";
                window.ClearPrivateText();
                window.QueryInput.Text.Should().BeEmpty();
                window.Source.Text.Should().BeEmpty();
                window.Files.ItemsSource.Should().BeNull();
                window.NextPage.IsEnabled.Should().BeFalse();
                window.UseStateInput.SelectedItem.Should().Be(OwnedDefinitionUseFilter.All);
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task ActualAdmittedControllerPagesCompleteMaximumNativeInventoryAndClearSearchReturnsToFirstPage(ArtifactKind kind)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            var folder = Path.Combine(f.RoamingRoot, OwnedDefinitionKind.DirectoryName(kind));
            Directory.CreateDirectory(folder);
            var names = Enumerable.Range(0, OwnedDefinitionKind.MaximumFiles(kind)).Select(index =>
                index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + new string('a', 61)).ToArray();
            foreach (var (name, index) in names.Select((name, index) => (name, index)))
            {
                var declared = "inventory-" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)
                    + string.Concat(Enumerable.Repeat("\U0001F600", 57));
                File.WriteAllBytes(Path.Combine(folder, name + OwnedDefinitionKind.Extension(kind)),
                    ArtifactFileDocument.Encode($"---\nkora-format: 1\nname: {declared}\ndescription: Private\n---\nPrivate body.", false));
            }
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
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            { if (window is OwnedInstructionEditorWindow selected) { editor = selected; } });
            controller.Bind();
            f.Main.CommandText = kind == ArtifactKind.Instruction ? "instructions list" : "prompts list";
            await f.Main.RunTypedCommand.ExecuteAsync();
            var native = editor!;
            native.Files.ItemsSource.Should().NotBeNull("the exact native inventory must publish; status: {0}",
                string.Join("\n", native.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text)));
            OwnedDefinitionInventoryRow[] Rows() => native.Files.ItemsSource!.Cast<OwnedDefinitionInventoryRow>().ToArray();
            var first = Rows();
            first.Should().NotBeEmpty().And.HaveCountLessThan(128);
            native.NextPage.IsEnabled.Should().BeTrue();
            native.Files.SelectedItem.Should().BeNull();
            var seen = new List<OwnedDefinitionInventoryRow>(first);
            while (native.NextPage.IsEnabled)
            {
                native.NextPage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var page = Rows();
                page.Should().NotBeEmpty();
                page.Sum(row => System.Text.Encoding.UTF8.GetByteCount(row.Details) + System.Text.Encoding.UTF8.GetByteCount(row.ToString()))
                    .Should().BeLessThanOrEqualTo(OwnedDefinitionInventory.MaximumDisplayUtf8Bytes);
                seen.AddRange(page);
                native.Files.SelectedItem.Should().BeNull();
                native.Source.Text.Should().BeNullOrEmpty();
            }
            seen.Select(row => row.Name).Should().Equal(names);
            seen.Should().OnlyContain(row => row.Kind == kind && row.ObservedUseEnabled == false && row.NativeRevision != null);
            native.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Clear search"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Rows().Should().Equal(first);
            native.NextPage.IsEnabled.Should().BeTrue();
            var statusBlock = ((DockPanel)native.Content!).Children.OfType<TextBlock>().Last();
            statusBlock.Text = "Before explicit native Refresh";
            var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            statusBlock.PropertyChanged += (_, change) =>
            { if (change.Property == TextBlock.TextProperty && statusBlock.Text?.StartsWith("Owned file inventory refreshed.", StringComparison.Ordinal) == true) { refreshed.TrySetResult(); } };
            native.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Refresh"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await refreshed.Task.WaitAsync(TestContext.Current.CancellationToken);
            Rows().Select(row => row.Name).Should().Equal(first.Select(row => row.Name));
            native.Files.SelectedItem.Should().BeNull();
            native.Source.Text.Should().BeNullOrEmpty();
        });
    }
}
