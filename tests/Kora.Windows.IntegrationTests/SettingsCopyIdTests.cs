using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Voice;
using Kora.NativeUxFixture;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SettingsCopyIdTests
{
    [Theory]
    [InlineData("Reduced motion", "appearance.reduced-motion")]
    [InlineData("Show presence", "appearance.presence.show")]
    [InlineData("Spoken summary limits", "speech.summary-words")]
    [InlineData("Spoken summary limits", "speech.summary-sentences")]
    [InlineData("Custom content sources", "customizations.skills.sources")]
    [InlineData("Default service and model", "providers.app.model")]
    public async Task CogCopiesOneExactIdAndPastingItShowsOnlyThatSetting(string settingName, string id)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            var window = new SettingsWindow(f.Main);
            try
            {
                window.Show();
                var browser = window.FindControl<SettingsBrowser>("SettingsTabs")!;
                browser.ShowFilter(id);
                var cog = window.GetLogicalDescendants().OfType<SettingActionsButton>()
                    .Single(button => string.Equals(button.SettingName, settingName, StringComparison.Ordinal));
                var copy = ((MenuFlyout)cog.Flyout!).Items.OfType<MenuItem>().Single(item => item.Header is "Copy Setting ID");
                ControlAutomationPeer.CreatePeerForElement(copy)!.GetName().Should().Contain("Copy Setting ID");
                var item = copy.Command is not null ? copy
                    : copy.Items.OfType<MenuItem>().Single(option => string.Equals(option.Header as string, id, StringComparison.Ordinal));
                var clipboard = window.Clipboard!;
                await clipboard.SetTextAsync("previous synthetic clipboard text");
                var theme = f.Main.ThemeMode;
                var consent = f.Main.HasVoiceConsent;
                await ((AsyncCommand)item.Command!).ExecuteAsync();
                var copied = await clipboard.TryGetTextAsync();
                copied.Should().Be(id);
                f.Main.ThemeMode.Should().Be(theme);
                f.Main.HasVoiceConsent.Should().Be(consent);
                browser.SelectedItem = window.FindControl<SettingsPage>("CallsTab");
                browser.Filter = "  " + copied!.ToUpperInvariant() + "  ";
                Dispatcher.UIThread.RunJobs();
                browser.SelectedItem.Should().BeNull();
                browser.MatchCount.Should().Be(1);
                browser.IsSettingVisible(id).Should().BeTrue();

                window.Hide();
                await FluentActions.Awaiting(() => ((AsyncCommand)item.Command!).ExecuteAsync())
                    .Should().ThrowAsync<InvalidOperationException>();
                (await clipboard.TryGetTextAsync()).Should().Be(id);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task AllIndividualAndCollectionIdsAreUniqueAndResolveToOneResult()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            var window = new SettingsWindow(f.Main);
            try
            {
                window.Show();
                var browser = window.FindControl<SettingsBrowser>("SettingsTabs")!;
                var ids = browser.SettingIds.ToArray();
                ids.Should().OnlyHaveUniqueItems();
                ids.Should().Contain(
                [
                    "appearance.presence.show", "speech.input.profile", "speech.input.resource", "speech.input.residency",
                    SpeechTextCommand.OptionId, SpeechTextCommand.PlacementId, SpeechTextCommand.DelayId,
                    SpeechTextCommand.WordEmphasisId, SpeechTextCommand.PinId, "display.speech-text-screen",
                    "sessions.archive-after-days", "sessions.delete-after-days",
                    ProviderModeCommand.OptionId, ProviderProfileCommand.ServiceOption, ProviderProfileCommand.ProfileOption,
                    "customizations.instructions.sources", "customizations.prompts.sources", "customizations.skills.sources",
                    "customizations.agents.sources", "customizations.mcp.sources", "customizations.mcp.included-servers",
                    "network.uri-rules", "knowledge.sources", "approvals.session-actions", "approvals.always-actions",
                ]);
                foreach (var id in ids)
                {
                    window.ShowFilter(id);
                    browser.MatchCount.Should().Be(1, id + " must resolve to one setting result");
                    browser.IsSettingVisible(id).Should().BeTrue();
                }
                window.ShowFilter("appearance.presence-display");
                browser.MatchCount.Should().Be(1);
                browser.IsSettingVisible("appearance.presence.show").Should().BeTrue();
                foreach (var cog in window.GetLogicalDescendants().OfType<SettingActionsButton>())
                {
                    var copy = ((MenuFlyout)cog.Flyout!).Items.OfType<MenuItem>()
                        .SingleOrDefault(item => item.Header is "Copy Setting ID");
                    if (cog.SettingName is "Speech and audio" or "Readiness") { copy.Should().BeNull(); }
                    else { copy.Should().NotBeNull(cog.SettingName + " must expose its exact setting IDs"); }
                }
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task ExactGroupedOptionNavigationSelectsItsEditorWithoutSaving()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            var window = new SettingsWindow(f.Main);
            try
            {
                window.Show();
                var browser = window.FindControl<SettingsBrowser>("SettingsTabs")!;
                browser.Filter = VoiceCaptureTiming.TrailingSilenceId;
                Dispatcher.UIThread.RunJobs();
                f.Main.SelectedVoiceTimingOption.Should().Be(VoiceCaptureTiming.TrailingSilenceId);
                browser.MatchCount.Should().Be(1);
                window.ShowFilter(SpeechTextCommand.DelayId);
                f.Main.SelectedSpeechCaptionOption.Should().Be(SpeechCaptionOption.DismissalDelay);
                browser.MatchCount.Should().Be(1);
                window.ShowFilter(SpeechTextCommand.WordEmphasisId);
                f.Main.SelectedSpeechCaptionOption.Should().Be(SpeechCaptionOption.WordEmphasis);
                f.Main.IsVoiceEnabled.Should().BeFalse();
                f.Main.HasVoiceConsent.Should().BeFalse();
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task FailedCopyReportsTheFailureWithoutResetOrAutomaticRetry()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var resets = 0;
            var copies = 0;
            var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reset = new AsyncCommand(() => { resets++; return Task.CompletedTask; }, exception => failure.TrySetException(exception));
            var cog = new SettingActionsButton { SettingName = "Test setting", ResetCommand = reset };
            cog.BindSettingIdCopy(["test.setting"], Copy, exception => failure.TrySetResult(exception));
            cog.BindSettingIdCopy(["test.setting"], Copy, exception => failure.TrySetResult(exception));
            var menu = (MenuFlyout)cog.Flyout!;
            var copy = menu.Items.OfType<MenuItem>().Single(item => item.Header is "Copy Setting ID");
            menu.Items.OfType<MenuItem>().Single(item => item.Header is "Reset setting").Command.Should().BeSameAs(reset);
            copy.Command!.Execute(null);
            (await failure.Task).Should().BeOfType<IOException>();
            copies.Should().Be(1);
            resets.Should().Be(0);

            Task Copy(string id)
            {
                id.Should().Be("test.setting");
                copies++;
                return Task.FromException(new IOException("Deterministic clipboard write failure."));
            }
        });
    }
}
