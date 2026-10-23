using AwesomeAssertions;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task Playback_settings_observe_saved_selection_without_replaying_output_and_stop_notifying_after_disposal()
    {
        var fixture = new Fixture(enablePlaybackVolume: true, enableWindowsSpeechRate: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.ViewModel.SelectedPlaybackVolume = 12;
        fixture.ViewModel.SelectedPlaybackVolume = 12;
        fixture.ViewModel.SelectedWindowsSpeechRate = -2;
        fixture.ViewModel.SavedPlaybackVolume.Should().Be(100);
        fixture.ViewModel.SavedWindowsSpeechRate.Should().Be(0);

        fixture.VolumePreferences.Value = new PlaybackVolume(35);
        fixture.RatePreferences.Value = new WindowsSpeechRate(4);
        fixture.VolumeConfiguration!.Observe();
        fixture.RateConfiguration!.Observe();

        fixture.ViewModel.SelectedPlaybackVolume.Should().Be(35);
        fixture.ViewModel.SelectedWindowsSpeechRate.Should().Be(4);
        fixture.ViewModel.SavedPlaybackVolume.Should().Be(35);
        fixture.ViewModel.SavedWindowsSpeechRate.Should().Be(4);
        fixture.ViewModel.PlaybackVolumeStatus.Should().Contain("Saved on this device");
        fixture.ViewModel.WindowsSpeechRateStatus.Should().Contain("Saved on this device");
        fixture.TextToSpeech.SpokenText.Should().BeNull();

        fixture.ViewModel.Dispose();
        var notifications = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        fixture.VolumeConfiguration.HoldUnavailable();
        fixture.RateConfiguration.HoldUnavailable();
        notifications.Should().BeEmpty();
        fixture.ViewModel.SelectedPlaybackVolume.Should().Be(35);
        fixture.ViewModel.SelectedWindowsSpeechRate.Should().Be(4);
    }
}
