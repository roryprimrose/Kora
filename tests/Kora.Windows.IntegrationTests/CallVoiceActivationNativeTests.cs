using Avalonia.Controls;
using Avalonia.Threading;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Application.Presentation;
using Kora.Core.Communication;
using Kora.NativeUxFixture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class CallVoiceActivationNativeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Calls_controls_complete_separate_exact_review_confirm_or_cancel_without_listening(bool cancel)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            await f.Main.SetAllowVoiceActivationDuringCallsAsync(false);
            var window = new SettingsWindow(f.Main);
            window.Show();
            window.ShowSection(SettingsSection.Calls);
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            try
            {
                async Task Click(string name)
                {
                    window.UpdateLayout();
                    var button = window.FindControl<Button>(name)!;
                    button.IsEffectivelyVisible.Should().BeTrue();
                    button.IsEffectivelyEnabled.Should().BeTrue();
                    await ((AsyncCommand)button.Command!).ExecuteAsync();
                }
                await Click("ReviewCallVoiceActivation");
                f.Main.CallVoiceActivationReviewStatus.Should().Contain("Off -> On");
                f.Main.AllowVoiceActivationDuringCalls.Should().BeFalse();
                await Click(cancel ? "CancelCallVoiceActivation" : "ConfirmCallVoiceActivation");
                var stored = new LocalCallAwarePreferences(f, NullLogger<LocalCallAwarePreferences>.Instance).Load();
                stored.Should().Be(new CallAwareSettings(true, !cancel));
                f.Main.AllowVoiceActivationDuringCalls.Should().Be(!cancel);
                f.Main.IsVoiceEnabled.Should().BeFalse();
                f.Main.HasVoiceConsent.Should().BeFalse();
                f.Main.IsManualCallActive.Should().BeFalse();
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("deactivate")]
    [InlineData("tab")]
    [InlineData("close")]
    public async Task Native_lifetime_changes_cannot_revive_a_review(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            await f.Main.SetAllowVoiceActivationDuringCallsAsync(false);
            var window = new SettingsWindow(f.Main);
            var other = new Window();
            window.Show();
            window.ShowSection(SettingsSection.Calls);
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            try
            {
                await f.Main.ReviewCallVoiceActivationCommand.ExecuteAsync();
                switch (change)
                {
                    case "hide": window.Hide(); window.Show(); break;
                    case "deactivate": other.Show(); other.Activate(); break;
                    case "tab": window.ShowSection(SettingsSection.Appearance); window.ShowSection(SettingsSection.Calls); break;
                    case "close": window.Close(); break;
                }
                if (!string.Equals(change, "close", StringComparison.Ordinal)) { window.Activate(); Dispatcher.UIThread.RunJobs(); }
                await FluentActions.Awaiting(() => f.Main.ConfirmCallVoiceActivationCommand.ExecuteAsync())
                    .Should().ThrowAsync<InvalidOperationException>();
                new LocalCallAwarePreferences(f, NullLogger<LocalCallAwarePreferences>.Instance).Load()
                    .Should().Be(new CallAwareSettings(true, false));
                f.Main.IsVoiceEnabled.Should().BeFalse();
            }
            finally { other.Close(); window.Close(); }
        });
    }
}
