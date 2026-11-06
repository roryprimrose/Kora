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
    public async Task NativeSettingsCompletesOrCancelsTheExactActivatedVoiceUndoReview(bool cancel)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new NativeUxFixtureSession(Path.GetTempPath(), syntheticVoice: true);
            await f.InitializeAsync();
            var window = new SettingsWindow(f.Main);
            window.Show();
            try
            {
                f.Main.CommandText = "set speech.summary-words to 40";
                await f.Main.RunTypedCommand.ExecuteAsync();
                var preferences = new LocalTextToSpeechPreferences(f, NullLogger<LocalTextToSpeechPreferences>.Instance);
                preferences.LoadSummaryLimits().Should().Be(new SpokenSummaryLimits(3, 40));
                f.Main.BindVoiceOwnershipGate(static () => true);
                await f.Main.SetVoiceConsentAsync(true);
                var reviewed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
                {
                    if (string.Equals(args.PropertyName, nameof(f.Main.CanConfirmSettingsUndo), StringComparison.Ordinal)
                        && f.Main.CanConfirmSettingsUndo)
                    { reviewed.TrySetResult(); }
                }
                f.Main.PropertyChanged += Changed;
                try
                {
                    await f.Main.BeginPushToTalkCommand.ExecuteAsync();
                    await f.Main.EndPushToTalkCommand.ExecuteAsync();
                    f.Voice.PublishTranscript(f.Main.AssistantName + ", undo the last settings change");
                    await reviewed.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
                }
                finally { f.Main.PropertyChanged -= Changed; }
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var confirm = window.FindControl<Button>("ConfirmSettingsUndo")!;
                confirm.IsEffectivelyEnabled.Should().BeTrue();
                confirm.IsEffectivelyVisible.Should().BeTrue();
                f.Main.SettingsUndoDescription.Should().Contain("40 words").And.Contain("restore");
                preferences.LoadSummaryLimits().Should().Be(new SpokenSummaryLimits(3, 40));
                var consent = f.Main.HasVoiceConsent;
                var captures = f.Voice.StartCalls;

                await ((AsyncCommand)window.FindControl<Button>(cancel ? "CancelSettingsUndo" : "ConfirmSettingsUndo")!.Command!)
                    .ExecuteAsync();

                preferences.LoadSummaryLimits().Should().Be(cancel ? new SpokenSummaryLimits(3, 40) : null,
                    f.Main.SettingsUndoDescription);
                f.Main.SettingsUndoDescription.Should().Contain(cancel ? "cancelled" : "new audited write");
                if (!cancel) { f.Main.SettingsUndoDescription.Should().Contain("restored").And.Contain("default"); }
                f.Main.CanConfirmSettingsUndo.Should().BeFalse();
                f.Main.HasVoiceConsent.Should().Be(consent);
                f.Voice.StartCalls.Should().Be(captures);
            }
            finally { window.Close(); }
        });
    }

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
                void AssertUndoActions(bool canReview, bool canConfirm)
                {
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    window.FindControl<Button>("ReviewSettingsUndo")!.IsEffectivelyEnabled.Should().Be(canReview);
                    window.FindControl<Button>("ConfirmSettingsUndo")!.IsEffectivelyEnabled.Should().Be(canConfirm);
                    window.FindControl<Button>("CancelSettingsUndo")!.IsEffectivelyEnabled.Should().Be(canConfirm);
                }

                AssertUndoActions(canReview: false, canConfirm: false);
                var preferences = new LocalAppearancePreferences(f, NullLogger<LocalAppearancePreferences>.Instance);
                f.Main.ThemeMode = ApplicationThemeMode.Dark;
                await f.Main.AppearanceUpdate;
                preferences.LoadThemeMode().Should().Be(ApplicationThemeMode.Dark);
                AssertUndoActions(canReview: true, canConfirm: false);
                async Task Click(string name)
                {
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    var button = window.FindControl<Button>(name)!;
                    button.IsEffectivelyEnabled.Should().BeTrue();
                    button.IsEffectivelyVisible.Should().BeTrue();
                    await ((AsyncCommand)button.Command!).ExecuteAsync();
                }
                await Click("ReviewSettingsUndo");
                f.Main.CanConfirmSettingsUndo.Should().BeTrue();
                AssertUndoActions(canReview: true, canConfirm: true);
                f.Main.SettingsUndoDescription.Should().Contain("default").And.Contain("shared device-local");
                preferences.LoadThemeMode().Should().Be(ApplicationThemeMode.Dark);
                await Click(cancel ? "CancelSettingsUndo" : "ConfirmSettingsUndo");
                preferences.LoadThemeMode().Should().Be(cancel ? ApplicationThemeMode.Dark : null, f.Main.SettingsUndoDescription);
                f.Main.CanConfirmSettingsUndo.Should().BeFalse();
                AssertUndoActions(canReview: cancel, canConfirm: false);
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
