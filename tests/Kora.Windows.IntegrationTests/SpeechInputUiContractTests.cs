using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class SpeechInputUiContractTests
{
    [Fact]
    public void SpeechInputControlsUseExplicitSelectionAndTheSameHostAction()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md"))) { directory = directory.Parent; }
        var root = directory?.FullName ?? throw new InvalidOperationException("Source root unavailable.");
        var controls = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml")).Descendants().ToArray();
        foreach (var selection in new[] { "SelectedSpeechInputProfile", "SelectedSpeechInputResource", "SelectedSpeechInputResidency" })
        {
            var selector = controls.Single(control => string.Equals(control.Attribute("SelectedItem")?.Value, "{Binding " + selection + "}", StringComparison.Ordinal));
            selector.Name.LocalName.Should().Be("ComboBox");
            selector.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangeSpeechInputNative}");
            selector.Attributes().Should().Contain(attribute => string.Equals(attribute.Name.LocalName, "AutomationProperties.Name", StringComparison.Ordinal));
        }
        foreach (var command in new[] { "SaveSpeechInputCommand", "InspectSpeechInputCommand" })
        {
            controls.Single(control => string.Equals(control.Attribute("Command")?.Value, "{Binding " + command + "}", StringComparison.Ordinal))
                .Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangeSpeechInputNative}");
        }
        var program = File.ReadAllText(Path.Combine(root, "src", "Kora", "Program.cs"));
        program.Should().Contain("services.AddSingleton<ISpeechInputProfileRuntime>").And.Contain("services.AddSingleton<SpeechInputConfigurationService>()");
        var lifetime = File.ReadAllText(Path.Combine(root, "src", "Kora", "SettingsWindowController.cs"));
        lifetime.Should().Contain("BindSpeechInputNativeLifetime");
    }
}
