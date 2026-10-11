using System.Xml.Linq;

using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class ArtifactDirectoryConfigurationUiContractTests
{
    [Fact]
    public void Settings_exposes_kind_scoped_add_remove_and_refresh_controls()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        {
            directory = directory.Parent;
        }
        var root = directory?.FullName
            ?? throw new InvalidOperationException("The repository source root is unavailable.");
        var controls = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml"))
            .Descendants()
            .ToArray();

        controls.Single(control =>
            string.Equals(control.Attribute("SelectedItem")?.Value,
                "{Binding SelectedArtifactDirectoryKind}", StringComparison.Ordinal))
            .Attribute("ItemsSource")!.Value.Should().Be("{Binding ArtifactDirectoryKinds}");
        controls.Single(control =>
            string.Equals(control.Attribute("ItemsSource")?.Value,
                "{Binding AdditionalArtifactDirectories}", StringComparison.Ordinal))
            .Attribute("SelectedItem")!.Value.Should().Be("{Binding SelectedArtifactDirectory}");
        foreach (var command in new[]
                 {
                     "AddArtifactDirectoryCommand",
                     "RemoveArtifactDirectoryCommand",
                     "RefreshArtifactDirectoriesCommand",
                 })
        {
            controls.Count(control =>
                string.Equals(control.Attribute("Command")?.Value,
                    "{Binding " + command + "}", StringComparison.Ordinal))
                .Should().Be(1);
        }
        controls.Any(control =>
            string.Equals(control.Attribute("Text")?.Value,
                "{Binding ArtifactDirectoryStatus}", StringComparison.Ordinal))
            .Should().BeTrue();
        controls.Single(control =>
            string.Equals(control.Attribute("ItemsSource")?.Value,
                "{Binding McpServers}", StringComparison.Ordinal))
            .Attribute("SelectedItem")!.Value.Should().Be("{Binding SelectedMcpServer}");
        foreach (var command in new[] { "IncludeMcpServerCommand", "ExcludeMcpServerCommand" })
        {
            controls.Count(control =>
                string.Equals(control.Attribute("Command")?.Value,
                    "{Binding " + command + "}", StringComparison.Ordinal))
                .Should().Be(1);
        }
    }
}
