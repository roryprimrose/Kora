using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class AudioOutputConfigurationUiContractTests
{
    [Fact]
    public void Native_output_selector_uses_presented_choices_and_explicit_metadata_save_and_reset()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        {
            directory = directory.Parent;
        }
        var root = directory?.FullName ?? throw new InvalidOperationException("The repository source root is unavailable.");
        var document = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml"));
        var controls = document.Descendants().ToArray();
        var selector = controls.Single(control =>
            string.Equals(control.Attribute("SelectedItem")?.Value, "{Binding SelectedOutputChoice}", StringComparison.Ordinal));
        selector.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangeAudioOutputDevice}");
        selector.Attribute("ItemsSource")!.Value.Should().Be("{Binding OutputDeviceChoices}");
        foreach (var command in new[] { "SaveOutputDeviceCommand", "ResetOutputDeviceCommand", "RefreshOutputDevicesCommand" })
        {
            controls.Single(control => string.Equals(control.Attribute("Command")?.Value, "{Binding " + command + "}", StringComparison.Ordinal))
                .Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangeAudioOutputDevice}");
        }
        controls.Any(control =>
            string.Equals(control.Attribute("Text")?.Value, "{Binding AudioOutputConfigurationStatus}", StringComparison.Ordinal))
            .Should().BeTrue();
    }
}
