using AwesomeAssertions;
using Kora.Core.Configuration;
using Kora.Core.Voice;
using Kora.Windows.Audio;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed class WindowsSpeechRateTests
{
    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(10)]
    public async Task Future_owned_windows_synthesis_consumes_exact_native_rate_before_start_without_autoplay(int rate)
    {
        var events = new List<string>();
        await using var service = new WindowsTextToSpeechService(null, NullLogger<WindowsTextToSpeechService>.Instance,
            setOwnedWindowsGain: volume => events.Add("gain:" + volume),
            cancelOwnedSynthesis: () => events.Add("retire"),
            setOwnedWindowsRate: value => events.Add("rate:" + value));
        service.SetWindowsSpeechRate(new(rate));
        service.SetWindowsSpeechRate(new(rate));
        events.Should().NotContain(item => item.StartsWith("rate", StringComparison.Ordinal));
        service.StartOwnedWindowsSynthesis(PlaybackVolume.Default, () => events.Add("future-fake-synthesis"));
        events.TakeLast(3).Should().Equal("gain:100", "rate:" + rate, "future-fake-synthesis");
        service.IsSpeaking.Should().BeFalse();
        service.PlaybackFrame.Should().Be(SpeechPlaybackFrame.Inactive);
    }

    [Fact]
    public async Task Unsupported_and_unconfirmed_rate_never_calls_setter_or_starts_synthesis()
    {
        var values = new List<int>();
        var starts = 0;
        await using var service = new WindowsTextToSpeechService(null, NullLogger<WindowsTextToSpeechService>.Instance,
            setOwnedWindowsGain: _ => { }, setOwnedWindowsRate: values.Add);
        service.SetWindowsSpeechRate(null);
        service.ApplyOwnedWindowsRate(SpeechProviderIds.Kokoro);
        FluentActions.Invoking(() => service.ApplyOwnedWindowsRate("unknown")).Should().Throw<WindowsSpeechRateUnavailableException>();
        FluentActions.Invoking(() => service.StartOwnedWindowsSynthesis(PlaybackVolume.Default, () => starts++)).Should().Throw<WindowsSpeechRateUnavailableException>();
        await FluentActions.Awaiting(() => service.SpeakAsync("Must not synthesize", new("not-installed", "Unknown", "en-US", SpeechVoiceGender.Unknown),
            new("not-a-device", "No device"), TestContext.Current.CancellationToken)).Should().ThrowAsync<WindowsSpeechRateUnavailableException>();
        values.Should().BeEmpty();
        starts.Should().Be(0);
        service.IsSpeaking.Should().BeFalse();
        service.SetWindowsSpeechRate(WindowsSpeechRate.Default);
        service.StartOwnedWindowsSynthesis(PlaybackVolume.Default, () => starts++);
        values.Should().Equal(0);
        starts.Should().Be(1);
    }

    [Fact]
    public async Task Native_setter_failure_has_no_synthesis_or_playback_and_is_not_reported_as_success()
    {
        await using var service = new WindowsTextToSpeechService(null, NullLogger<WindowsTextToSpeechService>.Instance,
            setOwnedWindowsGain: _ => { }, setOwnedWindowsRate: _ => throw new InvalidOperationException("native setter failed"));
        var started = false;
        FluentActions.Invoking(() => service.StartOwnedWindowsSynthesis(PlaybackVolume.Default, () => started = true))
            .Should().Throw<WindowsSpeechRateUnavailableException>()
            .WithInnerException<InvalidOperationException>().WithMessage("native setter failed");
        started.Should().BeFalse();
        service.IsSpeaking.Should().BeFalse();
    }

    [Fact]
    public async Task Rate_changes_retire_active_and_queued_generations_and_callbacks_can_reenter_without_replay()
    {
        WindowsTextToSpeechService? service = null;
        var cancellations = 0;
        service = new WindowsTextToSpeechService(null, NullLogger<WindowsTextToSpeechService>.Instance,
            setOwnedWindowsGain: null, cancelOwnedSynthesis: () =>
            {
                cancellations++;
                service!.OnPlaybackStopped(new object(), new());
            });
        await using (service)
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = 0;
            var active = service.RunCurrentOutputAsync(async (_, _) => { entered++; started.SetResult(); await finish.Task; },
                TestContext.Current.CancellationToken);
            await started.Task;
            var queued = service.RunCurrentOutputAsync((_, _) => { entered++; return Task.CompletedTask; }, TestContext.Current.CancellationToken);
            service.SetWindowsSpeechRate(new(-10));
            service.SetWindowsSpeechRate(new(-10));
            finish.SetResult();
            await FluentActions.Awaiting(async () => await active).Should().ThrowAsync<OperationCanceledException>();
            await FluentActions.Awaiting(async () => await queued).Should().ThrowAsync<OperationCanceledException>();
            entered.Should().Be(1);
            cancellations.Should().Be(1);
            service.SetWindowsSpeechRate(WindowsSpeechRate.Default);
            service.IsSpeaking.Should().BeFalse();
            entered.Should().Be(1);
        }
        service.Invoking(item => item.SetWindowsSpeechRate(new(1))).Should().Throw<ObjectDisposedException>();
    }
}
