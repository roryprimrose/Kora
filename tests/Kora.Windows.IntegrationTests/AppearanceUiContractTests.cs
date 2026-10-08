using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class AppearanceUiContractTests
{
    private static readonly string[] MouseEvents =
    [
        "PointerMovedEvent",
        "PointerPressedEvent",
        "PointerReleasedEvent",
        "PointerWheelChangedEvent",
    ];

    [Fact]
    public void Existing_direct_controls_bind_authoritative_domain_bounds_and_per_option_reset()
    {
        var root = FindRepositoryRoot();
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

    [Fact]
    public void Presence_and_response_windows_restart_their_timeout_for_all_mouse_events()
    {
        var root = FindRepositoryRoot();
        var windows = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MainWindow.axaml.cs"] = "SchedulePresenceTimeout();",
            ["ResponseWindow.axaml.cs"] = "RestartResponseTimeout();",
        };

        foreach (var window in windows)
        {
            var source = File.ReadAllText(Path.Combine(root, "src", "Kora", window.Key));
            foreach (var mouseEvent in MouseEvents)
            {
                source.Should().Contain(
                    $"AddHandler({mouseEvent}, OnMouseActivity, RoutingStrategies.Tunnel, handledEventsToo: true);");
            }

            source.Should().Contain("eventArgs.Pointer.Type == PointerType.Mouse")
                .And.Contain(window.Value);
        }
    }

    [Fact]
    public void Appearance_registry_controls_expose_screen_reader_accessible_names()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml"));
        foreach (var binding in new[]
        {
            "SelectedAppearanceOption", "ResetAppearanceOptionCommand", "ThemeMode",
            "ResponseTimeoutSeconds", "PresenceTimeoutSeconds", "PresenceSizePixels",
            "PresenceDotSizePercent", "PresenceDotDensityPercent", "PresenceMovementSpeedPercent",
            "IsPresenceSpeechScalingEnabled", "PresenceSpeechScaleAmountPercent",
        })
        {
            var control = document.Descendants().Single(element => element.Attributes().Any(attribute =>
                (attribute.Name.LocalName is "SelectedItem" or "Command" or "Value" or "IsChecked")
                && string.Equals(attribute.Value, "{Binding " + binding + "}", StringComparison.Ordinal)));
            control.Attribute("AutomationProperties.Name")!.Value.Should().NotBeNullOrWhiteSpace();
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository source root is unavailable.");
    }
}
