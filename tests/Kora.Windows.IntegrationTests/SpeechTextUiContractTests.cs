using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class SpeechTextUiContractTests
{
    [Fact]
    public void Caption_is_native_selectable_nonactivating_and_settings_use_exact_choices_without_auto_speech()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md"))) { directory = directory.Parent; }
        var root = directory?.FullName ?? throw new InvalidOperationException("Source root unavailable.");
        var caption = XDocument.Load(Path.Combine(root, "src", "Kora", "SpeechCaptionWindow.axaml"));
        caption.Root!.Attribute("ShowActivated")!.Value.Should().Be("False");
        caption.Root.Attribute("ShowInTaskbar")!.Value.Should().Be("False");
        var text = caption.Descendants().Single(node => node.Name.LocalName is "TextBox");
        text.Attribute("Text")!.Value.Should().Be("{Binding SpeechCaptionText, Mode=OneWay}");
        text.Attribute("IsReadOnly")!.Value.Should().Be("True");
        text.Attribute("AutomationProperties.Name")!.Value.Should().Be("Current spoken utterance");
        caption.Descendants().Single(node => node.Name.LocalName is "Button")
            .Attribute("Command")!.Value.Should().Be("{Binding ToggleSpeechCaptionPinCommand}");
        var settings = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml")).Descendants().ToArray();
        settings.Single(node => string.Equals(node.Attribute("SelectedItem")?.Value, "{Binding SelectedSpeechTextChoice}", StringComparison.Ordinal))
            .Attribute("ItemsSource")!.Value.Should().Be("{Binding SpeechTextChoices}");
        foreach (var command in new[] { "RefreshSpeechTextCommand", "SaveSpeechTextCommand", "ResetSpeechTextCommand",
            "SaveSpeechCaptionOptionCommand", "ResetSpeechCaptionOptionCommand" })
        {
            settings.Single(node => string.Equals(node.Attribute("Command")?.Value, "{Binding " + command + "}", StringComparison.Ordinal))
                .Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangeSpeechText}");
        }
    }
}
