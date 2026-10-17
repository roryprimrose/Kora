using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class SpeechConfigurationUiContractTests
{
    [Fact]
    public void One_cross_provider_voice_selector_keeps_asset_review_resets_and_live_status_separate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        {
            directory = directory.Parent;
        }
        var root = directory?.FullName ?? throw new InvalidOperationException("The repository source root is unavailable.");
        var document = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml"));
        var controls = document.Descendants().ToArray();
        controls.Any(control => string.Equals(control.Attribute("SelectedItem")?.Value,
            "{Binding SelectedInstalledSpeechProvider}", StringComparison.Ordinal)).Should().BeFalse();
        controls.Any(control => string.Equals(control.Attribute("Command")?.Value,
            "{Binding ResetSpeechProviderCommand}", StringComparison.Ordinal)).Should().BeFalse();
        var voice = controls.Single(control => string.Equals(control.Attribute("SelectedItem")?.Value,
            "{Binding SelectedVoice}", StringComparison.Ordinal));
        voice.Attribute("ItemsSource")!.Value.Should().Be("{Binding InstalledSpeechVoices}");
        var voiceCard = voice.Ancestors().First(control => string.Equals(control.Name.LocalName, "Border", StringComparison.Ordinal));
        voiceCard.Descendants().Count(control => string.Equals(control.Name.LocalName, "ComboBox", StringComparison.Ordinal))
            .Should().Be(1);
        voiceCard.Descendants().Any(control => string.Equals(control.Attribute("Text")?.Value,
            "Speech voice", StringComparison.Ordinal)).Should().BeTrue();
        voice.Descendants().Any(control => string.Equals(control.Attribute("Text")?.Value,
            "{Binding ConfigurationId}", StringComparison.Ordinal)).Should().BeTrue();
        var assetProvider = controls.Single(control => string.Equals(control.Attribute("SelectedItem")?.Value,
            "{Binding SelectedSpeechProvider}", StringComparison.Ordinal));
        assetProvider.Attribute("ItemsSource")!.Value.Should().Be("{Binding SpeechProviders}");
        foreach (var command in new[] { "ResetSpeechVoiceCommand", "ResetSummarySentencesCommand", "ResetSummaryWordsCommand",
            "DownloadSpeechProviderCommand", "RemoveSpeechProviderCommand", "PreviewVoiceCommand", "StopSpeechCommand" })
        {
            controls.Any(control => string.Equals(control.Attribute("Command")?.Value,
                "{Binding " + command + "}", StringComparison.Ordinal)).Should().BeTrue();
        }
        foreach (var pair in new[] { ("SelectedSummarySentences", "SummarySentenceChoices"), ("SelectedSummaryWords", "SummaryWordChoices") })
        {
            var choice = controls.Single(control => string.Equals(control.Attribute("SelectedItem")?.Value,
                "{Binding " + pair.Item1 + "}", StringComparison.Ordinal));
            choice.Attribute("ItemsSource")!.Value.Should().Be("{Binding " + pair.Item2 + "}");
            choice.Ancestors().Single(control => string.Equals(control.Name.LocalName, "TabItem", StringComparison.Ordinal))
                .Attribute("Header")!.Value.Should().Be("Speech & audio");
        }
        controls.Any(control => string.Equals(control.Attribute("Text")?.Value,
            "{Binding SpeechSettingStatus}", StringComparison.Ordinal)).Should().BeTrue();
        controls.Any(control => string.Equals(control.Attribute("Text")?.Value,
            "Speech provider assets (review only)", StringComparison.Ordinal)).Should().BeTrue();
        File.ReadAllText(Path.Combine(root, "src", "Kora", "Program.cs"))
            .Should().Contain("AddSingleton<SpeechConfigurationService>()").And.Contain("AddSingleton<ISpeechCatalog>");
    }
}
