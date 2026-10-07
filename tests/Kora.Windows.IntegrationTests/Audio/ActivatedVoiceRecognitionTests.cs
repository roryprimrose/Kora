using AwesomeAssertions;

using Kora.Core.Platform;
using Kora.Core.Voice;
using Kora.Windows.Audio;
using Kora.Windows.Session;
using Kora.Windows.IntegrationTests.Session;

using Neovolve.Logging.Xunit;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed class ActivatedVoiceRecognitionTests(
    ITestOutputHelper output) : LoggingTestsBase<WindowsVoiceRecognitionService>(output)
{
    private static readonly MicrophoneDevice Microphone = new("mic", "Synthetic microphone");

    [Fact]
    public async Task Prefix_retirement_discards_queued_native_audio_and_transcript_before_new_explicit_grammar()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var oldCapture = new FakeCapture();
        factory.OpenResult.SetResult(oldCapture);
        await using var service = Create(privacy, factory);
        var transcripts = new List<VoiceTranscriptEventArgs>();
        service.TranscriptRecognized += (_, args) => transcripts.Add(args);
        await service.BeginPushToTalkAsync(Microphone,
            ["Kora session help", "Kora get assistant name"], "Kora",
            TestContext.Current.CancellationToken);
        service.AcceptCaptureGeneration(service.Generation).Should().BeTrue();
        var oldGeneration = service.Generation;
        var oldTranscript = oldCapture.QueueTranscript("Kora session help");
        var oldAudio = oldCapture.QueueAudio([1, 2, 3]);
        service.InvalidateCapture();
        await service.StopAsync(TestContext.Current.CancellationToken);
        oldTranscript();
        oldAudio();
        service.AcceptCaptureGeneration(oldGeneration).Should().BeFalse();
        transcripts.Should().BeEmpty();
        service.IsCaptureQuiescent.Should().BeTrue();
        factory.OpenCount.Should().Be(1);
        var newCapture = new FakeCapture();
        factory.OpenResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.OpenResult.SetResult(newCapture);
        await service.BeginPushToTalkAsync(Microphone,
            ["Nova session help", "Nova get assistant name"], "Nova",
            TestContext.Current.CancellationToken);
        factory.Phrases.Should().Contain("Nova session help").And.NotContain("Kora session help");
        service.AcceptCaptureGeneration(service.Generation).Should().BeTrue();
        oldTranscript();
        newCapture.Transcript("Nova session help");
        transcripts.Should().ContainSingle().Which.Transcript.Should().Be("Nova session help");
    }

    [Theory]
    [InlineData(WindowsSessionState.Unknown, MicrophoneAccessState.Allowed)]
    [InlineData(WindowsSessionState.Locked, MicrophoneAccessState.Allowed)]
    [InlineData(WindowsSessionState.Disconnected, MicrophoneAccessState.Allowed)]
    [InlineData(WindowsSessionState.Suspended, MicrophoneAccessState.Allowed)]
    [InlineData(WindowsSessionState.Unlocked, MicrophoneAccessState.Denied)]
    [InlineData(WindowsSessionState.Unlocked, MicrophoneAccessState.Unknown)]
    public async Task Denied_session_or_permission_never_opens_audio(
        WindowsSessionState state, MicrophoneAccessState permission)
    {
        using var privacy = new FakePrivacy { Current = Ready with { SessionState = state, MicrophoneAccess = permission } };
        var factory = new FakeFactory();
        await using var service = Create(privacy, factory);
        var start = () => service.BeginPushToTalkAsync(Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken);

        await start.Should().ThrowAsync<InvalidOperationException>();
        factory.OpenCount.Should().Be(0);
        service.IsListening.Should().BeFalse();
        service.IsAmbientListeningAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Lock_closes_gate_and_releases_recorder_before_delayed_recognition_teardown()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var capture = new FakeCapture { DisposeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        factory.OpenResult.SetResult(capture);
        await using var service = Create(privacy, factory);
        var transcripts = new List<VoiceTranscriptEventArgs>();
        service.TranscriptRecognized += (_, args) => transcripts.Add(args);
        await BeginCaptureAsync(service);
        var generation = service.CaptureGeneration;
        var queuedResult = capture.QueueTranscript("stale");

        privacy.Set(Ready with { SessionState = WindowsSessionState.Locked });

        service.IsListening.Should().BeFalse();
        service.CaptureGeneration.Should().BeGreaterThan(generation);
        capture.ReleaseCount.Should().BeGreaterThan(0);
        capture.DisposeGate.Task.IsCompleted.Should().BeFalse();
        factory.Stream!.BufferedBytes.Should().Be(0);
        queuedResult();
        capture.Audio([1, 2, 3]);
        transcripts.Should().BeEmpty();
        capture.DisposeGate.SetResult();
    }

    [Theory]
    [InlineData(WindowsSessionState.Unknown)]
    [InlineData(WindowsSessionState.Locked)]
    [InlineData(WindowsSessionState.Disconnected)]
    [InlineData(WindowsSessionState.Suspended)]
    [InlineData(WindowsSessionState.SignedOut)]
    public async Task Production_observer_closes_capture_before_requery_and_discards_queued_native_callbacks(
        WindowsSessionState state)
    {
        using var source = new ObserverSource();
        using var observer = new WindowsPrivacyObservationService(source, Logger);
        var factory = new FakeFactory();
        var capture = new FakeCapture { DisposeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        factory.OpenResult.SetResult(capture);
        await using var service = new WindowsVoiceRecognitionService(Logger, observer, factory, VoiceCaptureLimits.Default);
        var results = new List<VoiceTranscriptEventArgs>();
        service.TranscriptRecognized += (_, args) => results.Add(args);
        await BeginCaptureAsync(service);
        var generation = service.Generation;
        var queuedAudio = capture.QueueAudio([1, 2, 3]);
        var queuedTranscript = capture.QueueTranscript("lock the machine");
        var closedBeforeQuery = false;
        source.BeforeRead = () =>
        {
            closedBeforeQuery = !service.IsListening && service.Generation > generation &&
                capture.ReleaseCount > 0 && factory.Stream!.BufferedBytes == 0;
        };
        try
        {
            source.Notify(state);
            closedBeforeQuery.Should().BeTrue();
            queuedAudio();
            queuedTranscript();
            results.Should().BeEmpty();
            service.IsListening.Should().BeFalse();
            factory.OpenCount.Should().Be(1);
        }
        finally
        {
            capture.DisposeGate.TrySetResult();
        }
    }

    [Theory]
    [InlineData(MicrophoneAccessState.Denied)]
    [InlineData(MicrophoneAccessState.Unknown)]
    public async Task Production_permission_timer_closes_capture_and_restore_does_not_reopen_it(
        MicrophoneAccessState permission)
    {
        using var source = new ObserverSource();
        var time = new PrivacyPollingTimeProvider();
        using var observer = new WindowsPrivacyObservationService(source, Logger, TimeSpan.FromSeconds(1), time);
        var factory = new FakeFactory();
        var capture = new FakeCapture();
        factory.OpenResult.SetResult(capture);
        await using var service = new WindowsVoiceRecognitionService(Logger, observer, factory, VoiceCaptureLimits.Default);
        await BeginCaptureAsync(service);
        var generation = service.Generation;
        var queuedTranscript = capture.QueueTranscript("lock the machine");
        var results = new List<VoiceTranscriptEventArgs>();
        service.TranscriptRecognized += (_, args) => results.Add(args);

        source.Snapshot = Ready with { MicrophoneAccess = permission };
        time.Timer.Tick();
        service.IsListening.Should().BeFalse();
        service.Generation.Should().BeGreaterThan(generation);
        capture.ReleaseCount.Should().BeGreaterThan(0);
        source.Snapshot = Ready;
        time.Timer.Tick();
        queuedTranscript();

        results.Should().BeEmpty();
        service.IsListening.Should().BeFalse();
        factory.OpenCount.Should().Be(1);
    }

    [Theory]
    [InlineData(WindowsSessionState.Unknown)]
    [InlineData(WindowsSessionState.Locked)]
    [InlineData(WindowsSessionState.Disconnected)]
    [InlineData(WindowsSessionState.Suspended)]
    public async Task Production_observer_cancels_pending_open_and_a_late_native_result_never_records(
        WindowsSessionState state)
    {
        using var source = new ObserverSource();
        using var observer = new WindowsPrivacyObservationService(source, Logger);
        var factory = new FakeFactory();
        await using var service = new WindowsVoiceRecognitionService(Logger, observer, factory, VoiceCaptureLimits.Default);
        var opening = service.BeginPushToTalkAsync(Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken);
        await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        var generation = service.Generation;

        source.Notify(state);

        var action = () => opening;
        await action.Should().ThrowAsync<OperationCanceledException>();
        var late = new FakeCapture();
        factory.OpenResult.SetResult(late);
        await late.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        service.Generation.Should().BeGreaterThan(generation);
        late.StartCount.Should().Be(0);
        late.ReleaseCount.Should().BeGreaterThan(0);
        service.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task Production_poll_query_failure_closes_capture_and_recovered_observation_cannot_restart_it()
    {
        using var source = new ObserverSource();
        var time = new PrivacyPollingTimeProvider();
        using var observer = new WindowsPrivacyObservationService(source, Logger, TimeSpan.FromSeconds(1), time);
        var factory = new FakeFactory();
        var capture = new FakeCapture();
        factory.OpenResult.SetResult(capture);
        await using var service = new WindowsVoiceRecognitionService(Logger, observer, factory, VoiceCaptureLimits.Default);
        await BeginCaptureAsync(service);
        var generation = service.Generation;
        source.BeforeRead = () => throw new InvalidOperationException("Synthetic permission/endpoint query failure");

        time.Timer.Tick();

        observer.Current.SessionState.Should().Be(WindowsSessionState.Unknown);
        observer.Current.MicrophoneAccess.Should().Be(MicrophoneAccessState.Unknown);
        service.IsListening.Should().BeFalse();
        service.Generation.Should().BeGreaterThan(generation);
        capture.ReleaseCount.Should().BeGreaterThan(0);
        source.BeforeRead = null;
        time.Timer.Tick();
        observer.Current.CanCapture.Should().BeTrue();
        service.IsListening.Should().BeFalse();
        factory.OpenCount.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shutdown_of_a_held_activation_releases_recorder_before_teardown_and_rejects_late_results(
        bool dispose)
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var capture = new FakeCapture { DisposeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        factory.OpenResult.SetResult(capture);
        await using var service = Create(privacy, factory);
        await BeginCaptureAsync(service);
        var generation = service.Generation;
        var queuedAudio = capture.QueueAudio([1, 2, 3]);
        var queuedTranscript = capture.QueueTranscript("lock the machine");
        var results = new List<VoiceTranscriptEventArgs>();
        service.TranscriptRecognized += (_, args) => results.Add(args);

        var shutdown = dispose ? service.DisposeAsync().AsTask() : service.StopAsync(TestContext.Current.CancellationToken);
        try
        {
            await capture.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            service.IsListening.Should().BeFalse();
            service.Generation.Should().BeGreaterThan(generation);
            capture.ReleaseCount.Should().BeGreaterThan(0);
            shutdown.IsCompleted.Should().BeFalse();
            service.IsCaptureQuiescent.Should().BeFalse();
            queuedAudio();
            queuedTranscript();
            results.Should().BeEmpty();
        }
        finally
        {
            capture.DisposeGate.TrySetResult();
        }
        await shutdown;
        service.IsCaptureQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Production_topology_observer_preserves_System_routing_but_never_substitutes_a_lost_pinned_endpoint(
        bool pinned, bool defaultUnavailable)
    {
        using var source = new ObserverSource();
        using var observer = new WindowsPrivacyObservationService(source, Logger);
        var factory = new FakeFactory();
        var capture = new FakeCapture();
        factory.OpenResult.SetResult(capture);
        await using var service = new WindowsVoiceRecognitionService(Logger, observer, factory, VoiceCaptureLimits.Default);
        var selected = pinned ? Microphone : SystemAudioDevices.Microphone;
        await service.BeginPushToTalkAsync(selected, ["help"], cancellationToken: TestContext.Current.CancellationToken);
        service.AcceptCaptureGeneration(service.Generation).Should().BeTrue();
        var generation = service.Generation;
        source.Snapshot = Ready with
        {
            ActiveMicrophoneIds = ["other"],
            DefaultMicrophoneId = defaultUnavailable ? null : "other",
        };

        source.Notify(topology: true);

        var shouldClose = pinned || defaultUnavailable;
        service.IsListening.Should().Be(!shouldClose);
        factory.SelectedMicrophone.Should().Be(selected);
        factory.OpenCount.Should().Be(1);
        if (shouldClose)
        {
            service.Generation.Should().BeGreaterThan(generation);
            capture.ReleaseCount.Should().BeGreaterThan(0);
        }
        else
        {
            service.Generation.Should().Be(generation);
            capture.ReleaseCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task Cancelled_open_returns_promptly_and_late_success_is_disposed_without_recording()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        await using var service = Create(privacy, factory);
        using var cancellation = new CancellationTokenSource();
        var start = service.BeginPushToTalkAsync(Microphone, ["help"], cancellationToken: cancellation.Token);
        await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        cancellation.Cancel();

        var action = () => start;
        await action.Should().ThrowAsync<OperationCanceledException>();
        var late = new FakeCapture();
        factory.OpenResult.SetResult(late);
        await late.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        late.StartCount.Should().Be(0);
        late.ReleaseCount.Should().BeGreaterThan(0);
        service.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task Invalidation_cancels_pending_open_and_late_open_cannot_restore_generation()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        await using var service = Create(privacy, factory);
        var start = service.BeginPushToTalkAsync(Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken);
        await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        service.InvalidateCapture();
        var invalidated = service.CaptureGeneration;
        var action = () => start;
        await action.Should().ThrowAsync<OperationCanceledException>();
        var late = new FakeCapture();
        factory.OpenResult.SetResult(late);
        await late.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        service.CaptureGeneration.Should().Be(invalidated);
        late.StartCount.Should().Be(0);
    }

    [Fact]
    public async Task Open_deadline_quarantines_late_native_result()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var limits = VoiceCaptureLimits.Default with { OpenDeadline = TimeSpan.FromMilliseconds(50) };
        await using var service = Create(privacy, factory, limits);
        var action = () => service.BeginPushToTalkAsync(Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<TimeoutException>();
        var late = new FakeCapture();
        factory.OpenResult.SetResult(late);
        await late.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        late.StartCount.Should().Be(0);
    }

    [Fact]
    public async Task Fresh_privacy_and_topology_are_revalidated_after_native_open()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        await using var service = Create(privacy, factory);
        var start = service.BeginPushToTalkAsync(Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken);
        await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        privacy.Current = Ready with { TopologyRevision = 2 };
        var late = new FakeCapture();
        factory.OpenResult.SetResult(late);

        var action = () => start;
        await action.Should().ThrowAsync<InvalidOperationException>();
        late.StartCount.Should().Be(0);
        late.ReleaseCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Transcript_requires_current_sender_generation_and_bounds()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var capture = new FakeCapture();
        factory.OpenResult.SetResult(capture);
        await using var service = Create(privacy, factory);
        var results = new List<VoiceTranscriptEventArgs>();
        service.TranscriptRecognized += (_, args) => results.Add(args);
        await service.StartAsync(Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken);
        service.AcceptCaptureGeneration(service.Generation).Should().BeTrue();
        capture.Transcript("wrong sender", new FakeCapture());
        capture.Transcript("help");

        results.Should().ContainSingle();
        results[0].Generation.Should().Be(service.CaptureGeneration);
        var stale = capture.QueueTranscript("old generation");
        service.InvalidateCapture();
        stale();
        results.Should().ContainSingle();
    }

    [Fact]
    public async Task Oversized_transcript_and_audio_overflow_fail_closed()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var capture = new FakeCapture();
        factory.OpenResult.SetResult(capture);
        await using var service = Create(privacy, factory);
        var results = new List<VoiceTranscriptEventArgs>();
        service.TranscriptRecognized += (_, args) => results.Add(args);
        await BeginCaptureAsync(service);

        capture.Transcript(new string('a', 4097));

        results.Should().BeEmpty();
        service.IsListening.Should().BeFalse();
        capture.ReleaseCount.Should().BeGreaterThan(0);
        var next = new FakeFactory();
        var nextCapture = new FakeCapture();
        next.OpenResult.SetResult(nextCapture);
        await using var second = Create(privacy, next);
        await BeginCaptureAsync(second);
        nextCapture.Audio(new byte[64001]);
        second.IsListening.Should().BeFalse();
        next.Stream!.BufferedBytes.Should().Be(0);
    }

    [Fact]
    public async Task Restoration_and_device_reconnection_do_not_restart_recording()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var capture = new FakeCapture();
        factory.OpenResult.SetResult(capture);
        await using var service = Create(privacy, factory);
        await BeginCaptureAsync(service);

        privacy.Set(Ready with { ActiveMicrophoneIds = [], DefaultMicrophoneId = null });
        privacy.Set(Ready);

        service.IsListening.Should().BeFalse();
        factory.OpenCount.Should().Be(1);
        capture.StartCount.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Valid_default_microphone_changes_preserve_active_generation_and_pinned_capture(bool pinned)
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var capture = new FakeCapture();
        factory.OpenResult.SetResult(capture);
        await using var service = Create(privacy, factory);
        await service.BeginPushToTalkAsync(
            pinned ? Microphone : SystemAudioDevices.Microphone, ["help"],
            cancellationToken: TestContext.Current.CancellationToken);
        service.AcceptCaptureGeneration(service.Generation).Should().BeTrue();
        var generation = service.Generation;

        privacy.Set(Ready with
        {
            TopologyRevision = 2,
            ActiveMicrophoneIds = pinned ? ["mic", "new-mic"] : ["new-mic"],
            DefaultMicrophoneId = "new-mic",
        });

        service.IsListening.Should().BeTrue();
        service.Generation.Should().Be(generation);
        capture.ReleaseCount.Should().Be(0);
        capture.StartCount.Should().Be(1);
        factory.OpenCount.Should().Be(1);
        await service.EndPushToTalkAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PTT_end_releases_capture_then_accepts_only_bounded_final_recognition()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var capture = new FakeCapture { CompleteOnFinish = false };
        factory.OpenResult.SetResult(capture);
        await using var service = Create(privacy, factory);
        var result = new TaskCompletionSource<VoiceTranscriptEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.TranscriptRecognized += (_, args) => result.TrySetResult(args);
        await BeginCaptureAsync(service);
        var generation = service.CaptureGeneration;

        var ending = ((IVoiceRecognitionService)service).EndCaptureAsync(TestContext.Current.CancellationToken);
        capture.Audio([1, 2, 3]);
        capture.Transcript("help");
        capture.Completed.SetResult();
        await ending;

        capture.ReleaseCount.Should().BeGreaterThan(0);
        (await result.Task).Generation.Should().Be(generation);
        factory.Stream!.BufferedBytes.Should().Be(0);
        service.IsListening.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Empty_and_maximum_capture_deadlines_release_the_microphone(bool empty)
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var capture = new FakeCapture();
        factory.OpenResult.SetResult(capture);
        var limits = empty
            ? VoiceCaptureLimits.Default with { EmptySpeechDeadline = TimeSpan.FromMilliseconds(50) }
            : VoiceCaptureLimits.Default with { MaximumCapture = TimeSpan.FromMilliseconds(50) };
        await using var service = Create(privacy, factory, limits);
        await BeginCaptureAsync(service);
        if (!empty)
        {
            capture.DetectSpeech();
        }

        await capture.Released.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        service.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task Releasing_PTT_during_open_cancels_the_pending_activation()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        await using var service = Create(privacy, factory);
        var start = service.BeginPushToTalkAsync(Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken);
        await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        await service.EndPushToTalkAsync(TestContext.Current.CancellationToken);

        var action = () => start;
        await action.Should().ThrowAsync<OperationCanceledException>();
        var late = new FakeCapture();
        factory.OpenResult.SetResult(late);
        await late.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        late.StartCount.Should().Be(0);
    }

    [Fact]
    public async Task Normal_completion_keeps_one_delivered_transcript_valid_until_privacy_invalidation()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var capture = new FakeCapture();
        factory.OpenResult.SetResult(capture);
        await using var service = Create(privacy, factory);
        var results = new List<VoiceTranscriptEventArgs>();
        service.TranscriptRecognized += (_, args) => results.Add(args);
        await BeginCaptureAsync(service);
        var generation = service.CaptureGeneration;
        capture.Transcript("help");
        capture.Transcript("duplicate");
        capture.Completed.TrySetResult();
        await capture.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        results.Should().ContainSingle();
        results[0].Generation.Should().Be(generation);
        service.CaptureGeneration.Should().Be(generation);
        privacy.Set(Ready with { SessionState = WindowsSessionState.Locked });

        service.CaptureGeneration.Should().BeGreaterThan(generation);
    }

    [Fact]
    public async Task Changed_privacy_without_a_notification_is_checked_before_audio_and_results()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var capture = new FakeCapture();
        factory.OpenResult.SetResult(capture);
        await using var service = Create(privacy, factory);
        var results = new List<VoiceTranscriptEventArgs>();
        service.TranscriptRecognized += (_, args) => results.Add(args);
        await BeginCaptureAsync(service);
        privacy.Current = Ready with { MicrophoneAccess = MicrophoneAccessState.Denied };

        capture.Audio([1, 2, 3]);
        capture.Transcript("stale");

        results.Should().BeEmpty();
        service.IsListening.Should().BeFalse();
        capture.ReleaseCount.Should().BeGreaterThan(0);
        factory.Stream!.BufferedBytes.Should().Be(0);
    }

    [Fact]
    public async Task Late_callbacks_from_an_old_capture_cannot_feed_a_replacement_capture()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var old = new FakeCapture();
        factory.OpenResult.SetResult(old);
        await using var service = Create(privacy, factory);
        var results = new List<VoiceTranscriptEventArgs>();
        service.TranscriptRecognized += (_, args) => results.Add(args);
        await BeginCaptureAsync(service);
        var oldTranscript = old.QueueTranscript("old");
        var oldAudio = old.QueueAudio([1, 2, 3]);
        await service.StopAsync(TestContext.Current.CancellationToken);
        var current = new FakeCapture();
        factory.OpenResult = new TaskCompletionSource<IActivatedCapture>(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.OpenResult.SetResult(current);
        await BeginCaptureAsync(service);

        oldTranscript();
        oldAudio();

        results.Should().BeEmpty();
        factory.Stream!.BufferedBytes.Should().Be(0);
        service.IsListening.Should().BeTrue();
        current.Transcript("current");
        results.Should().ContainSingle().Which.Transcript.Should().Be("current");
    }

    [Fact]
    public async Task A_slow_authoritative_probe_is_inside_the_capture_open_deadline()
    {
        using var privacy = new FakePrivacy();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        privacy.BeforeRefresh = () =>
        {
            entered.TrySetResult();
            release.Wait(TestContext.Current.CancellationToken);
        };
        var factory = new FakeFactory();
        var limits = VoiceCaptureLimits.Default with { OpenDeadline = TimeSpan.FromMilliseconds(100) };
        await using var service = Create(privacy, factory, limits);
        var start = service.BeginPushToTalkAsync(Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        try
        {
            var action = () => start;
            await action.Should().ThrowAsync<TimeoutException>();
            factory.OpenCount.Should().Be(0);
            service.IsListening.Should().BeFalse();
        }
        finally
        {
            release.Set();
            await privacy.Refreshed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task The_base_voice_contract_invalidates_the_same_synchronous_generation_gate()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var capture = new FakeCapture();
        factory.OpenResult.SetResult(capture);
        await using var service = Create(privacy, factory);
        IVoiceRecognitionService contract = service;
        await contract.StartAsync(Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken);
        service.AcceptCaptureGeneration(service.Generation).Should().BeTrue();
        var generation = contract.Generation;

        contract.InvalidateCapture();

        contract.IsListening.Should().BeFalse();
        contract.Generation.Should().BeGreaterThan(generation);
        capture.ReleaseCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task A_slow_native_start_is_bounded_and_late_completion_cannot_restore_listening()
    {
        using var privacy = new FakePrivacy();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capture = new FakeCapture
        {
            BeforeStart = () =>
            {
                entered.TrySetResult();
                release.Wait(TestContext.Current.CancellationToken);
                exited.TrySetResult();
            },
        };
        var factory = new FakeFactory();
        factory.OpenResult.SetResult(capture);
        var limits = VoiceCaptureLimits.Default with { OpenDeadline = TimeSpan.FromMilliseconds(200) };
        await using var service = Create(privacy, factory, limits);
        var start = service.BeginPushToTalkAsync(Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        service.IsListening.Should().BeFalse();
        try
        {
            var action = () => start;
            await action.Should().ThrowAsync<TimeoutException>();
            var invalidated = service.Generation;
            capture.ReleaseCount.Should().BeGreaterThan(0);
            release.Set();
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            await WaitForCaptureQuiescenceAsync(service);
            capture.Transcript("late");
            service.Generation.Should().Be(invalidated);
            service.IsListening.Should().BeFalse();
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task Capture_state_reports_empty_expiry_without_reenabling_or_emitting_a_command()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var capture = new FakeCapture();
        factory.OpenResult.SetResult(capture);
        var limits = VoiceCaptureLimits.Default with { EmptySpeechDeadline = TimeSpan.FromMilliseconds(100) };
        await using var service = Create(privacy, factory, limits);
        var stopped = new TaskCompletionSource<VoiceCaptureStateChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.CaptureStateChanged += (_, args) =>
        {
            if (!args.IsListening)
            {
                stopped.TrySetResult(args);
            }
        };
        await BeginCaptureAsync(service);
        var activeGeneration = service.Generation;

        var closed = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        closed.IsListening.Should().BeFalse();
        closed.Generation.Should().BeGreaterThan(activeGeneration);
        service.IsListening.Should().BeFalse();
        factory.OpenCount.Should().Be(1);
    }

    [Fact]
    public async Task A_quarantined_open_prevents_a_false_quiescence_receipt_and_another_open()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var limits = VoiceCaptureLimits.Default with { OpenDeadline = TimeSpan.FromMilliseconds(100) };
        await using var service = Create(privacy, factory, limits);
        var start = () => service.BeginPushToTalkAsync(Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken);
        await start.Should().ThrowAsync<TimeoutException>();

        service.IsCaptureQuiescent.Should().BeFalse();
        var another = () => service.BeginPushToTalkAsync(Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken);
        await another.Should().ThrowAsync<InvalidOperationException>();
        var shutdown = () => service.StopAsync(TestContext.Current.CancellationToken);
        await shutdown.Should().ThrowAsync<InvalidOperationException>();
        var late = new FakeCapture();
        factory.OpenResult.SetResult(late);
        await service.StopAsync(TestContext.Current.CancellationToken);

        service.IsCaptureQuiescent.Should().BeTrue();
        late.StartCount.Should().Be(0);
        late.ReleaseCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Stop_fails_within_the_deadline_when_detached_cleanup_holds_the_lifecycle_lock()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var capture = new FakeCapture
        {
            DisposeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        factory.OpenResult.SetResult(capture);
        var limits = VoiceCaptureLimits.Default with { OpenDeadline = TimeSpan.FromMilliseconds(100) };
        var service = Create(privacy, factory, limits);
        await BeginCaptureAsync(service);
        capture.Completed.TrySetResult();
        await capture.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        var stop = () => service.StopAsync(TestContext.Current.CancellationToken);

        (await stop.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*did not finish within its deadline*");
        service.IsCaptureQuiescent.Should().BeFalse();

        capture.DisposeGate.SetResult();
        await capture.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        var dispose = () => service.DisposeAsync().AsTask();
        await dispose.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task An_early_native_result_stops_recording_but_waits_for_the_hosts_generation_acknowledgement()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var capture = new FakeCapture();
        capture.BeforeStart = () =>
        {
            capture.Transcript("early command");
            capture.Completed.TrySetResult();
        };
        factory.OpenResult.SetResult(capture);
        await using var service = Create(privacy, factory);
        var transcripts = new List<VoiceTranscriptEventArgs>();
        var completions = new List<VoiceRecognitionCompletedEventArgs>();
        service.TranscriptRecognized += (_, args) => transcripts.Add(args);
        service.RecognitionCompleted += (_, args) => completions.Add(args);

        await service.StartAsync(Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken);
        var accepted = service.Generation;

        transcripts.Should().BeEmpty();
        completions.Should().BeEmpty();
        service.IsListening.Should().BeFalse();
        capture.ReleaseCount.Should().BeGreaterThan(0);
        service.AcceptCaptureGeneration(accepted).Should().BeTrue();
        service.AcceptCaptureGeneration(accepted).Should().BeTrue();
        transcripts.Should().ContainSingle().Which.Generation.Should().Be(accepted);
        completions.Should().ContainSingle().Which.Reason.Should().Be(VoiceRecognitionCompletionReason.Recognized);
        service.Generation.Should().Be(accepted);
    }

    [Fact]
    public async Task Privacy_invalidation_discards_an_early_result_before_the_host_can_acknowledge_it()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        var capture = new FakeCapture();
        capture.BeforeStart = () => capture.Transcript("early command");
        factory.OpenResult.SetResult(capture);
        await using var service = Create(privacy, factory);
        var transcripts = new List<VoiceTranscriptEventArgs>();
        var completions = new List<VoiceRecognitionCompletedEventArgs>();
        service.TranscriptRecognized += (_, args) => transcripts.Add(args);
        service.RecognitionCompleted += (_, args) => completions.Add(args);
        await service.StartAsync(Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken);
        var generation = service.Generation;

        privacy.Set(Ready with { SessionState = WindowsSessionState.Locked });

        service.AcceptCaptureGeneration(generation).Should().BeFalse();
        transcripts.Should().BeEmpty();
        completions.Should().ContainSingle().Which.Generation.Should().Be(generation);
        completions[0].Reason.Should().Be(VoiceRecognitionCompletionReason.Failed);
    }

    [Fact]
    public async Task Empty_completion_has_a_retired_generation_and_is_not_a_recognition_failure()
    {
        using var privacy = new FakePrivacy();
        var factory = new FakeFactory();
        factory.OpenResult.SetResult(new FakeCapture());
        var limits = VoiceCaptureLimits.Default with { EmptySpeechDeadline = TimeSpan.FromMilliseconds(100) };
        await using var service = Create(privacy, factory, limits);
        var completion = new TaskCompletionSource<VoiceRecognitionCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new List<VoiceRecognitionFailureEventArgs>();
        service.RecognitionCompleted += (_, args) => completion.TrySetResult(args);
        service.RecognitionFailed += (_, args) => failures.Add(args);
        await BeginCaptureAsync(service);
        var generation = service.Generation;

        var finished = await completion.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        finished.Generation.Should().Be(generation);
        finished.Reason.Should().Be(VoiceRecognitionCompletionReason.EmptySpeechTimeout);
        service.Generation.Should().BeGreaterThan(generation);
        service.IsListening.Should().BeFalse();
        failures.Should().BeEmpty();
    }

    private static async Task BeginCaptureAsync(WindowsVoiceRecognitionService service)
    {
        await service.BeginPushToTalkAsync(
            Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken);
        service.AcceptCaptureGeneration(service.Generation).Should().BeTrue();
    }

    private static async Task WaitForCaptureQuiescenceAsync(WindowsVoiceRecognitionService service)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        while (!service.IsCaptureQuiescent)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private WindowsVoiceRecognitionService Create(
        FakePrivacy privacy, FakeFactory factory, VoiceCaptureLimits? limits = null) =>
        new(Logger, privacy, factory, limits ?? VoiceCaptureLimits.Default);

    private static WindowsPrivacySnapshot Ready => new(
        WindowsSessionState.Unlocked, MicrophoneAccessState.Allowed, 1, ["mic"], "mic", "speaker");

    private sealed class ObserverSource : IWindowsPrivacySource
    {
        public event EventHandler<WindowsPrivacySignalEventArgs>? Changed;
        public WindowsPrivacySnapshot Snapshot { get; set; } = Ready;
        public Action? BeforeRead { get; set; }

        public WindowsPrivacySnapshot Read()
        {
            BeforeRead?.Invoke();
            return Snapshot;
        }

        public void Notify(WindowsSessionState? state = null, bool topology = false) =>
            Changed?.Invoke(this, new WindowsPrivacySignalEventArgs(state, topology));

        public void Dispose()
        {
        }
    }

    private sealed class FakePrivacy : IWindowsPrivacyObservationService
    {
        public event EventHandler<WindowsPrivacyChangedEventArgs>? Changed;
        public WindowsPrivacySnapshot Current { get; set; } = Ready;
        public Action? BeforeRefresh { get; set; }
        public TaskCompletionSource Refreshed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public WindowsPrivacySnapshot Refresh()
        {
            BeforeRefresh?.Invoke();
            Refreshed.TrySetResult();
            return Current;
        }

        public void Set(WindowsPrivacySnapshot snapshot)
        {
            var previous = Current;
            Current = snapshot;
            Changed?.Invoke(this, new WindowsPrivacyChangedEventArgs(previous, snapshot));
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeFactory : IActivatedCaptureFactory
    {
        public TaskCompletionSource<IActivatedCapture> OpenResult { get; set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int OpenCount { get; private set; }
        public BlockingAudioStream? Stream { get; private set; }
        public MicrophoneDevice? SelectedMicrophone { get; private set; }
        public IReadOnlyList<string> Phrases { get; private set; } = [];

        public Task<IActivatedCapture> OpenAsync(
            MicrophoneDevice microphone, IReadOnlyList<string> phrases, BlockingAudioStream stream, Func<bool> canOpen)
        {
            if (!canOpen())
            {
                throw new OperationCanceledException();
            }

            OpenCount++;
            SelectedMicrophone = microphone;
            Phrases = phrases;
            Stream = stream;
            Entered.TrySetResult();
            return OpenResult.Task;
        }
    }

    private sealed class FakeCapture : IActivatedCapture
    {
        public event ActivatedAudioAvailable? DataAvailable;
        public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;
        public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed;
        public event EventHandler? SpeechDetected;
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DisposeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? DisposeGate { get; set; }
        public bool CompleteOnFinish { get; set; } = true;
        public int StartCount { get; private set; }
        public int ReleaseCount { get; private set; }
        public Action? BeforeStart { get; set; }
        public Task Completion => Completed.Task;

        public void Start()
        {
            BeforeStart?.Invoke();
            StartCount++;
        }

        public Task ReleaseRecorder()
        {
            ReleaseCount++;
            Released.TrySetResult();
            return Task.CompletedTask;
        }

        public void FinishRecognition(bool cancel)
        {
            if (CompleteOnFinish || cancel)
            {
                Completed.TrySetResult();
            }
        }

        public async ValueTask DisposeAsync()
        {
            DisposeEntered.TrySetResult();
            if (DisposeGate is not null)
            {
#pragma warning disable VSTHRD003 // Synthetic gate has no synchronization-context dependency.
                await DisposeGate.Task;
#pragma warning restore VSTHRD003
            }

            Completed.TrySetResult();
            Disposed.TrySetResult();
        }

        public void Audio(byte[] bytes) => DataAvailable?.Invoke(this, bytes);
        public void Transcript(string text, object? sender = null)
        {
#pragma warning disable MA0091 // Deliberately inject a wrong sender to exercise the identity gate.
            TranscriptRecognized?.Invoke(sender ?? this, new VoiceTranscriptEventArgs(text, 0.9f));
#pragma warning restore MA0091
        }

        public Action QueueTranscript(string text)
        {
            var callback = TranscriptRecognized;
            return () => callback?.Invoke(this, new VoiceTranscriptEventArgs(text, 0.9f));
        }

        public Action QueueAudio(byte[] bytes)
        {
            var callback = DataAvailable;
            return () => callback?.Invoke(this, bytes);
        }

        public void DetectSpeech() => SpeechDetected?.Invoke(this, EventArgs.Empty);
        public void Fail() => RecognitionFailed?.Invoke(this, new VoiceRecognitionFailureEventArgs("synthetic"));
    }
}