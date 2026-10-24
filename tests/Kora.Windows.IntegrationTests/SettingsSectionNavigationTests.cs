using System.Reflection;
using Avalonia.Controls;
using AwesomeAssertions;
using Kora.Application.Presentation;
using Kora.NativeUxFixture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SettingsSectionNavigationTests
{
    [Theory]
    [InlineData("open appearance settings", "AppearanceTab")]
    [InlineData("open speech settings", "SpeechAudioTab")]
    [InlineData("open audio settings", "SpeechAudioTab")]
    [InlineData("open microphone settings", "SpeechAudioTab")]
    [InlineData("open response settings", "ResponsesTab")]
    [InlineData("open session settings", "SessionsTab")]
    [InlineData("open call settings", "CallsTab")]
    public async Task CommandsSelectNamedNativeTabsAndReuseUnsavedSingletonState(string phrase, string anchor)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            var choose = 0;
            using var controller = new SettingsWindowController(f.Main,
                NullLogger<SettingsWindowController>.Instance, () => choose++);
            f.Main.CommandText = phrase;
            await f.Main.RunTypedCommand.ExecuteAsync();
            f.Main.Transcript.Should().StartWith("Settings section opened.");
            var window = Window(controller);
            window.IsVisible.Should().BeTrue();
            var tabs = window.FindControl<TabControl>("SettingsTabs")!;
            tabs.SelectedItem.Should().BeSameAs(window.FindControl<TabItem>(anchor));
            f.Main.AssistantNameInput = "Unsaved section draft";
            await f.Main.RunTypedCommand.ExecuteAsync();
            Window(controller).Should().BeSameAs(window);
            f.Main.AssistantNameInput.Should().Be("Unsaved section draft");
            f.Main.IsManualCallActive.Should().BeFalse();
            f.Main.IsVoiceEnabled.Should().BeFalse();
            choose.Should().Be(0);
            f.Main.ShowSettings();
            tabs.SelectedItem.Should().BeSameAs(window.FindControl<TabItem>(anchor));
            f.Main.ShowReadiness();
            tabs.SelectedItem.Should().BeSameAs(window.FindControl<TabItem>("ReadinessTab"));
            f.Main.RequestVoiceRecovery();
            tabs.SelectedItem.Should().BeSameAs(window.FindControl<TabItem>("SpeechAudioTab"));
        });
    }

    [Fact]
    public async Task NativeHideReshowDisposalAndInvalidSectionNeverReviveOrDefault()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            using var controller = new SettingsWindowController(f.Main, NullLogger<SettingsWindowController>.Instance);
            f.Main.ShowSettings();
            var window = Window(controller);
            controller.Navigate(SettingsSection.Calls, static () => true);
            var tabs = window.FindControl<TabControl>("SettingsTabs")!;
            var selected = tabs.SelectedItem;
            var lifetime = controller.CaptureLifetime();
            window.Hide();
            lifetime().Should().BeFalse();
            f.Main.ShowSettings();
            window.IsVisible.Should().BeTrue();
            lifetime().Should().BeFalse();
            Action invalid = () => controller.Navigate((SettingsSection)999, static () => true);
            invalid.Should().Throw<ArgumentOutOfRangeException>();
            Action invalidTab = () => window.ShowSection((SettingsSection)999);
            invalidTab.Should().Throw<ArgumentOutOfRangeException>();
            tabs.SelectedItem.Should().BeSameAs(selected);
            Action revoked = () => controller.Navigate(SettingsSection.Appearance, static () => false);
            revoked.Should().Throw<InvalidOperationException>();
            tabs.SelectedItem.Should().BeSameAs(selected);
            var lastLifetime = controller.CaptureLifetime();
            controller.Dispose();
            lastLifetime().Should().BeFalse();
            Action disposed = () => controller.Navigate(SettingsSection.Appearance, static () => true);
            disposed.Should().Throw<InvalidOperationException>();
            f.Main.CommandText = "open response settings";
            await f.Main.RunTypedCommand.ExecuteAsync();
            f.Main.Transcript.Should().Contain("not bound");
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task NativeAdmissionLossNeverReportsSuccessOrLeavesANewPrivateWindowVisible(int revokeAt)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            using var controller = new SettingsWindowController(f.Main, NullLogger<SettingsWindowController>.Instance);
            var checks = 0;
            Action navigate = () => controller.Navigate(SettingsSection.Calls, () => ++checks < revokeAt);
            navigate.Should().Throw<InvalidOperationException>();
            var opened = typeof(SettingsWindowController).GetField("window", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(controller) as SettingsWindow;
            (opened?.IsVisible ?? false).Should().BeFalse();
        });
    }

    [Fact]
    public async Task NativeDisposalDuringShowFailsExplicitlyWithoutAStaleWindowDereference()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            using var controller = new SettingsWindowController(f.Main, NullLogger<SettingsWindowController>.Instance);
            f.Main.ShowSettings();
            var window = Window(controller);
            window.Hide();
            window.PropertyChanged += (_, change) =>
            {
                if (change.Property == Avalonia.Visual.IsVisibleProperty && window.IsVisible)
                { controller.Dispose(); }
            };
            Action navigate = () => controller.Navigate(SettingsSection.Calls, static () => true);
            navigate.Should().Throw<InvalidOperationException>();
            window.IsVisible.Should().BeFalse();
        });
    }

    private static SettingsWindow Window(SettingsWindowController controller) =>
        (SettingsWindow)typeof(SettingsWindowController).GetField("window", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;
}
