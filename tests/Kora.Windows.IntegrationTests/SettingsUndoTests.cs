using Avalonia.Controls;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Core.Configuration;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SettingsUndoTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeUndoSeparatelyReviewsConfirmsOrCancelsAnExactDefaultRestoration(bool cancel)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            var window = new SettingsWindow(f.Main);
            window.Show();
            try
            {
                var preferences = new LocalAppearancePreferences(f, NullLogger<LocalAppearancePreferences>.Instance);
                f.Main.ThemeMode = ApplicationThemeMode.Dark;
                await f.Main.AppearanceUpdate;
                preferences.LoadThemeMode().Should().Be(ApplicationThemeMode.Dark);
                async Task Click(string name)
                {
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    var button = window.FindControl<Button>(name)!;
                    button.IsEffectivelyEnabled.Should().BeTrue();
                    await ((AsyncCommand)button.Command!).ExecuteAsync();
                }
                await Click("ReviewSettingsUndo");
                f.Main.CanConfirmSettingsUndo.Should().BeTrue();
                f.Main.SettingsUndoDescription.Should().Contain("default").And.Contain("shared device-local");
                preferences.LoadThemeMode().Should().Be(ApplicationThemeMode.Dark);
                await Click(cancel ? "CancelSettingsUndo" : "ConfirmSettingsUndo");
                preferences.LoadThemeMode().Should().Be(cancel ? ApplicationThemeMode.Dark : null, f.Main.SettingsUndoDescription);
                f.Main.CanConfirmSettingsUndo.Should().BeFalse();
                f.Main.IsVoiceEnabled.Should().BeFalse();
                f.Main.HasVoiceConsent.Should().BeFalse();
                f.Main.IsManualCallActive.Should().BeFalse();
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task HidingAndReshowingNativeSettingsCannotReviveUndoReview()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            var window = new SettingsWindow(f.Main);
            window.Show();
            try
            {
                f.Main.ThemeMode = ApplicationThemeMode.Dark;
                await f.Main.AppearanceUpdate;
                await ((AsyncCommand)window.FindControl<Button>("ReviewSettingsUndo")!.Command!).ExecuteAsync();
                window.Hide();
                window.Show();
                await ((AsyncCommand)window.FindControl<Button>("ConfirmSettingsUndo")!.Command!).ExecuteAsync();
                new LocalAppearancePreferences(f, NullLogger<LocalAppearancePreferences>.Instance)
                    .LoadThemeMode().Should().Be(ApplicationThemeMode.Dark);
                f.Main.SettingsUndoDescription.Should().Contain("not confirmed");
                f.Main.CanConfirmSettingsUndo.Should().BeFalse();
            }
            finally { window.Close(); }
        });
    }
}
