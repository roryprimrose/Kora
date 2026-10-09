using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class ResponseModeConfigurationUiContractTests
{
    [Fact]
    public void Native_device_default_uses_owned_choices_explicit_save_reset_and_complete_status()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        {
            directory = directory.Parent;
        }
        var root = directory?.FullName ?? throw new InvalidOperationException("The repository source root is unavailable.");
        var controls = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml")).Descendants().ToArray();
        var selector = controls.Single(control =>
            string.Equals(control.Attribute("SelectedItem")?.Value, "{Binding SelectedResponseModeChoice}", StringComparison.Ordinal));
        selector.Attribute("ItemsSource")!.Value.Should().Be("{Binding ResponseModeChoices}");
        selector.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangeResponseMode}");
        foreach (var command in new[] { "RefreshResponseModeCommand", "SaveResponseModeCommand", "ResetResponseModeCommand" })
        {
            controls.Single(control => string.Equals(control.Attribute("Command")?.Value, "{Binding " + command + "}", StringComparison.Ordinal))
                .Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangeResponseMode}");
        }
        controls.Any(control => string.Equals(control.Attribute("SelectedItem")?.Value, "{Binding DefaultResponseModeOption}", StringComparison.Ordinal))
            .Should().BeFalse();
        controls.Any(control => string.Equals(control.Attribute("Text")?.Value, "{Binding ResponseModeConfigurationStatus}", StringComparison.Ordinal))
            .Should().BeTrue();
    }
}
