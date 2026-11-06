using AwesomeAssertions;
using Kora.Core.Platform;
using Kora.Core.Voice;
using Kora.Windows.Audio;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed partial class ActivatedVoiceRecognitionTests
{
    [Theory]
    [InlineData(false, 7)]
    [InlineData(true, 20)]
    public async Task ConfirmedTimingReachesNativeFactoryAndExpiresAtExactFrozenBoundary(bool reply, int start)
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var native = new FakeCapture();
        factory.OpenResult.SetResult(native);
        var source = new TimingSource { Timing = new(7, 20, 2) };
        var clock = new CaptureClock();
        await using var service = new WindowsVoiceRecognitionService(Logger, privacy, factory, VoiceCaptureLimits.Default, clock, source);
        var transcripts = new List<VoiceTranscriptEventArgs>();
        service.TranscriptRecognized += (_, args) => transcripts.Add(args);
        if (reply) { await service.BeginReplyAsync(Microphone, ["Yes"], static () => true, TestContext.Current.CancellationToken); }
        else { await BeginCaptureAsync(service); }
        service.AcceptCaptureGeneration(service.Generation).Should().BeTrue();
        factory.SpeechStartWindow.Should().Be(TimeSpan.FromSeconds(start));
        factory.TrailingSilence.Should().Be(TimeSpan.FromSeconds(2));
        source.Timing = new(10, 60, 3);
        clock.Advance(TimeSpan.FromSeconds(start) - TimeSpan.FromTicks(1));
        service.IsListening.Should().BeTrue();
        clock.Advance(TimeSpan.FromTicks(1));
        service.IsListening.Should().BeFalse();
        native.Transcript("late");
        await service.StopAsync(TestContext.Current.CancellationToken);
        transcripts.Should().BeEmpty();
        service.IsCaptureQuiescent.Should().BeTrue();
        factory.OpenCount.Should().Be(1);
        service.IsAmbientListeningAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task TimingChangesNeverReleaseOwnedSlotOrRetargetLateOutputAndNextCaptureGetsNewSnapshot()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var native = new FakeCapture { DisposeGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        factory.OpenResult.SetResult(native);
        var source = new TimingSource { Timing = new(7, 20, 2) };
        await using var service = new WindowsVoiceRecognitionService(Logger, privacy, factory, VoiceCaptureLimits.Default, timingSource: source);
        var transcripts = new List<VoiceTranscriptEventArgs>();
        service.TranscriptRecognized += (_, args) => transcripts.Add(args);
        await BeginCaptureAsync(service);
        var delayed = native.QueueTranscript("stale");
        try
        {
            source.Timing = new(8, 25, 1.5m);
            privacy.Set(Ready with { SessionState = WindowsSessionState.Locked });
            await native.DisposeEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
            service.IsCaptureQuiescent.Should().BeFalse();
            var refused = () => BeginCaptureAsync(service);
            await refused.Should().ThrowAsync<InvalidOperationException>();
            delayed();
            transcripts.Should().BeEmpty();
        }
        finally { native.DisposeGate.TrySetResult(); }
        await service.StopAsync(TestContext.Current.CancellationToken);
        privacy.Set(Ready);
        factory.OpenResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.OpenResult.SetResult(new FakeCapture());
        await BeginCaptureAsync(service);
        factory.SpeechStartWindow.Should().Be(TimeSpan.FromSeconds(8));
        factory.TrailingSilence.Should().Be(TimeSpan.FromSeconds(1.5));
        factory.OpenCount.Should().Be(2);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("cancelled")]
    [InlineData("profile")]
    public async Task TimingDoesNotGrantCaptureAuthority(string boundary)
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var source = new TimingSource { Unavailable = boundary is "unavailable" };
        await using var service = new WindowsVoiceRecognitionService(Logger, privacy, factory, VoiceCaptureLimits.Default, timingSource: source);
        if (boundary is "profile") { service.InitializeProfile(null); }
        using var cancellation = new CancellationTokenSource();
        if (boundary is "cancelled") { cancellation.Cancel(); }
        var begin = () => service.BeginPushToTalkAsync(Microphone, ["help"], cancellationToken: cancellation.Token);
        await begin.Should().ThrowAsync<Exception>();
        factory.OpenCount.Should().Be(0);
        service.IsListening.Should().BeFalse();
        service.IsCaptureQuiescent.Should().BeTrue();
    }

    private sealed class TimingSource : IVoiceCaptureTimingSource
    {
        public VoiceCaptureTiming Timing { get; set; } = VoiceCaptureTiming.Default;
        public bool Unavailable { get; init; }
        public VoiceCaptureTiming GetTiming() => Unavailable ? throw new InvalidOperationException("Unconfirmed timing") : Timing;
    }

    private sealed class CaptureClock : TimeProvider
    {
        private TimeSpan elapsed;
        private readonly List<Timer> timers = [];
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(callback, state, elapsed + dueTime);
            timers.Add(timer);
            return timer;
        }
        public void Advance(TimeSpan amount)
        {
            elapsed += amount;
            foreach (var timer in timers.ToArray()) { timer.Fire(elapsed); }
        }
        private sealed class Timer(TimerCallback callback, object? state, TimeSpan due) : ITimer
        {
            private bool disposed;
            public void Fire(TimeSpan now) { if (!disposed && now >= due) { disposed = true; callback(state); } }
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() => disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
