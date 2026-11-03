using AwesomeAssertions;

using Kora.Core.Voice;
using Kora.Windows.Audio;

using Microsoft.Extensions.Logging.Abstractions;

using NAudio.Wave;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed class WindowsTextToSpeechOutputLifecycleTests
{
    [Fact]
    public async Task EverySynthesisAndPlaybackBoundaryRequiresFreshAdmissionAndRetiresLateCallbacks()
    {
        var starts = 0;
        var boundaries = 0;
        var eligible = true;
        Action? late = null;
        Task Admit(Action start, CancellationToken _)
        {
            boundaries++;
            late = start;
            if (eligible) { start(); }
            return Task.CompletedTask;
        }
        await WindowsTextToSpeechService.AdmitSpeechBoundaryAsync(() => starts++, Admit, TestContext.Current.CancellationToken);
        starts.Should().Be(1);
        FluentActions.Invoking(() => late!()).Should().Throw<OperationCanceledException>();
        eligible = false;
        await FluentActions.Awaiting(() => WindowsTextToSpeechService.AdmitSpeechBoundaryAsync(() => starts++,
            Admit, TestContext.Current.CancellationToken)).Should().ThrowAsync<OperationCanceledException>();
        boundaries.Should().Be(2);
        starts.Should().Be(1);
        FluentActions.Invoking(() => late!()).Should().Throw<OperationCanceledException>();
        await WindowsTextToSpeechService.AdmitSpeechBoundaryAsync(() => starts++, null, TestContext.Current.CancellationToken);
        starts.Should().Be(2);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await FluentActions.Awaiting(() => WindowsTextToSpeechService.AdmitSpeechBoundaryAsync(() => starts++,
            null, cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
        starts.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedAdmittedStartJoinsPhysicalSynthesisBeforeReleasingItsOwner(bool cancelled)
    {
        var stops = 0;
        await using var service = new WindowsTextToSpeechService(null, NullLogger<WindowsTextToSpeechService>.Instance,
            setOwnedWindowsGain: null, cancelOwnedSynthesis: () => stops++);
        using var cancellation = new CancellationTokenSource();
        var physical = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var joining = service.JoinRejectedSynthesisAsync(physical.Task, cancellation);
        cancellation.IsCancellationRequested.Should().BeTrue();
        stops.Should().Be(1);
        joining.IsCompleted.Should().BeFalse();
        if (cancelled) { physical.SetCanceled(cancellation.Token); }
        else { physical.SetResult(); }
        await joining;
    }

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
