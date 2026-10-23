using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Voice;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Voice;

public sealed partial class HandsFreeVoiceRecognitionServiceTests
{
    [Fact]
    public async Task Binding_and_concurrent_capture_have_one_explicit_owner()
    {
        var stage = new Stage();
        var native = new Activated();
        var voice = Create(native, stage);
        voice.Revalidate();
        await voice.ResumeAfterActivatedInputAsync();
        var unbound = () => voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        await unbound.Should().ThrowAsync<InvalidOperationException>();
        voice.Bind(() => Admission, (_, _) => Task.CompletedTask, static () => true);
        var duplicate = () => voice.Bind(() => Admission, (_, _) => Task.CompletedTask, static () => true);
        duplicate.Should().Throw<InvalidOperationException>();
        await native.BeginPushToTalkAsync(Admission.Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken);
        var competing = () => voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        await competing.Should().ThrowAsync<InvalidOperationException>();
        await native.StopAsync(TestContext.Current.CancellationToken);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        await stage.NextAsync();
        voice.Revalidate();
        await competing.Should().ThrowAsync<InvalidOperationException>();
        await voice.ResumeAfterActivatedInputAsync();
        stage.Opens.Should().Be(1);
        await voice.DisposeAsync();
        await voice.DisposeAsync();
        await voice.ResumeAfterActivatedInputAsync();
        var disposedBind = () => voice.Bind(() => Admission, (_, _) => Task.CompletedTask, static () => true);
        disposedBind.Should().Throw<ObjectDisposedException>();
        await competing.Should().ThrowAsync<ObjectDisposedException>();
        voice.IsProfileAvailable.Should().BeFalse();
        var manual = () => voice.BeginPushToTalkAsync(Admission.Microphone, ["help"],
            cancellationToken: TestContext.Current.CancellationToken);
        await manual.Should().ThrowAsync<ObjectDisposedException>();
        var reply = () => voice.BeginReplyAsync(Admission.Microphone, ["Yes"], static () => true, TestContext.Current.CancellationToken);
        await reply.Should().ThrowAsync<ObjectDisposedException>();
        var initialize = () => voice.InitializeProfile(SpeechInputSelection.Default);
        initialize.Should().Throw<ObjectDisposedException>();
        var profile = () => voice.RetireProfileAsync(TestContext.Current.CancellationToken);
        await profile.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Theory]
    [InlineData("missing-policy")]
    [InlineData("playback")]
    [InlineData("open-failure")]
    public async Task Missing_policy_playback_and_native_open_failure_never_dispatch(string cause)
    {
        var stage = new Stage { OpenFailure = cause is "open-failure" ? new InvalidOperationException("open failed") : null };
        await using var voice = Create(new(), stage);
        voice.Bind(() => cause is "missing-policy" ? null : Admission,
            (_, _) => throw new InvalidOperationException("No dispatch allowed."), () => cause is not "playback");
        var start = () => voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        await start.Should().ThrowAsync<InvalidOperationException>();
        await voice.Completion;
        voice.State.Should().Be(HandsFreeVoiceState.RecoveryRequired);
        voice.IsCaptureQuiescent.Should().BeTrue();
        await voice.ResumeAfterActivatedInputAsync();
        stage.Opens.Should().Be(cause is "open-failure" ? 1 : 0);
    }

