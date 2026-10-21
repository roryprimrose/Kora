using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class SessionQueueConfigurationUiContractTests
{
    [Fact]
    public void NativeExpiredReplacementReviewAndSeparateConfirmationUseActualSelectedQueueAndClearOnClosure()
    {
        var root = SourceRoot();
        var controls = XDocument.Load(Path.Combine(root, "src", "Kora", "SessionsWindow.axaml")).Descendants().ToArray();
        foreach (var (name, admission) in new[] { ("ReviewExpiredRequeue", "CanReviewExpiredRequeue"),
            ("ConfirmExpiredRequeue", "CanConfirmExpiredRequeue"), ("DiscardExpiredRequeue", "CanDiscardExpiredRequeue") })
        {
            controls.Single(control => control.Attributes().Any(attribute =>
                string.Equals(attribute.Name.LocalName, "Name", StringComparison.Ordinal)
                && string.Equals(attribute.Value, name, StringComparison.Ordinal)))
                .Attribute("IsEnabled")!.Value.Should().Be("{Binding " + admission + "}");
        }
        var controller = File.ReadAllText(Path.Combine(root, "src", "Kora", "SessionsWindow.axaml.cs"));
        controller.Should().Contain("await model.ReviewExpiredRequeueAsync()")
            .And.Contain("await model.ConfirmExpiredRequeueAsync()").And.Contain("await model.DiscardExpiredRequeueAsync()");
        var model = File.ReadAllText(Path.Combine(root, "src", "Kora", "SessionsViewModel.QueueRequeue.cs"));
        model.Should().Contain("SessionCommandOperation.QueueRequeueExpiredReview")
            .And.Contain("SessionCommandOperation.QueueRequeueExpiredConfirm")
            .And.Contain("SessionCommandOperation.QueueRequeueExpiredDiscard")
            .And.Contain("selectionEpoch == epoch").And.Contain("queueRequeueEpoch == selectionEpoch");
        File.ReadAllText(Path.Combine(root, "src", "Kora", "SessionsViewModel.cs")).Should().Contain("queueRequeueReview = null;");
    }

    [Fact]
    public void Native_fixed_queue_options_and_per_option_resets_bind_shared_admission_and_visible_lifetime()
    {
        var root = SourceRoot();
        var controls = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml")).Descendants().ToArray();
        foreach (var (selected, maximum) in new[] { ("SelectedQueuePending", "MaximumPendingPerSession"), ("SelectedQueueSlots", "MaximumExecutionSlots"),
            ("SelectedQueueLifetimeMinutes", "MaximumPendingLifetimeMinutes"), ("SelectedQueueActiveBudgetMinutes", "MaximumActiveBudgetMinutes") })
        {
            var selector = controls.Single(control => string.Equals(control.Attribute("Value")?.Value, "{Binding " + selected + "}", StringComparison.Ordinal));
            selector.Name.LocalName.Should().Be("SettingsNumberField");
            selector.Attribute("Minimum")!.Value.Should().Be("1");
            selector.Attribute("Maximum")!.Value.Should().Be("{x:Static hosting:SessionQueueLimits." + maximum + "}");
            selector.Attribute("SavedValue")!.Value.Should().Be("{Binding " + selected.Replace("Selected", "Saved", StringComparison.Ordinal) + "}");
            selector.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangeQueueConfigurationNative}");
        }
        foreach (var command in new[] { "RefreshQueueConfigurationCommand", "SaveQueuePendingCommand", "ResetQueuePendingCommand",
            "SaveQueueSlotsCommand", "ResetQueueSlotsCommand", "SaveQueueLifetimeCommand", "ResetQueueLifetimeCommand",
            "SaveQueueActiveBudgetCommand", "ResetQueueActiveBudgetCommand" })
        {
            controls.Single(control => string.Equals((control.Attribute("Command") ?? control.Attribute("ActionCommand"))?.Value, "{Binding " + command + "}", StringComparison.Ordinal))
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
