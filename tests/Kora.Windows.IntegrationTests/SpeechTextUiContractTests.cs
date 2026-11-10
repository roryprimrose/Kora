using System.Xml.Linq;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Commands;
using Kora.Core.Voice;

namespace Kora.Windows.IntegrationTests;

public sealed class SpeechTextUiContractTests
{
    [Fact]
    public void Natural_sentence_and_number_word_delay_share_exact_native_choices_and_recognition_vocabulary()
    {
        var sentence = SpeechTextCommand.Parse("Kora, show the current sentence", "Kora")!;
        sentence.Operation.Should().Be(AppearanceCommandOperation.Set);
        sentence.Value.Should().Be(SpeechTextMode.CurrentSentence);
        sentence.Should().Be(SpeechTextCommand.Parse("set display.speech-text to CurrentSentence", "Kora"));
        foreach (var phrase in new[] { "keep speech text visible for ten seconds",
            "keep speech text visible for twenty-one seconds", "keep speech text visible for twenty one seconds" })
        {
            var command = SpeechTextCommand.Parse("Kora, " + phrase, "Kora")!;
            command.CaptionOption.Should().Be(SpeechCaptionOption.DismissalDelay);
            command.CaptionValue.Should().Be(new SpeechCaptionValue.Delay(phrase.Contains("ten", StringComparison.Ordinal) ? 10 : 21));
            SpeechTextCommand.FixedPhrases.Should().Contain(phrase);
        }
        SpeechTextCommand.FixedPhrases.Should().Contain("show the current sentence");
    }

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
        text.Attribute("Text").Should().BeNull("only the revalidating controller may publish caption content");
        text.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name")!
            .Value.Should().Be("CaptionText");
        text.Attribute("IsReadOnly")!.Value.Should().Be("True");
        text.Attribute("AutomationProperties.Name")!.Value.Should().Be("Current spoken utterance");
        caption.Descendants().Single(node => node.Name.LocalName is "Button")
            .Attribute("Command")!.Value.Should().Be("{Binding ToggleSpeechCaptionPinCommand}");
        var settings = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsView.axaml")).Descendants().ToArray();
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
