using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class AuditRetentionUiContractTests
{
    [Fact]
    public void Native_draft_shared_registry_current_lifetime_and_bootstrap_precede_required_audit_writes()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        {
            directory = directory.Parent;
        }
        var root = directory?.FullName ?? throw new InvalidOperationException("Source root unavailable.");
        var controls = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml")).Descendants().ToArray();
        var selector = controls.Single(control =>
            string.Equals(control.Attribute("SelectedItem")?.Value, "{Binding SelectedAuditRetentionDays}", StringComparison.Ordinal));
        selector.Attribute("ItemsSource")!.Value.Should().Be("{Binding AuditRetentionChoices}");
        selector.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangeAuditRetentionNative}");
        foreach (var command in new[] { "SaveAuditRetentionCommand", "ResetAuditRetentionCommand", "RefreshAuditRetentionCommand" })
        {
            controls.Single(control => string.Equals(control.Attribute("Command")?.Value, "{Binding " + command + "}", StringComparison.Ordinal))
                .Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangeAuditRetentionNative}");
        }
        File.ReadAllText(Path.Combine(root, "src", "Kora", "SettingsWindowController.cs"))
            .Should().Contain("BindAuditRetentionNativeLifetime").And.Contain("Volatile.Read(ref nativeVisibilityRevision) == revision");
        var composition = File.ReadAllText(Path.Combine(root, "src", "Kora", "Program.cs"));
        composition.Should().Contain("new WindowsSqliteHostInteractionStore(paths, tasks, auditPolicy: auditPolicy, sessionRetentionPolicy: sessionPolicy)")
            .And.Contain("diagnosticPolicy: diagnosticPolicy, auditPolicy: auditPolicy")
            .And.Contain("services.AddSingleton(auditPolicy)")
            .And.Contain("services.AddSingleton<IAuditRetentionSessionStore>(interactions)")
            .And.Contain("services.AddSingleton<AuditRetentionConfigurationService>()")
            .And.Contain("throw new AuditRetentionUnavailableException(exception)")
            .And.Contain("Inspect device-local audit preference and required audit/intent receipts")
            .And.Contain("no fallback policy or automatic replay");
        composition.IndexOf("auditConfiguration.Observe()", StringComparison.Ordinal)
            .Should().BeLessThan(composition.IndexOf(".RecoverAsync(CancellationToken.None)", StringComparison.Ordinal));
        composition.IndexOf("new LocalSessionRetentionPreferences(paths).Load()", StringComparison.Ordinal)
            .Should().BeGreaterThan(0)
            .And.BeLessThan(composition.IndexOf("interactions.InitializeAsync(", StringComparison.Ordinal));
    }
}
