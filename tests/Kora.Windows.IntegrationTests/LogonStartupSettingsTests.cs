using System.Reflection;
using Avalonia.Controls;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Controls;
using Kora.NativeUxFixture;
using Kora.Windows.Startup;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class LogonStartupSettingsTests
{
    private static readonly string[] StartupControlNames =
    [
        "InspectLogonStartup", "EnableLogonStartup", "DisableLogonStartup",
        "ConfirmLogonStartup", "CancelLogonStartup", "LogonStartupActions",
    ];

    [Fact]
    public async Task UnavailableStartupControlsCannotEnterTheKeyboardCycle()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            var window = new SettingsWindow(fixture.Main);
            try
            {
                window.Show();
                window.UpdateLayout();
                foreach (var name in StartupControlNames)
                {
                    window.FindControl<Button>(name)!.IsEffectivelyEnabled.Should().BeFalse();
                }
                fixture.Main.HasVoiceConsent.Should().BeFalse();
                fixture.Main.IsVoiceEnabled.Should().BeFalse();
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task NativeSettingsUsesTheAdmittedSqliteWorkflowAndHideRetiresReviewWithoutRealRegistryAccess()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await using var admission = new LogonStartupAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var native = new WindowsLogonStartupRegistrationTests.Native();
            var preferences = new LocalLogonStartupPreferences(f);
            var configuration = new LogonStartupConfigurationService(preferences,
                new WindowsLogonStartupRegistration(native), admission, f.Audit, TimeProvider.System);
            await f.InitializeAsync(null, logonStartupConfiguration: configuration);
            using var controller = new SettingsWindowController(f.Main, NullLogger<SettingsWindowController>.Instance);
            f.Main.ShowSettings();
            var window = (SettingsWindow)typeof(SettingsWindowController).GetField("window", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;
            window.FindControl<Button>("EnableLogonStartup")!.Command.Should().BeSameAs(f.Main.EnableLogonStartupCommand);
            window.FindControl<Button>("ConfirmLogonStartup")!.Command.Should().BeSameAs(f.Main.ConfirmLogonStartupCommand);
            var actions = window.FindControl<SettingActionsButton>("LogonStartupActions")!;
            actions.ResetCommand.Should().BeSameAs(f.Main.ResetLogonStartupCommand);
            actions.IsEffectivelyEnabled.Should().BeTrue();
            await f.Main.InspectLogonStartupCommand.ExecuteAsync();
            await f.Main.EnableLogonStartupCommand.ExecuteAsync();
            native.Writes.Should().Be(0);
            window.Hide();
            f.Main.ShowSettings();
            await f.Main.ConfirmLogonStartupCommand.ExecuteAsync();
            native.Writes.Should().Be(0);
            await f.Main.EnableLogonStartupCommand.ExecuteAsync();
            await f.Main.ConfirmLogonStartupCommand.ExecuteAsync();
            preferences.Load().Should().BeTrue();
            native.Writes.Should().Be(1);
            await f.Main.ResetLogonStartupCommand.ExecuteAsync();
            preferences.Load().Should().BeTrue();
            native.Writes.Should().Be(1);
            await f.Main.ConfirmLogonStartupCommand.ExecuteAsync();
            preferences.Load().Should().BeNull();
            native.User.Should().BeNull();
            f.Main.HasVoiceConsent.Should().BeFalse();
            f.Main.IsVoiceEnabled.Should().BeFalse();
            f.Access.SetOpen(false);
            await f.Main.EnableLogonStartupCommand.ExecuteAsync();
            native.Writes.Should().Be(2);
        });
    }
}
