using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class DiagnosticImpactUiContractTests
{
    [Fact]
    public void Native_logging_has_separate_accessible_proposal_preview_status_cancel_clear_without_apply_or_save_routes()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        {
            directory = directory.Parent;
        }
        var root = directory?.FullName ?? throw new InvalidOperationException("Repository source root unavailable.");
        var controls = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml")).Descendants().ToArray();
        var proposal = controls.Single(control => string.Equals(control.Attribute("SelectedItem")?.Value,
            "{Binding ProposedDiagnosticImpactDays}", StringComparison.Ordinal));
        proposal.Attribute("ItemsSource")!.Value.Should().Be("{Binding DiagnosticRetentionChoices}");
        proposal.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangeDiagnosticRetentionNative}");
        proposal.Attribute("AutomationProperties.Name")!.Value.Should().Contain("ordinary diagnostic");
        proposal.Attribute("AutomationProperties.HelpText")!.Value.Should().Contain("does not scan or save");
        foreach (var (command, enabled) in new[]
        {
            ("PreviewDiagnosticImpactCommand", "CanPreviewDiagnosticImpact"),
            ("CancelDiagnosticImpactCommand", "CanCancelDiagnosticImpact"),
            ("ClearDiagnosticImpactCommand", "CanClearDiagnosticImpact"),
        })
        {
            var control = controls.Single(element => string.Equals(element.Attribute("Command")?.Value,
                "{Binding " + command + "}", StringComparison.Ordinal));
            control.Attribute("IsEnabled")!.Value.Should().Be("{Binding " + enabled + "}");
            control.Attribute("AutomationProperties.Name")!.Value.Should().NotBeNullOrWhiteSpace();
        }
        var status = controls.Single(control => string.Equals(control.Attribute("Text")?.Value,
            "{Binding DiagnosticImpactPreviewStatus}", StringComparison.Ordinal));
        status.Attribute("AutomationProperties.Name")!.Value.Should().Contain("Read-only");
        var composition = File.ReadAllText(Path.Combine(root, "src", "Kora", "Program.cs"));
        composition.Should().Contain("new WindowsSqliteDiagnosticImpactReader(provider.GetRequiredService<WindowsSegmentedEvidenceSink>(), paths)")
            .And.Contain("services.AddSingleton<DiagnosticImpactPreviewService>()");
    }
}
