using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class WindowsSpeechRateUiContractTests
{
    [Fact]
    public void Native_rate_controls_bind_only_explicit_admitted_preferences_and_windows_synthesis_consumes_rate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md"))) { directory = directory.Parent; }
        var root = directory?.FullName ?? throw new InvalidOperationException("Source root unavailable.");
        var controls = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml")).Descendants().ToArray();
        var selector = controls.Single(c => string.Equals(c.Attribute("SelectedItem")?.Value, "{Binding SelectedWindowsSpeechRate}", StringComparison.Ordinal));
        selector.Attribute("ItemsSource")!.Value.Should().Be("{Binding WindowsSpeechRateChoices}");
        selector.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangeWindowsSpeechRateNative}");
        foreach (var command in new[] { "SaveWindowsSpeechRateCommand", "ResetWindowsSpeechRateCommand", "RefreshWindowsSpeechRateCommand" })
        {
            controls.Single(c => string.Equals(c.Attribute("Command")?.Value, "{Binding " + command + "}", StringComparison.Ordinal))
                .Attribute("IsEnabled")!.Value.Should().Be(command.StartsWith("Refresh", StringComparison.Ordinal)
                    ? "{Binding CanInspectWindowsSpeechRateNative}" : "{Binding CanChangeWindowsSpeechRateNative}");
        }
        controls.Any(c => string.Equals(c.Attribute("Text")?.Value, "{Binding WindowsSpeechRateStatus}", StringComparison.Ordinal)).Should().BeTrue();
        var native = File.ReadAllText(Path.Combine(root, "src", "Kora.Windows", "Audio", "WindowsTextToSpeechService.cs"));
        native.Should().Contain("rate => synthesizer.Rate = rate")
            .And.Contain("RateSupport = SpeechRateSupport.WindowsNative")
            .And.Contain("StartOwnedWindowsSynthesis(volume, () => synthesizer.SpeakAsync(prompt))");
        var kokoro = File.ReadAllText(Path.Combine(root, "src", "Kora.Windows", "Audio", "KokoroTextToSpeechProvider.cs"));
        kokoro.Should().NotContain("WindowsSpeechRate");
    }
}
