using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Application.Presentation;
using Kora.Application.UnitTests.Configuration;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Configuration;
using Kora.Controls;
using Kora.NativeUxFixture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class PlaybackVolumeUiContractTests
{
    [Theory]
    [InlineData("hide")]
    [InlineData("close")]
    [InlineData("dispose")]
    public async Task Native_volume_surface_revokes_pending_input_and_fresh_reopen_requires_a_new_action(string transition)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            var store = new AudioControlTestStore();
            await using var admission = new AudioControlAdmission(store, store, new(store));
            var preferences = new LocalPlaybackVolumePreferences(f);
            preferences.Save(new(30));
            var audit = new VolumeAudit();
            await using var playback = new SettingInspectionWorkflowTests.PlaybackData();
            var volume = new PlaybackVolumeConfigurationService(preferences, playback, admission, audit);
            using var settings = new AudioPlaybackSettings(new AvaloniaUiDispatcher(),
                NullLogger<AudioPlaybackSettings>.Instance, volume);
            await f.InitializeAsync(null, audioPlaybackSettings: settings);
            using var controller = new SettingsWindowController(f.Main, NullLogger<SettingsWindowController>.Instance);
            f.Main.CanChangePlaybackVolume.Should().BeTrue();
            f.Main.CanChangePlaybackVolumeNative.Should().BeFalse();
            controller.Navigate(SettingsSection.SpeechAudio, static () => true);
            var window = SettingsWindow(controller);
            window.UpdateLayout();
            var field = window.GetVisualDescendants().OfType<SettingsNumberField>()
                .Single(item => string.Equals(item.Label, "Volume", StringComparison.Ordinal));
            field.IsEnabled.Should().BeTrue();
            field.ActionCommand.Should().BeSameAs(f.Main.SavePlaybackVolumeCommand);
            field.Value = 0;
            f.Main.SelectedPlaybackVolume.Should().Be(0);
            var before = f.Main.ResponseBody;
            var creation = new TaskCompletionSource<WorkSessionAuthorization>(TaskCreationOptions.RunContinuationsAsynchronously);
            store.PendingCreation = creation.Task;
            var pending = ((AsyncCommand)field.ActionCommand!).ExecuteAsync();
            try
            {
                pending.IsCompleted.Should().BeFalse();
                field.IsEnabled.Should().BeFalse();
                if (transition is "dispose") { controller.Dispose(); }
                else
                {
                    if (transition is "hide") { window.Hide(); }
                    else { window.Close(); }
                    f.Main.ShowSettings();
                    SettingsWindow(controller).IsVisible.Should().BeTrue();
                }
                f.Main.CanChangePlaybackVolumeNative.Should().BeFalse();
                preferences.Load().Should().Be(new PlaybackVolume(30));
                audit.Events.Should().BeEmpty();
            }
            finally { creation.TrySetResult(store.Authority!); }
            await pending;
            preferences.Load().Should().Be(new PlaybackVolume(30));
            audit.Events.Should().BeEmpty();
            f.Main.ResponseBody.Should().Be(before);
            store.Tasks.GroupBy(task => task.Request.TaskId).Select(tasks => tasks.Last())
                .Should().OnlyContain(task => task.IsTerminal);

            store.PendingCreation = null;
            controller.Dispose();
            using var replacement = new SettingsWindowController(f.Main, NullLogger<SettingsWindowController>.Instance);
            replacement.Navigate(SettingsSection.SpeechAudio, static () => true);
            await f.Main.RefreshPlaybackVolumeCommand.ExecuteAsync();
            f.Main.SavedPlaybackVolume.Should().Be(30);
            f.Main.SelectedPlaybackVolume = 0;
            await f.Main.SavePlaybackVolumeCommand.ExecuteAsync();
            new LocalPlaybackVolumePreferences(f).Load().Should().Be(new PlaybackVolume(0));
            f.Main.SavedPlaybackVolume.Should().Be(0);
            await f.Main.ResetPlaybackVolumeCommand.ExecuteAsync();
            new LocalPlaybackVolumePreferences(f).Load().Should().BeNull();
            f.Main.SavedPlaybackVolume.Should().Be(100);
            audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested,
                SecurityAuditOutcome.Succeeded, SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        });
    }

    private static SettingsWindow SettingsWindow(SettingsWindowController controller) =>
        (SettingsWindow)typeof(SettingsWindowController).GetField("window", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;

    private sealed class VolumeAudit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }

    [Fact]
    public void Native_volume_uses_a_bounded_unsaved_draft_and_explicit_admitted_save_reset_refresh_without_preview()
    {
        var root = SourceRoot();
        var controls = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml")).Descendants().ToArray();
        var selector = controls.Single(control =>
            string.Equals(control.Attribute("Value")?.Value, "{Binding SelectedPlaybackVolume}", StringComparison.Ordinal));
        selector.Name.LocalName.Should().Be("SettingsNumberField");
        selector.Attribute("Minimum")!.Value.Should().Be("0");
        selector.Attribute("Maximum")!.Value.Should().Be("{x:Static configuration:PlaybackVolume.MaximumPercent}");
        selector.Attribute("SavedValue")!.Value.Should().Be("{Binding SavedPlaybackVolume}");
        selector.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangePlaybackVolumeNative}");
        foreach (var command in new[] { "SavePlaybackVolumeCommand", "ResetPlaybackVolumeCommand", "RefreshPlaybackVolumeCommand" })
        {
            controls.Single(control => string.Equals((control.Attribute("Command") ?? control.Attribute("ActionCommand"))?.Value, "{Binding " + command + "}", StringComparison.Ordinal))
                .Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangePlaybackVolumeNative}");
        }
        controls.Any(control => string.Equals(control.Attribute("Text")?.Value, "{Binding PlaybackVolumeStatus}", StringComparison.Ordinal))
            .Should().BeTrue();
        var service = File.ReadAllText(Path.Combine(root, "src", "Kora.Windows", "Audio", "WindowsTextToSpeechService.cs"));
        service.Should().Contain("percent => synthesizer.Volume = percent").And.Contain("ApplyOwnedWindowsGain(volume)")
            .And.Contain("Pcm16PlaybackGain.Attenuate(audio.Samples, volume)").And.Contain("ValidatePlaybackSamples(audioReader)");
        Regex.IsMatch(service, @"waveOutSetVolume|(?:AudioEndpointVolume\.(?:Mute|MasterVolumeLevel\w*)\s*=)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Should().BeFalse();
    }

    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("The repository source root is unavailable.");
    }
}
