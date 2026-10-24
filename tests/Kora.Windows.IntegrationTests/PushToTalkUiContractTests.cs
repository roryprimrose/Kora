using System.Xml.Linq;

using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Controls;
using Kora.NativeUxFixture;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class PushToTalkUiContractTests
{
    [Fact]
    public void Settings_has_no_push_to_talk_control_or_command_capture_handlers()
    {
        var root = SourceRoot();
        var settings = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml"));
        settings.Descendants().Attributes().Where(attribute =>
            string.Equals(attribute.Value, "PushToTalkButton", StringComparison.Ordinal)
            || attribute.Value.Contains("BeginPushToTalk", StringComparison.Ordinal)
            || attribute.Value.Contains("EndPushToTalk", StringComparison.Ordinal)
            || attribute.Value.Contains("OnPushToTalk", StringComparison.Ordinal))
            .Should().BeEmpty();
        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml.cs"));
        codeBehind.Should().NotContain("pushToTalkButton")
            .And.NotContain("OnPushToTalk")
            .And.NotContain("BeginPushToTalk")
            .And.NotContain("EndPushToTalk")
            .And.NotContain("OnCaptureSurfaceUnavailable");
    }

    [Fact]
    public async Task Settings_retains_voice_configuration_and_recovery_without_a_capture_entrypoint()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            var recoveryRequests = 0;
            var window = new SettingsWindow(fixture.Main, () => recoveryRequests++);
            try
            {
                window.Show();
                window.ShowVoiceRecovery();
                window.UpdateLayout();
                window.FindControl<Button>("PushToTalkButton").Should().BeNull();
                var buttons = window.GetLogicalDescendants().OfType<Button>().ToArray();
                buttons.Should().NotContain(button =>
                    ReferenceEquals(button.Command, fixture.Main.BeginPushToTalkCommand)
                    || ReferenceEquals(button.Command, fixture.Main.EndPushToTalkCommand));
                foreach (var command in new[]
                {
                    fixture.Main.EnableVoiceConsentCommand,
                    fixture.Main.WithdrawVoiceConsentCommand,
                    fixture.Main.ToggleListeningCommand,
                    fixture.Main.OpenMicrophonePrivacySettingsCommand,
                })
                {
                    buttons.Should().Contain(button => ReferenceEquals(button.Command, command));
                }
                buttons.Should().NotContain(button => ReferenceEquals(button.Command, fixture.Main.RefreshMicrophonesCommand));
                var microphoneActions = window.GetVisualDescendants().OfType<SettingActionsButton>().Single(button =>
                    string.Equals(button.SettingName, "Microphone", StringComparison.Ordinal));
                var microphoneMenu = (MenuFlyout)microphoneActions.Flyout!;
                microphoneMenu.ShowAt(microphoneActions);
                window.UpdateLayout();
                var refresh = microphoneMenu.Items.OfType<MenuItem>().Single();
                refresh.Command.Should().BeSameAs(fixture.Main.RefreshMicrophonesCommand);
                ControlAutomationPeer.CreatePeerForElement(refresh)!.GetName().Should().Be("Refresh microphones");
                microphoneMenu.Hide();
                var recovery = window.FindControl<Button>("ChooseMicrophone")!;
                recovery.IsEnabled.Should().BeTrue();
                recovery.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                recoveryRequests.Should().Be(1);
                fixture.Main.VoiceConsentDescription.Should().Contain("Settings only manages listening permission and readiness");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Response_window_exposes_hold_to_talk_only_for_disabled_listening_mode()
    {
        var root = SourceRoot();
        var response = XDocument.Load(Path.Combine(root, "src", "Kora", "ResponseWindow.axaml"));
        var button = response.Descendants().Single(element =>
            string.Equals(element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value,
                "PushToTalkButton", StringComparison.Ordinal));

        button.Attribute("IsVisible")!.Value.Should().Be("{Binding IsPushToTalkVisible}");
        button.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanPushToTalk}");
        button.Attribute("AutomationProperties.Name")!.Value
            .Should().Be("Push to talk; hold while speaking");

        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "Kora", "ResponseWindow.axaml.cs"));
        codeBehind.Should().Contain("OnPushToTalkPressed")
            .And.Contain("OnPushToTalkReleased")
            .And.Contain("OnPushToTalkCaptureLost")
            .And.Contain("EndPushToTalkCommand.Execute(null)");
    }

    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kora.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Source root unavailable.");
    }
}
