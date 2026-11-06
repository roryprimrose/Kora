using System.Reflection;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Application.Voice;
using Kora.Core.Voice;
using Kora.NativeUxFixture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class VoiceCaptureTimingUiTests
{
    [Fact]
    public async Task NativeTimingControlsReviewCancelConfirmAndReloadWithoutOpeningAudio()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var source = new DirectoryInfo(AppContext.BaseDirectory);
            while (source is not null && !File.Exists(Path.Combine(source.FullName, "Kora.slnx"))) { source = source.Parent; }
            var root = Path.Combine(source?.FullName ?? throw new InvalidOperationException("Owning source root unavailable."),
                ".scratch", "orchestration", "capture-timing");
            Directory.CreateDirectory(root);
            using var fixture = new NativeUxFixtureSession(root, syntheticVoice: true);
            await using var admission = new AudioControlAdmission(fixture.Interactions, fixture.Interactions, new(fixture.Tasks));
            var preferences = new LocalVoiceCaptureTimingPreferences(fixture);
            var timing = new VoiceCaptureTimingConfigurationService(preferences, admission, fixture.Audit,
                NullLogger<VoiceCaptureTimingConfigurationService>.Instance);
            await fixture.InitializeAsync(null, voiceCaptureTimingConfiguration: timing);
            using var controller = new SettingsWindowController(fixture.Main, NullLogger<SettingsWindowController>.Instance);
            fixture.Main.ShowSettings();
            // Read only the controller's existing window handle; exercise its real lifetime and bindings.
            var window = (SettingsWindow)typeof(SettingsWindowController).GetField("window",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;
            try
            {
                window.ShowVoiceRecovery();
                window.UpdateLayout();
                var descendants = window.GetVisualDescendants().ToArray();
                var editor = descendants.OfType<TextBox>().Single(control =>
                    string.Equals(ControlAutomationPeer.CreatePeerForElement(control)!.GetName(), "Capture timing in seconds", StringComparison.Ordinal));
                var selector = descendants.OfType<ComboBox>().Single(control =>
                    string.Equals(ControlAutomationPeer.CreatePeerForElement(control)!.GetName(), "Capture timing option", StringComparison.Ordinal));
                selector.SelectedItem = VoiceCaptureTiming.TrailingSilenceId;
                editor.Text = "2";
                fixture.Main.SelectedVoiceTimingOption.Should().Be(VoiceCaptureTiming.TrailingSilenceId);
                fixture.Main.VoiceTimingSeconds.Should().Be("2");
                var buttons = descendants.OfType<Button>().ToArray();
                Button Find(string name) => buttons.Single(button => string.Equals(button.Content as string, name, StringComparison.Ordinal));
                Find("Review capture timing").IsEnabled.Should().BeTrue();
                await ((AsyncCommand)Find("Review capture timing").Command!).ExecuteAsync();
                fixture.Main.HasVoiceTimingReview.Should().BeTrue();
                preferences.Load().Should().BeNull();
                await ((AsyncCommand)Find("Cancel capture timing").Command!).ExecuteAsync();
                fixture.Main.HasVoiceTimingReview.Should().BeFalse();
                await ((AsyncCommand)Find("Review capture timing").Command!).ExecuteAsync();
                fixture.Main.CanConfirmVoiceTiming.Should().BeTrue(fixture.Main.Transcript);
                await ((AsyncCommand)Find("Confirm capture timing").Command!).ExecuteAsync();
                Dispatcher.UIThread.RunJobs();
                fixture.Main.ResponseTitle.Should().Be("Capture timing saved.", fixture.Main.ResponseBody);
                preferences.Load().Should().Be(new VoiceCaptureTiming(5, 15, 2));
                new LocalVoiceCaptureTimingPreferences(fixture).Load().Should().Be(preferences.Load());
                fixture.Voice.StartCalls.Should().Be(0);
                fixture.Main.HasVoiceConsent.Should().BeFalse();
            }
            finally { window.Close(); }
        });
    }
}
