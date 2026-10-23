using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class SpeechComponentUiContractTests
{
    [Fact]
    public void SettingsAndInstallerKeepComponentPreparationSeparateFromSelectionAndLaunchRequirements()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        {
            directory = directory.Parent;
        }
        var root = directory?.FullName ?? throw new InvalidOperationException("The repository source root is unavailable.");
        var settings = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml"));
        var panel = settings.Descendants().Single(element =>
            string.Equals(element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value,
                "SpeechComponentsPanel", StringComparison.Ordinal));
        panel.Ancestors().Single(element => string.Equals(element.Name.LocalName, "TabItem", StringComparison.Ordinal))
            .Attribute("Header")!.Value.Should().Be("Speech & audio");
        File.ReadAllText(Path.Combine(root, "src", "Kora", "SettingsWindow.SpeechComponents.cs"))
            .Should().Contain("ReviewSpeechComponentAsync").And.Contain("ApplySpeechComponentAsync")
            .And.Contain("No qualified optional speech-input components")
            .And.NotContain("new SpeechComponentDefinition");
        File.ReadAllText(Path.Combine(root, "src", "Kora", "SettingsWindowController.cs"))
            .Should().Contain("BindSpeechComponentNativeLifetime");
        File.ReadAllText(Path.Combine(root, "installer", "Kora.Setup", "SetupWindow.axaml.cs"))
            .Should().Contain("SpeechComponentInstallChoice.InspectAsync").And.Contain("IsChecked = false")
            .And.Contain("speechChoices.Where(choice => choice.Selected)");
        File.ReadAllText(Path.Combine(root, "installer", "Kora.Setup", "OptionalComponentSetup.cs"))
            .Should().Contain("choice.PrepareAsync").And.NotContain("new SpeechComponentDefinition");
        var program = File.ReadAllText(Path.Combine(root, "src", "Kora", "Program.cs"));
        program.Should().Contain("SpeechComponentCatalogue.Production").And.Contain("ISpeechComponentReadiness")
            .And.Contain("UnavailableSpeechComponentRuntime");
    }
}