    [Fact]
    public async Task Stop_during_native_open_joins_late_resource_and_never_accepts_wake()
    {
        var stage = new Stage { HoldOpen = true };
        await using var voice = Create(new(), stage);
        voice.Bind(() => Admission, (_, _) => throw new InvalidOperationException("No dispatch allowed."), static () => true);
        var start = voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var late = await stage.NextAsync();
        var stop = voice.StopAsync(TestContext.Current.CancellationToken);
        stop.IsCompleted.Should().BeFalse();
        voice.IsCaptureQuiescent.Should().BeFalse();
        stage.ReleaseOpen.SetResult();
        await stop;
        var opened = () => start;
        await opened.Should().ThrowAsync<OperationCanceledException>();
        late.Invalidated.Should().BeTrue();
        late.IsQuiescent.Should().BeTrue();
        stage.Opens.Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unconfirmed_capture_release_blocks_dispatch_rearm_and_disposal_claims(bool failClose)
    {
        var stage = new Stage();
        var voice = Create(new(), stage);
        voice.Bind(() => Admission, (_, _) => throw new InvalidOperationException("No dispatch allowed."), static () => true);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        opened.FailClose = failClose;
        opened.ConfirmQuiescence = false;
        opened.Wake.SetResult(new(opened.Binding.Generation, 3, true));
        opened.Result.SetResult(new("help", 1, opened.Binding.Generation));
        await voice.Completion;
        voice.State.Should().Be(HandsFreeVoiceState.RecoveryRequired);
        voice.IsCaptureQuiescent.Should().BeFalse();
        await voice.ResumeAfterActivatedInputAsync();
        var start = () => voice.StartHandsFreeAsync(true, TestContext.Current.CancellationToken);
        await start.Should().ThrowAsync<InvalidOperationException>();
        var dispose = async () => await voice.DisposeAsync();
        await dispose.Should().ThrowAsync<InvalidOperationException>();
        stage.Opens.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fresh_gate_after_each_stage_rejects_a_changed_policy_even_without_event_delivery(bool afterWake)
    {
        var stage = new Stage();
        await using var voice = Create(new(), stage);
        var current = Admission;
        var calls = 0;
        voice.Bind(() => current, (_, _) => { calls++; return Task.CompletedTask; }, static () => true);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        if (afterWake)
        {
            opened.Wake.SetResult(new(opened.Binding.Generation, 3, true));
            await opened.CaptureStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        }
        current = Admission with { RecoveryRevision = 2 };
        opened.Wake.TrySetResult(new(opened.Binding.Generation, 3, true));
        opened.Result.TrySetResult(new("help", 1, opened.Binding.Generation));
        await voice.Completion;
        calls.Should().Be(0);
        voice.State.Should().Be(HandsFreeVoiceState.RecoveryRequired);
    }

    [Fact]
    public async Task Exact_host_request_and_w3c_cause_survive_dispatch_and_failed_turn_ends_truthfully()
    {
        List<Activity> ended = [];
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "Kora.Application", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => { lock (ended) { ended.Add(activity); } },
        };
        ActivitySource.AddActivityListener(listener);
        var stage = new Stage();
        await using var voice = Create(new(), stage);
        voice.Bind(() => Admission, (_, request) =>
        {
            HostActivity.RequireCurrent().Request.Should().Be(request);
            request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
            Activity.Current!.IdFormat.Should().Be(ActivityIdFormat.W3C);
            throw new InvalidOperationException("Governed operation failed.");
        }, static () => true);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        opened.Wake.SetResult(new(opened.Binding.Generation, 3, true));
        opened.Result.SetResult(new("help", 1, opened.Binding.Generation));
        await voice.Completion;
        lock (ended)
        {
            ended.Should().ContainSingle();
            ended[0].Status.Should().Be(ActivityStatusCode.Error);
            ended[0].GetTagItem("kora.outcome").Should().Be("Failed");
        }
        voice.State.Should().Be(HandsFreeVoiceState.RecoveryRequired);
    }

    [Fact]
    public async Task Native_events_remain_native_and_all_subscriptions_are_removed()
    {
        await using var voice = Create(new(), new Stage { IsAvailable = false });
        EventHandler<VoiceTranscriptEventArgs> transcript = (_, _) => { };
        EventHandler<VoiceRecognitionFailureEventArgs> failed = (_, _) => { };
        EventHandler<VoiceCaptureStateChangedEventArgs> changed = (_, _) => { };
        EventHandler<VoiceRecognitionCompletedEventArgs> completed = (_, _) => { };
        voice.TranscriptRecognized += transcript;
        voice.RecognitionFailed += failed;
        voice.CaptureStateChanged += changed;
        voice.RecognitionCompleted += completed;
        voice.ReplySpeechStarted += changed;
        voice.TranscriptRecognized -= transcript;
        voice.RecognitionFailed -= failed;
        voice.CaptureStateChanged -= changed;
        voice.RecognitionCompleted -= completed;
        voice.ReplySpeechStarted -= changed;
        await voice.ResumeAfterActivatedInputAsync();
    }

    [Fact]
    public async Task Reply_completion_rearms_wake_only_after_fresh_policy_and_manual_hold_does_not()
    {
        var stage = new Stage();
        var native = new Activated();
        await using var voice = Create(native, stage);
        HandsFreeVoiceAdmission? current = Admission;
        var canOpen = false;
        voice.Bind(() => current, (_, _) => Task.CompletedTask, () => canOpen);
        await voice.ResumeAfterActivatedInputAsync();
        canOpen = true;
        current = null;
        await voice.ResumeAfterActivatedInputAsync();
        current = Admission;
        await native.BeginReplyAsync(Admission.Microphone, ["Yes"], static () => true, TestContext.Current.CancellationToken);
        await voice.ResumeAfterActivatedInputAsync();
        stage.Opens.Should().Be(0);
        await native.StopAsync(TestContext.Current.CancellationToken);
        await voice.ResumeAfterActivatedInputAsync();
        await stage.NextAsync();
        await voice.PauseWakeForReplyAsync();
        voice.RequireExplicitRecovery();
        await voice.ResumeAfterActivatedInputAsync();
        stage.Opens.Should().Be(1);
    }

    [Fact]
    public async Task Command_duration_is_bounded_at_shared_sixty_second_limit_and_timeout_does_not_rearm()
    {
        var stage = new Stage();
        var clock = new DeadlineTimeProvider();
        var logs = new VoiceLogger();
        await using var voice = new HandsFreeVoiceRecognitionService(new Activated(), stage, logs, clock);
        var changes = 0;
        EventHandler changed = (_, _) => Interlocked.Increment(ref changes);
        voice.Changed += changed;
        voice.Bind(() => Admission, (_, _) => throw new InvalidOperationException("No dispatch allowed."), static () => true);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        opened.Wake.SetResult(new(opened.Binding.Generation, 3, true));
        await opened.CaptureStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        clock.DueTime.Should().Be(TimeSpan.FromSeconds(60));
        clock.DueTime.Should().Be(VoiceInputLimits.MaximumCommandDuration);
        clock.Expire();
        await voice.Completion;
        opened.Invalidated.Should().BeTrue();
        opened.IsQuiescent.Should().BeTrue();
        voice.State.Should().Be(HandsFreeVoiceState.RecoveryRequired);
        changes.Should().Be(3);
        logs.Events.Should().Contain(1).And.Contain(2);
        voice.Changed -= changed;
        stage.Opens.Should().Be(1);
    }

    [Fact]
    public async Task Deferred_wake_uses_links_not_retired_parent_and_each_turn_gets_its_own_host_session()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "Kora.Application", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        var stage = new Stage();
        await using var voice = Create(new(), stage);
        var requests = new List<HostRequest>();
        var commands = new List<Activity>();
        voice.Bind(() => Admission, async (_, request) =>
        {
            requests.Add(request);
            commands.Add(Activity.Current!);
            if (requests.Count == 2) { await voice.StopAsync(TestContext.Current.CancellationToken); }
        }, static () => true);
        ActivityContext original;
        using (var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Policy))
        {
            original = host.Activity!.Context;
            await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
            host.Complete(HostOperationOutcome.Completed);
        }
        var first = await stage.NextAsync();
        first.Wake.SetResult(new(first.Binding.Generation, 3, true));
        first.Result.SetResult(new("help", 1, first.Binding.Generation));
        var second = await stage.NextAsync();
        second.Wake.SetResult(new(second.Binding.Generation, 3, true));
        second.Result.SetResult(new("help", 1, second.Binding.Generation));
        await voice.Completion;
        requests.Should().HaveCount(2);
        requests[0].SessionId.Should().NotBe(requests[1].SessionId);
        commands.Should().HaveCount(2);
        commands[0].ParentSpanId.Should().Be(default(ActivitySpanId));
        commands[1].ParentSpanId.Should().Be(default(ActivitySpanId));
        commands[0].TraceId.Should().NotBe(original.TraceId);
        commands[1].TraceId.Should().NotBe(commands[0].TraceId);
        commands[0].Links.Should().ContainSingle(link => link.Context == original);
        commands[1].Links.Should().ContainSingle(link => link.Context == commands[0].Context);
        commands.Should().OnlyContain(command => command.Status == ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task Already_cancelled_start_acquires_nothing_and_does_not_become_a_recovery_grant()
    {
        var stage = new Stage();
        await using var voice = Create(new(), stage);
        voice.Bind(() => Admission, (_, _) => Task.CompletedTask, static () => true);
        var start = () => voice.StartHandsFreeAsync(cancellationToken: new CancellationToken(canceled: true));
        await start.Should().ThrowAsync<OperationCanceledException>();
        await voice.Completion;
        stage.Opens.Should().Be(0);
        voice.State.Should().Be(HandsFreeVoiceState.Stopped);
    }

    [Fact]
    public async Task Profile_switch_joins_ambient_cleanup_before_native_cutover_and_never_auto_rearms()
    {
        var stage = new Stage();
        var native = new Activated();
        await using var voice = Create(native, stage);
        voice.InitializeProfile(null);
        voice.IsProfileAvailable.Should().BeFalse();
        voice.InitializeProfile(SpeechInputSelection.Default);
        voice.IsProfileAvailable.Should().BeTrue();
        voice.ProfileGeneration.Should().Be(native.ProfileGeneration);
        voice.Bind(() => Admission, (_, _) => Task.CompletedTask, static () => true);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        opened.HoldClose = true;
        var initialize = () => voice.InitializeProfile(SpeechInputSelection.Default);
        initialize.Should().Throw<InvalidOperationException>();
        var cutover = voice.RetireProfileAsync(TestContext.Current.CancellationToken);
        await opened.CloseStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        cutover.IsCompleted.Should().BeFalse();
        native.ProfileRetireCalls.Should().Be(0);
        opened.ReleaseClose.SetResult();
        await using var lease = await cutover;
        native.ProfileRetireCalls.Should().Be(1);
        voice.Generation.Should().NotBe(opened.Binding.Generation);
        var next = new SpeechInputGeneration(2, SpeechInputSelection.Default);
        lease.Publish(next);
        voice.ProfileGeneration.Should().Be(next);
        voice.IsProfileAvailable.Should().BeTrue();
        lease.HoldUnavailable();
        voice.IsProfileAvailable.Should().BeFalse();
        voice.IsListening.Should().BeFalse();
        stage.Opens.Should().Be(1);
    }

    [Fact]
    public async Task Missing_native_profile_contract_is_explicitly_unavailable_not_a_default_profile()
    {
        var native = new Kora.Application.UnitTests.Interaction.QuestionReplyFixture.ReplyCapture();
        await using var voice = new HandsFreeVoiceRecognitionService(native, new Stage(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HandsFreeVoiceRecognitionService>.Instance);
        voice.IsProfileAvailable.Should().BeFalse();
        var generation = () => voice.ProfileGeneration;
        generation.Should().Throw<InvalidOperationException>();
        var initialize = () => voice.InitializeProfile(SpeechInputSelection.Default);
        initialize.Should().Throw<InvalidOperationException>();
        var retire = () => voice.RetireProfileAsync(TestContext.Current.CancellationToken);
        await retire.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class DeadlineTimeProvider : TimeProvider
    {
        private TimerCallback? callback;
        private object? callbackState;
        public TimeSpan DueTime { get; private set; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            this.callback = callback;
            callbackState = state;
            DueTime = dueTime;
            period.Should().Be(Timeout.InfiniteTimeSpan);
            return new DeadlineTimer();
        }
        public void Expire() => callback!(callbackState);

        private sealed class DeadlineTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class VoiceLogger : ILogger<HandsFreeVoiceRecognitionService>
    {
        public List<int> Events { get; } = [];
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Events.Add(eventId.Id);
    }
}
