using System.Xml.Linq;

using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class ExactGrantsWindowContractTests
{
    [Fact]
    public void NativeShellSeparatesTypedInventoryFromLegacyPreferencesAndExposesNamedExactConfirmationControls()
    {
        var source = Read("ExactGrantsWindow.axaml");
        var document = XDocument.Parse(source);
        foreach (var binding in new[] { "RefreshCommand", "NextCommand", "InspectCommand", "RevokeCommand", "Selected", "Inspection", "Reviewed", "Message",
            "PreviewBatchCommand", "RevokeBatchCommand", "BatchInspection", "BatchReviewed",
            "ReadHistoryCommand", "NextHistoryCommand", "HistoryRows", "HistorySummary",
            "HistoryOutcomeFilter}", "HistoryInitiatorFilter}", "HistoryReasonCodeFilter", "ClearHistoryFiltersCommand",
            "ScopeFilter}", "StatusFilter}", "ActionIdFilter", "OriginatingSessionFilter", "ClearFiltersCommand", "InventorySummary" })
        {
            var control = document.Descendants().Single(element => element.Attributes().Any(attribute =>
                attribute.Value.Contains("{Binding " + binding, StringComparison.Ordinal)));
            control.Attribute("AutomationProperties.Name")!.Value.Should().NotBeNullOrWhiteSpace();
        }
        source.Should().Contain("legacy named-action preferences")
            .And.Contain("unknown").And.Contain("does not stop or roll back")
            .And.Contain("exact displayed ID and revision");
        var status = document.Descendants().Single(element =>
            string.Equals(element.Attribute("Text")?.Value, "{Binding Message}", StringComparison.Ordinal));
        status.Attribute("AutomationProperties.LiveSetting")!.Value.Should().Be("Polite");
        document.Descendants().Single(element =>
            string.Equals(element.Attribute("AutomationProperties.Name")?.Value, "Retained exact operation grants", StringComparison.Ordinal))
            .Name.LocalName.Should().Be("ScrollableListBox");
        document.Descendants().Where(element => string.Equals(element.Name.LocalName, "Button", StringComparison.Ordinal))
            .Should().HaveCount(10);
        document.Descendants().Single(element =>
            string.Equals(element.Attribute("ItemsSource")?.Value, "{Binding HistoryRows}", StringComparison.Ordinal))
            .Name.LocalName.Should().Be("ScrollableListBox");
        source.Should().Contain("SelectionMode=\"Multiple\"").And.Contain("EVERY displayed exact batch ID")
            .And.Contain("SelectionChanged=\"OnSelectionChanged\"");
        source.Should().Contain("Stored status (not applicability)").And.Contain("correlation only")
            .And.Contain("Any by default").And.NotContain("Watermark=");
        Read("GrantListWindow.axaml").Should().NotContain("RevokeCommand");
        Read("App.ExactGrants.cs").Should().Contain("IExactGrantStore").And.Contain("ExactGrantControlAdmission");
        Read("SystemTrayController.cs").Should().Contain("Exact operation grants");
        Read("ExactGrantsWindowController.cs").Should().Contain("Dispatcher.UIThread.Invoke(WindowAdmitted)")
            .And.Contain("WindowAdmitted, logger");
    }

    private static string Read(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        {
            directory = directory.Parent;
        }
        if (directory is null) { throw new InvalidOperationException("The owned repository source root is unavailable."); }
        return File.ReadAllText(Path.Combine(directory.FullName, "src", "Kora", name));
    }
}