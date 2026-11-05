using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Core.Artifacts;
using Kora.Core.Customizations;
using Kora.Core.Mcp;
using Kora.Definitions.Artifacts;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class CustomizationWindowTests
{
    [Fact]
    public async Task BuiltInControlsSaveReviewedChoicesAndParentSuppressionWithoutChangingDefinitionDraftOrSourceVeto()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            await using var admission = new InstructionControlAdmission(fixture.Interactions, fixture.Interactions, new HostTaskCoordinator(fixture.Tasks));
            var profile = Path.Combine(fixture.LocalRoot, "tool-controls-profile");
            var store = new WindowsCustomizationStore(fixture, profile, Path.Combine(fixture.LocalRoot, "tool-controls-user"));
            var use = new ArtifactUsePreferences(fixture);
            using var catalogue = EmbeddedArtifactCatalogue.Load();
            var management = new CustomizationManagementService(store, admission, use, catalogue, fixture.Audit);
            var directories = new LocalArtifactDirectoryPreferences(fixture);
            var settings = new ArtifactDirectoryConfigurationService(directories, fixture.Audit);
            var policy = new McpCapabilityPreferences(use, directories, settings);
            var configuration = new McpServerConfigurationService(fixture, directories, profile, use, policy);
            using var reload = new ArtifactCatalogueReloadService(catalogue, fixture, directories, settings,
                configuration, NullLogger<ArtifactCatalogueReloadService>.Instance, profileRoamingRoot: profile);
            using var controller = new CustomizationWindowController(fixture.Main, management, new Authoring(), catalogue, reload,
                () => true, () => 1, NullLogger<CustomizationWindowController>.Instance,
                mcpConfiguration: configuration, mcpSettings: settings);
            var windows = new List<CustomizationWindow>();
            using var observe = Window.WindowOpenedEvent.AddClassHandler<CustomizationWindow>((window, _) => windows.Add(window));
            controller.Bind();
            controller.Open();
            await controller.WaitForOperation();
            var owner = windows.Should().ContainSingle().Subject;
            owner.Kind = CustomizationKind.McpServer;
            owner.CapabilityCategory.SelectedIndex = 1;
            var id = KoraMcpCatalog.ToolIds[0];
            var row = owner.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Tool: " + id));
            row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var selected = owner.SelectedCapability;
            owner.Editor.Text = "unsaved source buffer";
            owner.Request.Text = "unsaved editing request";
            owner.Search.Text = id;
            var manage = owner.GetLogicalDescendants().OfType<Button>().Single(button =>
                Equals(button.Content, "Review selected server capabilities"));
            manage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var dialog = owner.OwnedWindows.Should().ContainSingle().Subject.Should().BeOfType<McpCapabilityManagerWindow>().Subject;
            var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();
            buttons.Single(button => Equals(button.Content, "Disable all Tools")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var item = dialog.GetLogicalDescendants().OfType<CheckBox>().Single(check => Equals(check.Content, "Tool: " + id));
            item.IsChecked = true;
            dialog.GetLogicalDescendants().OfType<CheckBox>().Single(check => Equals(check.Content, "Server enabled in Kora")).IsChecked = false;
            buttons.Single(button => Equals(button.Content, "Review changes")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            policy.IsServerSelected(KoraMcpCatalog.ServerId).Should().BeTrue();
            dialog.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Confirm Save of these preferences"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await controller.WaitForOperation();
            owner.Status.Text.Should().Contain("saved and verified");
            owner.Editor.Text.Should().Be("unsaved source buffer");
            owner.Request.Text.Should().Be("unsaved editing request");
            owner.Search.Text.Should().Be(id);
            var restarted = new McpCapabilityPreferences(new ArtifactUsePreferences(fixture),
                new LocalArtifactDirectoryPreferences(fixture), settings);
            restarted.IsServerSelected(KoraMcpCatalog.ServerId).Should().BeFalse();
            restarted.IsToolSelected(KoraMcpCatalog.ServerId, id).Should().BeTrue();
            restarted.IsToolSelected(KoraMcpCatalog.ServerId, KoraMcpCatalog.ToolIds[1]).Should().BeFalse();
            restarted.IsToolEnabled(KoraMcpCatalog.ServerId, id).Should().BeFalse();
            use.IsDisabled(ArtifactUseIdentity.Mcp(KoraMcpCatalog.ServerId)).Should().BeFalse();
            controller.Dispose();
            owner.Search.Text.Should().BeEmpty();
            owner.SelectedCapability.Should().BeNull();
            owner.CapabilityCount.Text.Should().BeEmpty();
            selected.Should().NotBeNull();
        });
    }
}