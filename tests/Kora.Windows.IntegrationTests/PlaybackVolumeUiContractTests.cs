using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class PlaybackVolumeUiContractTests
{
    [Fact]
    public void Native_volume_uses_a_bounded_unsaved_draft_and_explicit_admitted_save_reset_refresh_without_preview()
    {
        var root = SourceRoot();
        var controls = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml")).Descendants().ToArray();
        var selector = controls.Single(control =>
            string.Equals(control.Attribute("SelectedItem")?.Value, "{Binding SelectedPlaybackVolume}", StringComparison.Ordinal));
        selector.Attribute("ItemsSource")!.Value.Should().Be("{Binding PlaybackVolumeChoices}");
        selector.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangePlaybackVolume}");
        foreach (var command in new[] { "SavePlaybackVolumeCommand", "ResetPlaybackVolumeCommand", "RefreshPlaybackVolumeCommand" })
        {
            controls.Single(control => string.Equals(control.Attribute("Command")?.Value, "{Binding " + command + "}", StringComparison.Ordinal))
                .Attribute("IsEnabled")!.Value.Should().Be("{Binding CanChangePlaybackVolume}");
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
