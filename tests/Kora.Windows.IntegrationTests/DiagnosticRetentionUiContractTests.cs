using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class DiagnosticRetentionUiContractTests
{
    [Fact]
    public void Native_future_SQLite_only_draft_and_explicit_controls_use_the_shared_policy_and_current_surface_lifetime()
    {
        var root = SourceRoot();
        var controls = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml")).Descendants().ToArray();
        var selector = controls.Single(control =>
            string.Equals(control.Attribute("SelectedItem")?.Value, "{Binding SelectedDiagnosticRetentionDays}", StringComparison.Ordinal));
        selector.Attribute("ItemsSource")!.Value.Should().Be("{Binding DiagnosticRetentionChoices}");
        selector.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangeDiagnosticRetentionNative}");
        foreach (var command in new[] { "SaveDiagnosticRetentionCommand", "ResetDiagnosticRetentionCommand", "RefreshDiagnosticRetentionCommand" })
        {
            controls.Single(control => string.Equals(control.Attribute("Command")?.Value, "{Binding " + command + "}", StringComparison.Ordinal))
                .Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangeDiagnosticRetentionNative}");
        }
        controls.Any(control => string.Equals(control.Attribute("Text")?.Value, "{Binding DiagnosticRetentionStatus}", StringComparison.Ordinal))
            .Should().BeTrue();
        var controller = File.ReadAllText(Path.Combine(root, "src", "Kora", "SettingsWindowController.cs"));
        controller.Should().Contain("BindDiagnosticRetentionNativeLifetime")
            .And.Contain("settingsWindow.PropertyChanged += OnWindowPropertyChanged")
            .And.Contain("window.PropertyChanged -= OnWindowPropertyChanged")
            .And.Contain("eventArgs.Property == Visual.IsVisibleProperty")
            .And.Contain("Interlocked.Increment(ref nativeVisibilityRevision)")
            .And.Contain("Volatile.Read(ref nativeVisible) == 1 && Volatile.Read(ref nativeVisibilityRevision) == revision")
            .And.NotContain("settingsWindow.IsVisible");
        var composition = File.ReadAllText(Path.Combine(root, "src", "Kora", "Program.cs"));
        composition.Should().Contain("new WindowsSqliteEvidenceSink(paths, diagnosticPolicy: diagnosticPolicy, auditPolicy: auditPolicy)")
            .And.Contain("services.AddSingleton(diagnosticPolicy)")
            .And.Contain("services.AddSingleton<IDiagnosticRetentionSessionStore>(interactions)")
            .And.Contain("services.AddSingleton<DiagnosticRetentionConfigurationService>()");
        composition.IndexOf("diagnosticConfiguration.Observe()", StringComparison.Ordinal)
            .Should().BeLessThan(composition.IndexOf("DesktopLog.Information(startupLogger, \"Starting Kora desktop host\")", StringComparison.Ordinal));
    }

    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("The repository source root is unavailable.");
    }
}
