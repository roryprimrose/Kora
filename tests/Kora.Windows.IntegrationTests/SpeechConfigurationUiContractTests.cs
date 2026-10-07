using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class SpeechConfigurationUiContractTests
{
    [Fact]
    public void Ready_selection_and_asset_review_are_separate_with_explicit_resets_and_live_status()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        {
            directory = directory.Parent;
        }
        var root = directory?.FullName ?? throw new InvalidOperationException("The repository source root is unavailable.");
        var document = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml"));
        var controls = document.Descendants().ToArray();
        var selected = controls.Single(control => string.Equals(control.Attribute("SelectedItem")?.Value,
            "{Binding SelectedInstalledSpeechProvider}", StringComparison.Ordinal));
        selected.Attribute("ItemsSource")!.Value.Should().Be("{Binding InstalledSpeechProviders}");
        var voice = controls.Single(control => string.Equals(control.Attribute("SelectedItem")?.Value,
            "{Binding SelectedVoice}", StringComparison.Ordinal));
        voice.Attribute("ItemsSource")!.Value.Should().Be("{Binding InstalledSpeechVoices}");
        foreach (var command in new[] { "ResetSpeechProviderCommand", "ResetSpeechVoiceCommand", "ResetSummarySentencesCommand", "ResetSummaryWordsCommand" })
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
