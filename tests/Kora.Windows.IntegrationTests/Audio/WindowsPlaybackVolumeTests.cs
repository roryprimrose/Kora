using AwesomeAssertions;
using Kora.Core.Configuration;
using Kora.Core.Voice;
using Kora.Windows.Audio;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed class WindowsPlaybackVolumeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    public async Task Owned_cancellation_callbacks_can_reenter_state_after_atomic_volume_retirement(int percent)
    {
        WindowsTextToSpeechService? service = null;
        var cancellations = 0;
        service = new WindowsTextToSpeechService(null,
            NullLogger<WindowsTextToSpeechService>.Instance, setOwnedWindowsGain: null,
            cancelOwnedSynthesis: () =>
            {
                cancellations++;
                Task.Run(() => service!.OnPlaybackStopped(new object(), new()),
                    TestContext.Current.CancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken)
                    .GetAwaiter().GetResult();
            });
        await using (service)
        {
            service.SetPlaybackVolume(null);
            service.SetPlaybackVolume(new(percent));
            service.InvalidateOutput();
            cancellations.Should().Be(3);
            service.IsSpeaking.Should().BeFalse();
            service.PlaybackFrame.Should().Be(SpeechPlaybackFrame.Inactive);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    public async Task Windows_engine_gain_uses_the_bounded_owned_instance_setter_without_synthesis(int percent)
    {
        var applied = new List<int>();
        await using var service = new WindowsTextToSpeechService(null,
            NullLogger<WindowsTextToSpeechService>.Instance, applied.Add);
        service.ApplyOwnedWindowsGain(new(percent));
        applied.Should().Equal(percent);
        service.IsSpeaking.Should().BeFalse();
        service.PlaybackFrame.Should().Be(SpeechPlaybackFrame.Inactive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    public async Task Owned_gain_changes_retire_inflight_and_queued_generations_without_opening_devices_or_synthesizing(int percent)
    {
        await using var service = new WindowsTextToSpeechService(NullLogger<WindowsTextToSpeechService>.Instance);
        service.SetPlaybackVolume(null);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        var active = service.RunCurrentOutputAsync(async (_, _) =>
        {
            entered++;
            started.SetResult();
            await finish.Task;
        }, TestContext.Current.CancellationToken);
        await started.Task;
        var queued = service.RunCurrentOutputAsync((_, _) => { entered++; return Task.CompletedTask; }, TestContext.Current.CancellationToken);
        service.SetPlaybackVolume(new(percent));
        service.SetPlaybackVolume(new(percent));
        finish.SetResult();
        await active.Invoking(async task => await task).Should().ThrowAsync<OperationCanceledException>();
        await queued.Invoking(async task => await task).Should().ThrowAsync<OperationCanceledException>();
        entered.Should().Be(1);
        service.SetPlaybackVolume(PlaybackVolume.Default);
        entered.Should().Be(1);
        service.IsSpeaking.Should().BeFalse();
        service.PlaybackFrame.Should().Be(SpeechPlaybackFrame.Inactive);
        service.OnPlaybackStopped(new object(), new(new IOException("retired callback")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Zero_and_unavailable_gain_refuse_before_provider_or_device_access(bool unavailable)
    {
        var applied = new List<int>();
        var service = new WindowsTextToSpeechService(null, NullLogger<WindowsTextToSpeechService>.Instance, applied.Add);
        service.SetPlaybackVolume(unavailable ? null : new PlaybackVolume(0));
        var speech = () => service.SpeakAsync("Never synthesize this.", new("not-installed", "Not installed", "en-US", SpeechVoiceGender.Unknown),
            new("not-a-device", "Not a device"), TestContext.Current.CancellationToken);
        await speech.Should().ThrowAsync<PlaybackVolumeUnavailableException>();
        applied.Should().BeEmpty();
        service.IsSpeaking.Should().BeFalse();
        await service.DisposeAsync();
        service.Invoking(item => item.SetPlaybackVolume(new(1))).Should().Throw<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void Native_sink_rejects_empty_or_partial_frames_without_opening_audio(int length, bool valid)
    {
        using var stream = new MemoryStream(new byte[length]);
        using var reader = new RawSourceWaveStream(stream, new WaveFormat(24000, 16, 1));
        var validation = () => WindowsTextToSpeechService.ValidatePlaybackSamples(reader);
        if (valid) { validation.Should().NotThrow(); }
        else { validation.Should().Throw<InvalidDataException>(); }
    }
}
