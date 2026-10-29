using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class ConversationExecutionUiContractTests
{
    [Fact]
    public void NativeMaximumHasSeparateReviewConfirmationAndTruthfulStatusBindings()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        { directory = directory.Parent; }
        var root = directory?.FullName ?? throw new InvalidOperationException("The repository source root is unavailable.");
        var controls = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml")).Descendants().ToArray();
        controls.Single(control => string.Equals(control.Attribute("SelectedItem")?.Value,
            "{Binding SelectedConversationExecutionLimit}", StringComparison.Ordinal))
            .Attribute("ItemsSource")!.Value.Should().Be("{Binding ConversationExecutionLimits}");
        foreach (var command in new[] { "Refresh", "Review", "Confirm", "Cancel" })
        {
            controls.Single(control => string.Equals(control.Attribute("Command")?.Value,
                "{Binding " + command + "ConversationExecutionCommand}", StringComparison.Ordinal))
                .Attribute("IsEnabled")!.Value.Should().Be(command is "Confirm"
                    ? "{Binding CanConfirmConversationExecution}" : "{Binding CanChangeConversationExecution}");
        }
        controls.Any(control => string.Equals(control.Attribute("Text")?.Value,
            "{Binding ConversationExecutionStatus}", StringComparison.Ordinal)).Should().BeTrue();
    }
}
