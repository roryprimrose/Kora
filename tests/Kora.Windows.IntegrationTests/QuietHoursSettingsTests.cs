using System.Reflection;
using Avalonia.Controls;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Voice;
using Kora.Core.Configuration;
using Kora.Controls;
using Kora.NativeUxFixture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class QuietHoursSettingsTests
{
    [Fact]
    public async Task NativeFieldsReviewSeparateConfirmReloadAndResetUseTheActualAdmittedSqliteWorkflow()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Environment.CurrentDirectory);
            await using var admission = new AudioControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var preferences = new LocalQuietHoursPreferences(f);
            var configuration = new QuietHoursConfigurationService(preferences, admission, f.Audit, f.Time,
                NullLogger<QuietHoursConfigurationService>.Instance);
            await f.InitializeAsync(null, quietHoursConfiguration: configuration);
            using var controller = new SettingsWindowController(f.Main, NullLogger<SettingsWindowController>.Instance);
            f.Main.ShowSettings();
            var window = (SettingsWindow)typeof(SettingsWindowController).GetField("window", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;
            window.FindControl<TextBox>("QuietHoursDays")!.Text = "Mon,Fri";
            window.FindControl<TextBox>("QuietHoursStart")!.Text = "22:00";
            window.FindControl<TextBox>("QuietHoursEnd")!.Text = "06:00";
            window.FindControl<TextBox>("QuietHoursZone")!.Text = "UTC";
            window.FindControl<Button>("ReviewQuietHours")!.Command.Should().BeSameAs(f.Main.ReviewQuietHoursCommand);
            window.FindControl<Button>("ConfirmQuietHours")!.Command.Should().BeSameAs(f.Main.ConfirmQuietHoursCommand);
            window.FindControl<SettingActionsButton>("QuietHoursActions")!.ResetCommand.Should().BeSameAs(f.Main.ResetQuietHoursCommand);
            await f.Main.ReviewQuietHoursCommand.ExecuteAsync();
            preferences.Load().Should().BeNull();
            f.Main.QuietHoursReviewText.Should().Contain("Mon,Fri 22:00 06:00 UTC");
            window.Hide();
            f.Main.ShowSettings();
            await f.Main.ConfirmQuietHoursCommand.ExecuteAsync();
            preferences.Load().Should().BeNull();
            await f.Main.ReviewQuietHoursCommand.ExecuteAsync();
            await f.Main.ConfirmQuietHoursCommand.ExecuteAsync();
            var schedule = QuietHoursSchedule.Weekly("Mon,Fri", "22:00", "06:00", "UTC");
            new LocalQuietHoursPreferences(f).Load().Should().Be(schedule);
            configuration.Get().Effective.Should().Be(schedule);
            await f.Main.ResetQuietHoursCommand.ExecuteAsync();
            preferences.Load().Should().Be(schedule);
            await f.Main.ConfirmQuietHoursCommand.ExecuteAsync();
            preferences.Load().Should().BeNull();
            configuration.Get().Effective.Should().Be(QuietHoursSchedule.Off);
            f.Main.HasVoiceConsent.Should().BeFalse();
            f.Main.IsVoiceEnabled.Should().BeFalse();
            f.Access.SetOpen(false);
            await f.Main.ReviewQuietHoursCommand.ExecuteAsync();
            preferences.Load().Should().BeNull();
        });
    }

    [Fact]
    public async Task UnavailableNativeControlsCannotOfferAWorkingSchedule()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Environment.CurrentDirectory);
            await f.InitializeAsync();
            var window = new SettingsWindow(f.Main);
            try
            {
                window.Show();
                window.UpdateLayout();
                window.FindControl<Button>("ReviewQuietHours")!.IsEffectivelyEnabled.Should().BeFalse();
                window.FindControl<Button>("ConfirmQuietHours")!.IsEffectivelyEnabled.Should().BeFalse();
                window.FindControl<TextBox>("QuietHoursZone")!.IsEffectivelyEnabled.Should().BeFalse();
            }
            finally { window.Close(); }
        });
    }
}
