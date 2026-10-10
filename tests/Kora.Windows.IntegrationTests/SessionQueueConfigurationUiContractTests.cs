using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class SessionQueueConfigurationUiContractTests
{
    [Fact]
    public void Native_fixed_queue_options_and_per_option_resets_bind_shared_admission_and_visible_lifetime()
    {
        var root = SourceRoot();
        var controls = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml")).Descendants().ToArray();
        foreach (var (selected, choices) in new[] { ("SelectedQueuePending", "QueuePendingChoices"), ("SelectedQueueSlots", "QueueSlotChoices"),
            ("SelectedQueueLifetimeMinutes", "QueueLifetimeMinuteChoices"), ("SelectedQueueActiveBudgetMinutes", "QueueActiveBudgetMinuteChoices") })
        {
            var selector = controls.Single(control => string.Equals(control.Attribute("SelectedItem")?.Value, "{Binding " + selected + "}", StringComparison.Ordinal));
            selector.Attribute("ItemsSource")!.Value.Should().Be("{Binding " + choices + "}");
            selector.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangeQueueConfigurationNative}");
        }
        foreach (var command in new[] { "RefreshQueueConfigurationCommand", "SaveQueuePendingCommand", "ResetQueuePendingCommand",
            "SaveQueueSlotsCommand", "ResetQueueSlotsCommand", "SaveQueueLifetimeCommand", "ResetQueueLifetimeCommand",
            "SaveQueueActiveBudgetCommand", "ResetQueueActiveBudgetCommand" })
        {
            controls.Single(control => string.Equals(control.Attribute("Command")?.Value, "{Binding " + command + "}", StringComparison.Ordinal))
                .Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangeQueueConfigurationNative}");
        }
        var controller = File.ReadAllText(Path.Combine(root, "src", "Kora", "SettingsWindowController.cs"));
        controller.Should().Contain("BindQueueConfigurationNativeLifetime")
            .And.Contain("Volatile.Read(ref nativeVisible) == 1 && Volatile.Read(ref nativeVisibilityRevision) == revision");
        var composition = File.ReadAllText(Path.Combine(root, "src", "Kora", "Program.cs"));
        composition.Should().Contain("services.AddSingleton<ISessionQueuePreferences>")
            .And.Contain("services.AddSingleton<SessionQueueConfigurationService>()")
            .And.Contain("configuration: provider.GetRequiredService<SessionQueueConfigurationService>()");
    }

    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md"))) { directory = directory.Parent; }
        return directory?.FullName ?? throw new InvalidOperationException("The repository source root is unavailable.");
    }
}
