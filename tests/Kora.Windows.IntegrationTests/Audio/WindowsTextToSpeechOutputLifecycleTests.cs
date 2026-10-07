using AwesomeAssertions;
using Kora.Core.Voice;
using Kora.Windows.Audio;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed class WindowsTextToSpeechOutputLifecycleTests
{
    [Fact]
    public async Task Genuine_native_output_gate_invalidates_queued_and_inflight_operations_without_opening_audio()
    {
        await using var service = new WindowsTextToSpeechService(NullLogger<WindowsTextToSpeechService>.Instance);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        var active = service.RunCurrentOutputAsync(async (_, _) =>
        {
            entered++;
            started.SetResult();
            await complete.Task;
        }, TestContext.Current.CancellationToken);
        await started.Task;
        var queued = service.RunCurrentOutputAsync((_, _) => { entered++; return Task.CompletedTask; }, TestContext.Current.CancellationToken);
        service.InvalidateOutput();
        complete.SetResult();
        await active.Invoking(async task => await task).Should().ThrowAsync<OperationCanceledException>();
        await queued.Invoking(async task => await task).Should().ThrowAsync<OperationCanceledException>();
        entered.Should().Be(1);
        await service.RunCurrentOutputAsync((_, _) => { entered++; return Task.CompletedTask; }, TestContext.Current.CancellationToken);
        entered.Should().Be(2);
        service.IsSpeaking.Should().BeFalse();
        service.PlaybackFrame.Should().Be(SpeechPlaybackFrame.Inactive);
    }

    [Fact]
    public async Task Late_retired_or_disposed_player_callbacks_cannot_recreate_playback_or_claim_completion()
    {
        var service = new WindowsTextToSpeechService(NullLogger<WindowsTextToSpeechService>.Instance);
        service.InvalidateOutput();
        service.OnPlaybackStopped(null, new(new IOException("owned retired callback")));
        service.OnPlaybackStopped(new object(), new(null));
        await service.DisposeAsync();
        service.OnPlaybackStopped(new object(), new(new IOException("owned disposed callback")));
        service.PlaybackFrame.Should().Be(SpeechPlaybackFrame.Inactive);
        var late = () => service.RunCurrentOutputAsync(static (_, _) => Task.CompletedTask, TestContext.Current.CancellationToken);
        await late.Should().ThrowAsync<ObjectDisposedException>();
    }
}
