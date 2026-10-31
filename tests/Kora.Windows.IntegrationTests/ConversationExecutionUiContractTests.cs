using System.Xml.Linq;

using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Controls;
using Kora.NativeUxFixture;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ConversationExecutionUiContractTests
{
    [Fact]
    public async Task NativeMaximumMenuKeepsUnavailablePreferenceDisabledWithoutChangingDemand()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            var window = new SettingsWindow(fixture.Main);
            try
            {
                window.Show();
                var tabs = window.FindControl<TabControl>("SettingsTabs")!;
                tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab => tab.Header is "Responses");
                window.UpdateLayout();
                var maximum = window.GetVisualDescendants().OfType<ComboBox>().Single(control =>
                    ReferenceEquals(control.ItemsSource, fixture.Main.ConversationExecutionLimits));
                var cog = window.GetVisualDescendants().OfType<SettingActionsButton>().Single(control =>
                    control.SettingName is "Ordinary conversation execution maximum");
                var refresh = ((MenuFlyout)cog.Flyout!).Items.OfType<MenuItem>().Single();
                var status = fixture.Main.ConversationExecutionStatus;
                var demand = fixture.Main.SelectedConversationExecutionLimit;
                ((MenuFlyout)cog.Flyout!).ShowAt(cog);
                window.UpdateLayout();

                ControlAutomationPeer.CreatePeerForElement(cog)!.GetName()
                    .Should().Be("Ordinary conversation execution maximum actions");
                ControlAutomationPeer.CreatePeerForElement(refresh)!.GetName()
                    .Should().Be("Refresh ordinary conversation execution maximum");
                refresh.Header.Should().Be("Refresh");
                refresh.Command.Should().BeSameAs(fixture.Main.RefreshConversationExecutionCommand);
                maximum.IsEffectivelyEnabled.Should().BeFalse();
                cog.IsEffectivelyEnabled.Should().BeFalse();
                refresh.IsEnabled.Should().BeFalse();
                fixture.Main.CanConfirmConversationExecution.Should().BeFalse();
                status.Should().Contain("unavailable");

                fixture.Main.SelectedConversationExecutionLimit.Should().BeSameAs(demand);
                fixture.Main.ConversationExecutionStatus.Should().Be(status);
                fixture.Main.CanConfirmConversationExecution.Should().BeFalse();
                ((MenuFlyout)cog.Flyout!).Hide();
            }
            finally { window.Close(); }
        });
    }

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
