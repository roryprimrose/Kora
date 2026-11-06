using System.Collections.Immutable;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Customizations;
using Kora.Core.Mcp;
using Kora.NativeUxFixture;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class CustomizationWindowTests
{
    private static readonly string[] NonmatchingToolFilters = ["no match", "schema-only-marker"];
    [Fact]
    public async Task OneSearchFiltersAdmittedBuiltInAndExternalRowsWithoutRequeryOrDraftMutation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            var directories = new LocalArtifactDirectoryPreferences(fixture);
            var settings = new ArtifactDirectoryConfigurationService(directories, fixture.Audit);
            var use = new ArtifactUsePreferences(fixture);
            var policy = new McpCapabilityPreferences(use, directories, settings);
            var configuration = new McpServerConfigurationService(fixture, directories,
                Path.Combine(fixture.LocalRoot, "profile"), capabilities: policy);
            var server = new string('a', 64);
            var source = new CustomizationSource(server, CustomizationKind.McpServer, CustomizationScope.User,
                "External configuration", "fixture-mcp.json", "server.json", true);
            var actions = new List<string>();
            CustomizationWindow? window = null;
            window = new(action =>
            {
                actions.Add(action);
                if (string.Equals(action, "Filter", StringComparison.Ordinal)) { window!.ShowSources([source], _ => false, _ => actions.Add("Select")); }
            }, fixture.Main);
            window.Show();
            try
            {
                var builtIn = new McpDisplayedServer(new(KoraMcpCatalog.ServerId, "Kora",
                    configuration.ReadBuiltInTools().ToImmutableArray(), configuration.ReadCapabilitySelection(),
                    new(true, false, false), policy.IsServerSelected(KoraMcpCatalog.ServerId)), "Host catalogue", "Built-in", true);
                var external = new McpDisplayedServer(new(server, "External server",
                    [
                        new(McpCapabilityKind.Tool, "external.search", "Unique external tool", """{"private":"schema-only-marker"}"""),
                        new(McpCapabilityKind.Tool, "external.disabled", "Disabled external tool", "{}"),
                        new(McpCapabilityKind.Resource, "data://item", "Reference text", "{}"),
                        new(McpCapabilityKind.ResourceTemplate, "data://{value}", "Reference template", "{}"),
                        new(McpCapabilityKind.Prompt, "review", "Review text", "{}"),
                    ], McpCapabilitySelection.Default.SetItem(server, McpCapabilityKind.Tool, "external.disabled", false),
                    new(true, true, true), true), "fixture-mcp.json", "User", true);
                var reads = 0;
                IReadOnlyList<McpDisplayedServer> Admitted()
                {
                    reads++;
                    return [builtIn, external];
                }
                window.Kind = CustomizationKind.McpServer;
                window.SetCapabilityMetadata(Admitted(), () => actions.Add("Manage"));
                window.ShowSources([source], _ => false, _ => actions.Add("Select"));
                var sourceButton = window.GetLogicalDescendants().OfType<Button>().Single(button =>
                    button.Content is StackPanel panel && panel.Children.OfType<TextBlock>()
                        .Any(text => string.Equals(text.Text, source.Name, StringComparison.Ordinal)));
                sourceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Editor.Text = "unsaved definition";
                window.Request.Text = "unsaved authoring request";
                var selectedSource = window.SelectedSource;
                actions.Clear();
                window.CapabilityCategory.SelectedIndex = 1;
                var totalTools = KoraMcpCatalog.ToolIds.Count + 2;
                window.CapabilityCount.Text.Should().Be(totalTools + " of " + totalTools + " capabilities");
                window.Search.Text = " UNIQUE EXTERNAL ";
                window.CapabilityCount.Text.Should().Be("1 of " + totalTools + " capabilities");
                window.FilterSummary.Text.Should().Be("0 of 1 artifacts; 1 of " + totalTools + " capabilities");
                var selectedButton = window.GetLogicalDescendants().OfType<Button>().Single(button =>
                    Equals(button.Content, "Tool: Unique external tool"));
                selectedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var selectedTool = window.SelectedCapability;
                foreach (var filter in NonmatchingToolFilters)
                {
                    window.Search.Text = filter;
                    window.CapabilityCount.Text.Should().Be("0 of " + totalTools + " capabilities");
                }
                window.ClearFilter.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Search.Text.Should().BeEmpty();
                window.ClearFilter.IsEnabled.Should().BeFalse();
                window.FilterSummary.Text.Should().Be("1 of 1 artifacts; " + totalTools + " of " + totalTools + " capabilities");
                window.SelectedCapability.Should().BeSameAs(selectedTool);
                window.SelectedSource.Should().BeSameAs(selectedSource);
                window.Editor.Text.Should().Be("unsaved definition");
                window.Request.Text.Should().Be("unsaved authoring request");
                window.CapabilityCategory.SelectedIndex = 2;
                window.CapabilityCount.Text.Should().Be("2 of 2 capabilities");
                reads.Should().Be(1);
                actions.Should().OnlyContain(action => string.Equals(action, "Filter", StringComparison.Ordinal));
                policy.IsToolSelected(KoraMcpCatalog.ServerId, KoraMcpCatalog.ToolIds[0]).Should().BeTrue();
                window.ClearPrivateText();
                window.Search.Text.Should().BeEmpty();
                window.CapabilityCount.Text.Should().BeEmpty();
                window.SelectedCapability.Should().BeNull();
                window.SelectedSource.Should().BeNull();
                window.Search.Text = "late callback";
                window.CapabilityCount.Text.Should().BeEmpty();
                window.FilterSummary.Text.Should().BeEmpty();
                window.SelectedCapability.Should().BeNull();
            }
            finally { window.Close(); }
        });
    }
}