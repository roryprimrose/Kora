using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class InCallFeedbackUiContractTests
{
    [Fact]
    public void Native_calls_controls_have_accessible_explicit_draft_refresh_save_reset_and_lifetime_binding()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kora.slnx"))) { directory = directory.Parent; }
        var root = directory?.FullName ?? throw new InvalidOperationException("Source root unavailable.");
        var tab = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml")).Descendants()
            .Single(control => string.Equals(control.Attribute("Header")?.Value, "Calls", StringComparison.Ordinal));
        var controls = tab.Descendants().ToArray();
        var selector = controls.Single(control => string.Equals(control.Attribute("SelectedItem")?.Value,
            "{Binding SelectedInCallFeedbackChoice}", StringComparison.Ordinal));
        selector.Attribute("ItemsSource")!.Value.Should().Be("{Binding InCallFeedbackChoices}");
        selector.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangeInCallFeedbackNative}");
        selector.Attribute("AutomationProperties.Name")!.Value.Should().NotBeNullOrWhiteSpace();
        foreach (var command in new[] { "RefreshInCallFeedbackCommand", "SaveInCallFeedbackCommand", "ResetInCallFeedbackCommand" })
        {
            var button = controls.Single(control => string.Equals(control.Attribute("Command")?.Value,
                "{Binding " + command + "}", StringComparison.Ordinal));
            button.Attribute("IsEnabled")!.Value.Should().Be(command.StartsWith("Refresh", StringComparison.Ordinal)
                ? "{Binding CanInspectInCallFeedbackNative}" : "{Binding CanChangeInCallFeedbackNative}");
            button.Attribute("AutomationProperties.Name")!.Value.Should().NotBeNullOrWhiteSpace();
        }
        var status = controls.Single(control => string.Equals(control.Attribute("Text")?.Value, "{Binding InCallFeedbackStatus}", StringComparison.Ordinal));
        status.Attribute("AutomationProperties.LiveSetting")!.Value.Should().Be("Polite");
        File.ReadAllText(Path.Combine(root, "src", "Kora", "SettingsWindowController.cs")).Should()
            .Contain("BindInCallFeedbackNativeLifetime");
        File.ReadAllText(Path.Combine(root, "src", "Kora", "Program.cs")).Should()
            .Contain("AddSingleton<InCallFeedbackConfigurationService>").And.Contain("AddSingleton<IInCallFeedbackPreferences>");
    }
}
