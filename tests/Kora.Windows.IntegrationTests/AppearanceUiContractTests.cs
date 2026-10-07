using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class AppearanceUiContractTests
{
    [Fact]
    public void Existing_direct_controls_bind_authoritative_domain_bounds_and_per_option_reset()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
            directory = directory.Parent;
        var root = directory?.FullName ?? throw new InvalidOperationException("The repository source root is unavailable.");
        var document = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml"));
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ResponseTimeoutSeconds"] = "ResponseWindowSettings.MinimumTimeoutSeconds",
            ["PresenceTimeoutSeconds"] = "PresenceSettings.MinimumTimeoutSeconds",
            ["PresenceSizePixels"] = "PresenceSettings.MinimumSizePixels",
            ["PresenceDotSizePercent"] = "PresenceSettings.MinimumDotSizePercent",
            ["PresenceDotDensityPercent"] = "PresenceSettings.MinimumDotDensityPercent",
            ["PresenceMovementSpeedPercent"] = "PresenceSettings.MinimumMovementSpeedPercent",
            ["PresenceSpeechScaleAmountPercent"] = "PresenceSettings.MinimumSpeechScaleAmountPercent",
        };
        foreach (var item in expected)
        {
            var control = document.Descendants().Single(element => string.Equals(element.Attribute("Value")?.Value, "{Binding " + item.Key + "}", StringComparison.Ordinal));
            control.Attribute("Minimum")!.Value.Should().Contain(item.Value);
            control.Attribute("Maximum")!.Value.Should().Contain(item.Value.Replace("Minimum", "Maximum", StringComparison.Ordinal));
        }
        document.Descendants().Any(element => string.Equals(element.Attribute("Text")?.Value, "{Binding AppearanceSettingStatus}", StringComparison.Ordinal)).Should().BeTrue();
        document.Descendants().Any(element => string.Equals(element.Attribute("Command")?.Value, "{Binding ResetAppearanceOptionCommand}", StringComparison.Ordinal)).Should().BeTrue();
        document.Descendants().Any(element => string.Equals(element.Attribute("SelectedItem")?.Value, "{Binding SelectedAppearanceOption}", StringComparison.Ordinal)).Should().BeTrue();
        var composition = File.ReadAllText(Path.Combine(root, "src", "Kora", "Program.cs"));
        composition.Should().Contain("AddSingleton<AppearanceConfigurationService>()");
    }
}
