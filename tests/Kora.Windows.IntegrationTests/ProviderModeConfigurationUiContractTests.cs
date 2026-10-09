using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class ProviderModeConfigurationUiContractTests
{
    [Fact]
    public void NativeProviderModeUsesHostChoicesAndExplicitInspectSaveReset()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        { directory = directory.Parent; }
        var root = directory?.FullName ?? throw new InvalidOperationException("The repository source root is unavailable.");
        var controls = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml")).Descendants().ToArray();
        var selector = controls.Single(control => string.Equals(control.Attribute("SelectedItem")?.Value, "{Binding SelectedProviderModeChoice}", StringComparison.Ordinal));
        selector.Attribute("ItemsSource")!.Value.Should().Be("{Binding ProviderModeChoices}");
        selector.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangeProviderMode}");
        foreach (var command in new[] { "RefreshProviderModeCommand", "SaveProviderModeCommand", "ResetProviderModeCommand" })
        {
            controls.Single(control => string.Equals(control.Attribute("Command")?.Value, "{Binding " + command + "}", StringComparison.Ordinal))
                .Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangeProviderMode}");
        }
        controls.Any(control => string.Equals(control.Attribute("Text")?.Value, "{Binding ProviderModeConfigurationStatus}", StringComparison.Ordinal)).Should().BeTrue();
    }
}
