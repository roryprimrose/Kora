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
    public void SettingsGroupAppearanceBeforePresence()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        var tab = document.Descendants(avalonia + "TabItem")
            .Single(element => string.Equals(element.Attribute("Header")?.Value, "Appearance", StringComparison.Ordinal));

        var headings = tab.Descendants(avalonia + "TextBlock")
            .Where(element => string.Equals(element.Attribute("FontSize")?.Value, "18", StringComparison.Ordinal)
                || string.Equals(element.Attribute("Classes")?.Value, "settings-title", StringComparison.Ordinal))
            .Select(element => element.Attribute("Text")?.Value);

        headings.Should().Equal(
            "Appearance",
            "Application theme",
            "Native reader text scale",
            "Always show responses",
            "Keep responses on top",
            "Response timeout",
            "Presence",
            "Show presence",
            "Presence renderer",
            "Presence timeout",
            "Presence size",
            "Dot size",
            "Dot density",
            "Movement speed",
            "Speech sizing",
            "Speech scale amount");
    }

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
        document.Descendants().Any(element => string.Equals(element.Attribute("SelectedItem")?.Value, "{Binding SelectedAppearanceOption}", StringComparison.Ordinal)).Should().BeFalse();
        var composition = File.ReadAllText(Path.Combine(root, "src", "Kora", "Program.cs"));
        composition.Should().Contain("AddSingleton<AppearanceConfigurationService>()");
    }

    [Theory]
    [InlineData("Application theme", "ResetThemeCommand", "SelectedItem", "ThemeMode")]
    [InlineData("Native reader text scale", "ResetReaderTextScaleCommand", "Value", "ReaderTextScalePercent")]
    [InlineData("Always show responses", "ResetResponseAlwaysVisibleCommand", "IsChecked", "IsResponseAlwaysVisible")]
    [InlineData("Keep responses on top", "ResetResponseWindowTopmostCommand", "IsChecked", "IsResponseWindowTopmost")]
    [InlineData("Response timeout", "ResetResponseTimeoutCommand", "Value", "ResponseTimeoutSeconds")]
    [InlineData("Show presence", "ResetPresenceDisplayCommand", "IsChecked", "IsPresenceDisplayEnabled")]
    [InlineData("Presence renderer", "ResetPresenceRendererCommand", "SelectedItem", "SelectedPresenceRenderer")]
    [InlineData("Presence timeout", "ResetPresenceTimeoutCommand", "Value", "PresenceTimeoutSeconds")]
    [InlineData("Presence size", "ResetPresenceSizeCommand", "Value", "PresenceSizePixels")]
    [InlineData("Dot size", "ResetPresenceDotSizeCommand", "Value", "PresenceDotSizePercent")]
    [InlineData("Dot density", "ResetPresenceDotDensityCommand", "Value", "PresenceDotDensityPercent")]
    [InlineData("Movement speed", "ResetPresenceMovementSpeedCommand", "Value", "PresenceMovementSpeedPercent")]
    [InlineData("Speech sizing", "ResetPresenceSpeechScalingCommand", "IsChecked", "IsPresenceSpeechScalingEnabled")]
    [InlineData("Speech scale amount", "ResetPresenceSpeechScaleAmountCommand", "Value", "PresenceSpeechScaleAmountPercent")]
    public void EachSettingHasItsOwnResetMenu(string label, string command, string attribute, string binding)
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "Kora", "SettingsWindow.axaml"));
        var actions = document.Descendants().Single(element =>
            string.Equals(element.Name.LocalName, "SettingActionsButton", StringComparison.Ordinal)
            && string.Equals(element.Attribute("SettingName")?.Value, label, StringComparison.Ordinal));
        actions.Attribute("ResetCommand")!.Value.Should().Be("{Binding " + command + "}");
        actions.Parent!.Descendants().Single(element =>
            string.Equals(element.Attribute(attribute)?.Value, "{Binding " + binding + "}", StringComparison.Ordinal))
            .Should().NotBeNull();
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
            "ThemeMode", "ReaderTextScalePercent", "IsPresenceDisplayEnabled",
            "ResponseTimeoutSeconds", "PresenceTimeoutSeconds", "PresenceSizePixels",
            "PresenceDotSizePercent", "PresenceDotDensityPercent", "PresenceMovementSpeedPercent",
            "IsPresenceSpeechScalingEnabled", "PresenceSpeechScaleAmountPercent",
            "SelectedPresenceRenderer",
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
