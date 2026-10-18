using System.Xml.Linq;

using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class SessionRetentionPlannerUiContractTests
{
    [Fact]
    public void NativePlannerHasSeparateExplicitProposalAndReadOnlyStatusWithoutApplyRoute()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        {
            directory = directory.Parent;
        }
        var root = directory?.FullName ?? throw new InvalidOperationException("Source root unavailable.");
        var controls = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml")).Descendants().ToArray();
        foreach (var proposal in new[] { "ProposedSessionArchiveDays", "ProposedSessionDeleteDays" })
        {
            var control = controls.Single(c =>
                string.Equals(c.Attribute("SelectedItem")?.Value, "{Binding " + proposal + "}", StringComparison.Ordinal));
            control.Attribute("ItemsSource")!.Value.Should().Be("{Binding SessionRetentionChoices}");
            control.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangeSessionRetentionNative}");
            control.Attribute("AutomationProperties.Name")!.Value.Should().Contain("read-only preview");
        }
        controls.Single(c => c.Attribute("Command")?.Value == "{Binding PreviewSessionRetentionCommand}")
            .Attribute("IsEnabled")!.Value.Should().Be("{Binding CanPreviewSessionRetention}");
        controls.Single(c => c.Attribute("Text")?.Value == "{Binding SessionRetentionPlanStatus}")
            .Attribute("AutomationProperties.Name")!.Value.Should().Be("Read-only session retention impact result");
        var source = File.ReadAllText(Path.Combine(root, "src", "Kora.Application", "Configuration", "SessionRetentionPlannerService.cs"));
        source.Should().NotContain("SaveAsync").And.NotContain("ApplyRetentionAsync").And.NotContain("RecordControlIntentAsync");
        var composition = File.ReadAllText(Path.Combine(root, "src", "Kora", "Program.cs"));
        composition.Should().Contain("AddSingleton<ISessionRetentionPlannerReader>")
            .And.Contain("new WindowsSqliteSessionRetentionPlannerReader(interactions, paths)")
            .And.Contain("AddSingleton<SessionRetentionPlannerService>");
        var controller = File.ReadAllText(Path.Combine(root, "src", "Kora", "SettingsWindowController.cs"));
        controller.Should().Contain("BindSessionRetentionNativeLifetime").And.Contain("nativeVisibilityRevision) == revision");
    }
}
