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
    [InlineData(true)]
    [InlineData(false)]
    public async Task HostedSettingsRevalidatesVisibilityAfterAttachingInsideAHiddenContainer(bool reveal)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            var container = new Border { IsVisible = false };
            var owner = new Avalonia.Controls.Window { Content = container };
            using var controller = new SettingsWindowController(f.Main, NullLogger<SettingsWindowController>.Instance);
            SettingsView? shown = null;
            owner.Show();
            HeadlessSession.SetActivation(owner, true);
            try
            {
                controller.BindWorkbench(() => owner, view =>
                {
                    shown = view;
                    container.Child = view;
                    owner.UpdateLayout();
                    view.IsNativePresented.Should().BeFalse();
                    container.IsVisible = reveal;
                    owner.UpdateLayout();
                });
                Action navigate = () => controller.Navigate(SettingsSection.Appearance, static () => true);
                if (reveal)
                {
                    navigate.Should().NotThrow();
                    shown!.IsNativePresented.Should().BeTrue();
                    var presented = shown.CapturePresentation();
                    presented().Should().BeTrue();
                    owner.Hide();
                    presented().Should().BeFalse();
                }
                else
                {
                    navigate.Should().Throw<InvalidOperationException>();
                    shown!.IsNativePresented.Should().BeFalse();
                    owner.IsVisible.Should().BeFalse();
                }
                f.Main.IsVoiceEnabled.Should().BeFalse();
            }
            finally { owner.Close(); }
        });
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("readiness")]
    [InlineData("voice")]
    public async Task NativeSettingsRequestReportsPresentationFailureInsteadOfTerminatingTheApp(string route)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            var owner = new Avalonia.Controls.Window();
            using var controller = new SettingsWindowController(f.Main, NullLogger<SettingsWindowController>.Instance);
            controller.BindWorkbench(() => owner, _ => throw new InvalidOperationException("Native presenter unavailable."));
            try
            {
                Action request = route switch
                {
                    "readiness" => f.Main.ShowReadiness,
                    "voice" => f.Main.RequestVoiceRecovery,
                    _ => f.Main.ShowSettings,
                };
                request.Should().NotThrow();
                f.Main.ResponseBody.Should().Contain("Settings could not be opened");
                owner.IsVisible.Should().BeFalse();
                f.Main.IsVoiceEnabled.Should().BeFalse();
            }
            finally { owner.Close(); }
        });
    }

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
            var tabs = window.FindControl<Kora.Controls.SettingsBrowser>("SettingsTabs")!;
            tabs.SelectedItem.Should().BeSameAs(window.FindControl<Kora.Controls.SettingsPage>(anchor));
            f.Main.AssistantNameInput = "Unsaved section draft";
            await f.Main.RunTypedCommand.ExecuteAsync();
            Window(controller).Should().BeSameAs(window);
            f.Main.AssistantNameInput.Should().Be("Unsaved section draft");
            f.Main.IsManualCallActive.Should().BeFalse();
            f.Main.IsVoiceEnabled.Should().BeFalse();
            choose.Should().Be(0);
            f.Main.ShowSettings();
            tabs.SelectedItem.Should().BeSameAs(window.FindControl<Kora.Controls.SettingsPage>(anchor));
            f.Main.ShowReadiness();
            tabs.SelectedItem.Should().BeSameAs(window.FindControl<Kora.Controls.SettingsPage>("ReadinessTab"));
            f.Main.RequestVoiceRecovery();
            tabs.SelectedItem.Should().BeSameAs(window.FindControl<Kora.Controls.SettingsPage>("SpeechAudioTab"));
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
            var tabs = window.FindControl<Kora.Controls.SettingsBrowser>("SettingsTabs")!;
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
