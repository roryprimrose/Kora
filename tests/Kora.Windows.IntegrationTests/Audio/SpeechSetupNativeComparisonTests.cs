using System.Diagnostics;
using System.Threading.Channels;

using AwesomeAssertions;

using Kora.Application.Voice;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Platform;
using Kora.Core.Voice;
using Kora.Windows.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Audio;

[Collection("Host tracing")]
public sealed class SpeechSetupNativeComparisonTests : IDisposable
{
    private readonly ActivityListener activityListener = Listen();
    private static readonly SpeechComparisonProfile Profile = new(SpeechInputSelection.Default, "Exact installed fixture profile");
    private static readonly MicrophoneDevice Microphone = new("mic", "Synthetic microphone");

    [Fact]
    public async Task EntireInteractionUsesExclusiveInertNativeCaptureAndMeasuredClockWithoutRoutingOrPreferencePublication()
    {
        using var root = Root();
        using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
        var factory = new Factory();
        var clock = new Clock();
        await using var native = Native(privacy, factory, clock);
        await using var voice = new HandsFreeVoiceRecognitionService(native, new UnavailableStage(),
            NullLogger<HandsFreeVoiceRecognitionService>.Instance);
        await using var comparison = new SpeechSetupComparison(voice, voice);
        var routed = new List<string>();
        voice.TranscriptRecognized += (_, args) => routed.Add(args.Transcript);
        native.RecognitionFailed += (_, _) => routed.Add("failure");
        native.CaptureStateChanged += (_, _) => routed.Add("capture");
        native.RecognitionCompleted += (_, _) => routed.Add("completion");
        comparison.ExpectedPhrase = "Kora approve deletion";
        comparison.Review(Microphone, static () => true);
        factory.Opens.Should().Be(0);
        var originalProfile = voice.ProfileGeneration;
        var run = comparison.TestAsync(true);
        for (var trial = 1; trial <= 2; trial++)
        {
            var open = await factory.NextAsync();
            comparison.Status.Should().StartWith("Opening");
            open.Phrases.Should().BeEmpty("the expected phrase must not bias grammar or become a command");
            open.Profile.Should().Be(originalProfile);
            open.Admitted().Should().BeTrue();
            var capture = new ActivatedVoiceRecognitionTests.FakeCapture
            {
                DisposeGate = new(TaskCreationOptions.RunContinuationsAsynchronously),
            };
            capture.BeforeStart = () =>
            {
                clock.Advance(TimeSpan.FromMilliseconds(75));
                capture.Audio([1, 2, 3]);
                capture.Transcript("foreign", new object());
                capture.Transcript(trial == 1 ? "kora approve deletion" : "not the same phrase");
            };
            open.Result.SetResult(capture);
            await capture.DisposeEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
            run.IsCompleted.Should().BeFalse();
            open.Stream.BufferedBytes.Should().Be(0);
            var late = capture.QueueTranscript("Kora confirm all");
            voice.IsCaptureQuiescent.Should().BeFalse();
            await FluentActions.Awaiting(() => voice.BeginPushToTalkAsync(Microphone, [], cancellationToken: TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
            await FluentActions.Awaiting(() => voice.BeginReplyAsync(Microphone, [], static () => true, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
            await FluentActions.Awaiting(() => voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
            await FluentActions.Awaiting(() => voice.RetireProfileAsync(TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
            late();
            capture.DisposeGate.SetResult();
        }
        await run;
        routed.Should().BeEmpty();
        comparison.Results.Should().HaveCount(2);
        comparison.Results[0].Correct.Should().BeTrue();
        comparison.Results[1].Correct.Should().BeFalse();
        comparison.Results.Should().OnlyContain(result => result.Observation.OpenToStart == TimeSpan.FromMilliseconds(75));
        comparison.Results[0].Observation.ColdStartup.Should().BeNull();
        comparison.Results[0].Observation.ResourceBytes.Should().BeNull();
        voice.ProfileGeneration.Should().Be(originalProfile);
        voice.IsListening.Should().BeFalse();
        voice.IsCaptureQuiescent.Should().BeTrue();
        comparison.Clear();
        comparison.Results.Should().BeEmpty();
        clock.FireAllQueued();
        routed.Should().BeEmpty();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("privacy")]
    [InlineData("topology")]
    [InlineData("owner")]
    [InlineData("deadline")]
    [InlineData("elapsed")]
    [InlineData("overflow")]
    public async Task RevocationAndLimitsCancelButDoNotReleaseExclusiveOwnershipBeforeNativeJoin(string cause)
    {
        using var root = Root();
        using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
        var factory = new Factory();
        var clock = new Clock();
        await using var native = Native(privacy, factory, clock);
        var admitted = true;
        using var cancel = new CancellationTokenSource();
        await using var lease = await native.AcquireComparisonAsync(native.ProfileGeneration, Microphone,
            () => admitted, cancel.Token);
        var work = lease.CaptureAsync(Profile, TestContext.Current.CancellationToken);
        var open = await factory.NextAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capture = new ActivatedVoiceRecognitionTests.FakeCapture
        {
            DisposeGate = new(TaskCreationOptions.RunContinuationsAsynchronously),
            BeforeStart = () => started.TrySetResult(),
        };
        open.Result.SetResult(capture);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        var late = capture.QueueTranscript("Kora unlock");
        switch (cause)
        {
            case "privacy": privacy.Set(privacy.Current with { SessionState = WindowsSessionState.Locked }); break;
            case "topology": privacy.Set(privacy.Current with { TopologyRevision = 2 }); break;
            case "owner": admitted = false; clock.FireRevalidation(); break;
            case "deadline": clock.FireDeadline(); break;
            case "elapsed": clock.Advance(SpeechComparisonObservation.MaximumAttempt); capture.Audio([1]); break;
            case "overflow": capture.Audio(new byte[64001]); break;
            default: cancel.Cancel(); break;
        }
        await capture.DisposeEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        open.Stream.BufferedBytes.Should().Be(0);
        work.IsCompleted.Should().BeFalse();
        native.IsCaptureQuiescent.Should().BeFalse();
        open.Admitted().Should().BeFalse();
        late();
        capture.DisposeGate.SetResult();
        await FluentActions.Awaiting(() => work).Should().ThrowAsync<OperationCanceledException>();
        await lease.DisposeAsync();
        native.IsCaptureQuiescent.Should().BeTrue();
        native.ProfileGeneration.Should().Be(new SpeechInputGeneration(0, SpeechInputSelection.Default));
        clock.FireAllQueued();
    }

    [Fact]
    public async Task CancellationDuringNonCooperativeNativeOpenWaitsForLateResourceAndDisposesIt()
    {
        using var root = Root();
        using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
        var factory = new Factory();
        await using var native = Native(privacy, factory, new Clock());
        using var cancel = new CancellationTokenSource();
        await using var lease = await native.AcquireComparisonAsync(native.ProfileGeneration, Microphone, static () => true, cancel.Token);
        var work = lease.CaptureAsync(Profile, TestContext.Current.CancellationToken);
        var open = await factory.NextAsync();
        cancel.Cancel();
        var disposing = lease.DisposeAsync().AsTask();
        disposing.IsCompleted.Should().BeFalse();
        native.IsCaptureQuiescent.Should().BeFalse();
        var late = new ActivatedVoiceRecognitionTests.FakeCapture();
        open.Result.SetResult(late);
        await FluentActions.Awaiting(() => work).Should().ThrowAsync<OperationCanceledException>();
        await disposing;
        late.Disposed.Task.IsCompletedSuccessfully.Should().BeTrue();
        late.StartCount.Should().Be(0);
        open.Stream.BufferedBytes.Should().Be(0);
        native.IsCaptureQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("oversized")]
    [InlineData("failure")]
    [InlineData("cleanup")]
    public async Task MissingInvalidAndFailedEvidenceCannotProduceAResult(string mode)
    {
        using var root = Root();
        using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
        var factory = new Factory();
        var native = Native(privacy, factory, new Clock());
        var lease = await native.AcquireComparisonAsync(native.ProfileGeneration, Microphone, static () => true, TestContext.Current.CancellationToken);
        var work = lease.CaptureAsync(Profile, TestContext.Current.CancellationToken);
        var open = await factory.NextAsync();
        var capture = new ActivatedVoiceRecognitionTests.FakeCapture();
        capture.BeforeStart = () =>
        {
            switch (mode)
            {
                case "empty": capture.Completed.SetResult(); break;
                case "oversized": capture.Transcript(new string('x', 257)); break;
                case "failure": capture.Fail(); break;
                default: capture.Transcript("text"); break;
            }
        };
        if (string.Equals(mode, "cleanup", StringComparison.Ordinal))
        { capture.ReleaseResult = Task.FromException(new InvalidOperationException("Synthetic release failure")); }
        open.Result.SetResult(capture);
        await FluentActions.Awaiting(() => work).Should().ThrowAsync<InvalidOperationException>();
        if (string.Equals(mode, "cleanup", StringComparison.Ordinal))
        { await FluentActions.Awaiting(() => lease.DisposeAsync().AsTask()).Should().ThrowAsync<InvalidOperationException>(); }
        else { await lease.DisposeAsync(); }
        capture.Disposed.Task.IsCompletedSuccessfully.Should().BeTrue();
        open.Stream.BufferedBytes.Should().Be(0);
        native.IsCaptureQuiescent.Should().Be(!string.Equals(mode, "cleanup", StringComparison.Ordinal));
        if (string.Equals(mode, "cleanup", StringComparison.Ordinal))
        { await FluentActions.Awaiting(() => native.DisposeAsync().AsTask()).Should().ThrowAsync<InvalidOperationException>(); }
        else { await native.DisposeAsync(); }
    }

    [Fact]
    public async Task ProductionOffersNothingAndExactGenerationAndProfileAreMandatory()
    {
        using var root = Root();
        using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
        await using var production = new WindowsVoiceRecognitionService(NullLogger<WindowsVoiceRecognitionService>.Instance, privacy);
        production.GetComparisonProfiles().Should().BeEmpty();
        await FluentActions.Awaiting(() => production.AcquireComparisonAsync(production.ProfileGeneration, Microphone,
            static () => true, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        var factory = new Factory();
        await using var native = Native(privacy, factory, new Clock());
        await FluentActions.Awaiting(() => native.AcquireComparisonAsync(new(99, SpeechInputSelection.Default), Microphone,
            static () => true, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        await using var lease = await native.AcquireComparisonAsync(native.ProfileGeneration, Microphone,
            static () => true, TestContext.Current.CancellationToken);
        await FluentActions.Awaiting(() => lease.CaptureAsync(Profile with { Name = "invented" }, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => native.RetireProfileAsync(TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        await native.StopAsync(TestContext.Current.CancellationToken);
        native.IsCaptureQuiescent.Should().BeTrue();
    }

    private static WindowsVoiceRecognitionService Native(ActivatedVoiceRecognitionTests.FakePrivacy privacy, Factory factory, Clock clock) =>
        new(NullLogger<WindowsVoiceRecognitionService>.Instance, privacy, factory, VoiceCaptureLimits.Default, clock);
    private static HostActivity Root() =>
        HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);

    public void Dispose() => activityListener.Dispose();
    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class Factory : IActivatedCaptureFactory
    {
        private readonly Channel<Open> opens = Channel.CreateUnbounded<Open>();
        public int Opens { get; private set; }
        public IReadOnlyList<SpeechComparisonProfile> GetComparisonProfiles() => [Profile];
        public Task<IActivatedCapture> OpenAsync(MicrophoneDevice microphone, IReadOnlyList<string> phrases,
            BlockingAudioStream stream, Func<bool> canOpen) => throw new NotSupportedException();
        public Task<IActivatedCapture> OpenAsync(MicrophoneDevice microphone, IReadOnlyList<string> phrases,
            BlockingAudioStream stream, Func<bool> canOpen, TimeSpan speechStartWindow, SpeechInputGeneration profile)
        {
            Opens++;
            var open = new Open(phrases, stream, canOpen, profile);
            opens.Writer.TryWrite(open).Should().BeTrue();
            return open.Result.Task;
        }
        public Task<Open> NextAsync() => opens.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask();
    }
    private sealed record Open(IReadOnlyList<string> Phrases, BlockingAudioStream Stream, Func<bool> Admitted, SpeechInputGeneration Profile)
    {
        public TaskCompletionSource<IActivatedCapture> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class UnavailableStage : IHandsFreeAudioStage
    {
        public bool IsAvailable => false;
        public Task<IHandsFreeAudioSession> OpenAsync(HandsFreeAudioBinding binding, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
    private sealed class Clock : TimeProvider
    {
        private long ticks;
        private readonly List<Timer> timers = [];
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref ticks);
        public void Advance(TimeSpan amount) => Interlocked.Add(ref ticks, amount.Ticks);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(() => callback(state), dueTime);
            lock (timers) { timers.Add(timer); }
            return timer;
        }
        public void FireDeadline() => Fire(SpeechComparisonObservation.MaximumAttempt);
        public void FireRevalidation() => Fire(TimeSpan.FromMilliseconds(100));
        private void Fire(TimeSpan due)
        {
            Timer[] snapshot;
            lock (timers) { snapshot = timers.Where(timer => timer.Due == due).ToArray(); }
            foreach (var timer in snapshot) { timer.Tick(); }
        }
        public void FireAllQueued()
        {
            Timer[] snapshot;
            lock (timers) { snapshot = timers.ToArray(); }
            foreach (var timer in snapshot) { timer.Tick(); }
        }
        private sealed class Timer(Action callback, TimeSpan due) : ITimer
        {
            public TimeSpan Due { get; } = due;
            public void Tick() => callback();
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
