using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Core.Artifacts;
using Kora.Core.Customizations;
using Kora.Definitions.Artifacts;
using Kora.Definitions.Prompts;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class CustomizationWindowTests
{
    [Fact]
    public Task BundledPromptInspectionShowsExactReadOnlyResourceWithoutOwnedFileOrAuthoring() =>
        HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
            var profile = Path.Combine(f.LocalRoot, "bundled-inspection");
            var store = new WindowsCustomizationStore(f, profile, Path.Combine(f.LocalRoot, "bundled-user"));
            var use = new ArtifactUsePreferences(f);
            using var embedded = EmbeddedArtifactCatalogue.Load();
            using var catalogue = new ArtifactCatalogue(embedded.Artifacts, use);
            var management = new CustomizationManagementService(store, admission, use, catalogue, f.Audit);
            var directories = new LocalArtifactDirectoryPreferences(f);
            using var reload = new ArtifactCatalogueReloadService(catalogue, f, directories,
                new ArtifactDirectoryConfigurationService(directories, f.Audit),
                new McpServerConfigurationService(f, directories, profile),
                NullLogger<ArtifactCatalogueReloadService>.Instance, profileRoamingRoot: profile);
            var authoring = new Authoring();
            using var controller = new CustomizationWindowController(f.Main, management, authoring, catalogue, reload,
                () => true, () => 1, NullLogger<CustomizationWindowController>.Instance);
            CustomizationWindow? opened = null;
            using var observe = Window.WindowOpenedEvent.AddClassHandler<CustomizationWindow>((window, _) => opened = window);
            controller.Bind();
            f.Main.CommandText = "edit Rewrite for clarity prompt";
            await f.Main.RunTypedCommand.ExecuteAsync();
            await controller.WaitForOperation();
            opened.Should().NotBeNull();
            var native = opened!;
            native.Kind.Should().Be(CustomizationKind.Prompt);
            native.Editor.IsReadOnly.Should().BeTrue();
            native.Editor.Text.Should().Be(EmbeddedPromptCatalogue.Load().Single().SourceText);
            native.Details.Text.Should().Contain("Rewrite for clarity | Built-in (read-only)");
            native.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Review and save"))
                .IsEnabled.Should().BeFalse();
            authoring.Calls.Should().Be(0);
            (await store.List(f.Token)).Should().BeEmpty();
            native.Close();
        });
}
