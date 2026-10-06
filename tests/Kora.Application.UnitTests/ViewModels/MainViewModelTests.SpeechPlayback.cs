using AwesomeAssertions;

using Kora.Application.ViewModels;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public void Presence_input_failure_surfaces_visual_recovery()
    {
        var fixture = new Fixture();

        fixture.ViewModel.ReportPresenceInputFailure("Windows rejected the input style.");

        fixture.ViewModel.ResponseTitle.Should().Be("Presence mouse routing is unavailable.");
        fixture.ViewModel.ResponseBody.Should().Contain("hidden to avoid blocking other windows");
        fixture.ViewModel.ResponseBody.Should().Contain("Restart Kora");
        fixture.ViewModel.ResponseBody.Should().Contain("Windows rejected the input style.");
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
    }

    [Fact]
    public async Task Presence_tracks_actual_playback_not_synthesis_and_resets_after_speech()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var changes = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        var preview = fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        await fixture.TextToSpeech.SpeakStarted.Task;
        fixture.ViewModel.IsSpeaking.Should().BeTrue();
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.ViewModel.IsSpeechPlaybackActive.Should().BeFalse();
        fixture.ViewModel.SpeechOutputLevel.Should().Be(0);

        fixture.TextToSpeech.PlaybackFrame = new SpeechPlaybackFrame(true, 0.75);
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.ViewModel.IsSpeechPlaybackActive.Should().BeTrue();
        fixture.ViewModel.SpeechOutputLevel.Should().Be(0.75);
        changes.Should().Contain(nameof(MainViewModel.IsSpeechPlaybackActive));
        changes.Should().Contain(nameof(MainViewModel.SpeechOutputLevel));

        changes.Clear();
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        changes.Should().BeEmpty();

        fixture.TextToSpeech.PlaybackFrame = new SpeechPlaybackFrame(true, 0);
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.ViewModel.IsSpeechPlaybackActive.Should().BeTrue();
        fixture.ViewModel.SpeechOutputLevel.Should().Be(0);

        fixture.TextToSpeech.PlaybackFrame = new SpeechPlaybackFrame(true, 1);
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.TextToSpeech.SpeakGate.SetResult();
        await preview;
        fixture.ViewModel.IsSpeechPlaybackActive.Should().BeFalse();
        fixture.ViewModel.SpeechOutputLevel.Should().Be(0);
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.ViewModel.IsSpeechPlaybackActive.Should().BeFalse();
    }

    [Fact]
    public async Task Silent_playback_transitions_notify_activity_without_notifying_an_unchanged_level()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var preview = fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        await fixture.TextToSpeech.SpeakStarted.Task;
        var changes = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        fixture.TextToSpeech.PlaybackFrame = new SpeechPlaybackFrame(true, 0);
        fixture.ViewModel.RefreshSpeechPlaybackFrame();

        fixture.ViewModel.IsSpeechPlaybackActive.Should().BeTrue();
        fixture.ViewModel.SpeechOutputLevel.Should().Be(0);
        changes.Should().Equal(nameof(MainViewModel.IsSpeechPlaybackActive));

        changes.Clear();
        fixture.TextToSpeech.PlaybackFrame = SpeechPlaybackFrame.Inactive;
        fixture.ViewModel.RefreshSpeechPlaybackFrame();

        fixture.ViewModel.IsSpeechPlaybackActive.Should().BeFalse();
        fixture.ViewModel.SpeechOutputLevel.Should().Be(0);
        changes.Should().Equal(nameof(MainViewModel.IsSpeechPlaybackActive));

        fixture.TextToSpeech.SpeakGate.SetResult();
        await preview;
    }

    [Fact]
    public async Task Stopping_speech_clears_presence_even_if_the_last_sample_was_loud()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var preview = fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        await fixture.TextToSpeech.SpeakStarted.Task;
        fixture.TextToSpeech.PlaybackFrame = new SpeechPlaybackFrame(true, 1);
        fixture.ViewModel.RefreshSpeechPlaybackFrame();

        await fixture.ViewModel.StopSpeechCommand.ExecuteAsync();
        await preview;

        fixture.ViewModel.IsSpeechPlaybackActive.Should().BeFalse();
        fixture.ViewModel.SpeechOutputLevel.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Presence_does_not_expose_output_when_the_host_is_locked_or_disposed(bool dispose)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var preview = fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        await fixture.TextToSpeech.SpeakStarted.Task;
        fixture.TextToSpeech.PlaybackFrame = new SpeechPlaybackFrame(true, 1);
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.ViewModel.IsSpeechPlaybackActive.Should().BeTrue();

        if (dispose)
        {
            fixture.ViewModel.Dispose();
        }
        else
        {
            fixture.Session.IsUnlocked = false;
        }
        fixture.ViewModel.RefreshSpeechPlaybackFrame();

        fixture.ViewModel.IsSpeechPlaybackActive.Should().BeFalse();
        fixture.ViewModel.SpeechOutputLevel.Should().Be(0);
        fixture.TextToSpeech.SpeakGate.SetResult();
        await preview;
    }

    [Fact]
    public async Task Playback_position_failure_invalidates_output_and_surfaces_recovery()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var preview = fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        await fixture.TextToSpeech.SpeakStarted.Task;
        fixture.TextToSpeech.PlaybackFrame = new SpeechPlaybackFrame(true, 1);
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.TextToSpeech.PlaybackFrameException = new AudioOutputDeviceUnavailableException(
            AudioOutputFailureReason.PlaybackFailed, "Playback position failed.");

        fixture.ViewModel.RefreshSpeechPlaybackFrame();

        fixture.Events.Should().Contain("speech.invalidate");
        fixture.ViewModel.IsSpeechPlaybackActive.Should().BeFalse();
        fixture.ViewModel.SpeechOutputLevel.Should().Be(0);
        fixture.ViewModel.ResponseTitle.Should().Be("Speech playback is unavailable.");
        fixture.ViewModel.ResponseBody.Should().Contain("Playback position failed.");
        fixture.TextToSpeech.SpeakGate.SetResult();
        await preview;
    }
}
