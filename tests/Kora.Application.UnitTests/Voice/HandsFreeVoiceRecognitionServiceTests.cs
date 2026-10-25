using System.Threading.Channels;
using AwesomeAssertions;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Voice;

[Collection("Host tracing")]
public sealed partial class HandsFreeVoiceRecognitionServiceTests
{
    [Fact]
    public async Task Wake_only_does_not_transcribe_and_one_final_waits_for_joined_cleanup_and_operation_before_rearm()
    {
        var stage = new Stage();
        var native = new Activated();
        await using var voice = Create(native, stage);
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new TaskCompletionSource<HostRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        long activatedGeneration = 0;
        voice.Bind(() => Admission, async (result, request) =>
        {
            result.Transcript.Should().Be("open settings");
            request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
            voice.IsCaptureQuiescent.Should().BeTrue();
            Interlocked.Increment(ref calls);
            delivered.SetResult(request);
            await operation.Task;
        }, static () => true, generation => activatedGeneration = generation);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var first = await stage.NextAsync();
        voice.State.Should().Be(HandsFreeVoiceState.WakeListening);
        voice.IsListening.Should().BeTrue();
        first.CaptureCalls.Should().Be(0);
        native.PushToTalkCalls.Should().Be(0);
        voice.CaptureGeneration.Should().Be(first.Binding.Generation);
        first.HoldClose = true;
        first.Wake.SetResult(new(first.Binding.Generation, 3, true));
        await first.CaptureStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        activatedGeneration.Should().Be(first.Binding.Generation, "routing context is bound before transcription");
        voice.IsListening.Should().BeTrue();
        first.Result.SetResult(new("open settings", 1, first.Binding.Generation));
        await first.CloseStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        calls.Should().Be(0);
        stage.Opens.Should().Be(1);
        voice.IsCaptureQuiescent.Should().BeFalse();
        first.ReleaseClose.SetResult();
        var request = await delivered.Task.WaitAsync(TestContext.Current.CancellationToken);
        voice.IsListening.Should().BeFalse();
        request.SessionId.Value.Should().NotBeEmpty();
        stage.Opens.Should().Be(1);
        operation.SetResult();
        var second = await stage.NextAsync();
        second.Binding.Generation.Should().NotBe(first.Binding.Generation);
        calls.Should().Be(1);
        await voice.StopAsync(TestContext.Current.CancellationToken);
        await voice.Completion;
        voice.State.Should().Be(HandsFreeVoiceState.Stopped);
        voice.IsCaptureQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("consent")]
    [InlineData("name")]
    [InlineData("profile")]
    [InlineData("device")]
    [InlineData("call")]
    [InlineData("owner")]
    [InlineData("lock")]
    public async Task Fresh_policy_loss_invalidates_audio_and_rejects_late_wake_and_transcript(string change)
    {
        var stage = new Stage();
        await using var voice = Create(new(), stage);
        HandsFreeVoiceAdmission? current = Admission;
        var calls = 0;
        voice.Bind(() => current, (_, _) => { calls++; return Task.CompletedTask; }, static () => true);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        current = change switch
        {
            "name" => Admission with { AssistantName = "Nova" },
            "profile" => Admission with { Profile = Admission.Profile with { Number = 2 } },
            "device" => Admission with { TopologyRevision = 2 },
            "call" => Admission with { CallRevision = 2 },
            "owner" => Admission with { OwnerRevision = 2 },
            _ => null,
        };
        voice.Revalidate();
        opened.Invalidated.Should().BeTrue();
        opened.Wake.TrySetResult(new(opened.Binding.Generation, 3, true));
        opened.Result.TrySetResult(new("delete", 1, opened.Binding.Generation));
        await voice.Completion;
        calls.Should().Be(0);
        stage.Opens.Should().Be(1);
        voice.IsCaptureQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("stale-wake")]
    [InlineData("negative-boundary")]
    [InlineData("playback")]
    [InlineData("stale-result")]
    [InlineData("empty")]
    [InlineData("large")]
    [InlineData("confidence")]
    [InlineData("nan")]
    public async Task Untrusted_or_unqualified_observation_fails_closed_and_requires_explicit_recovery(string invalid)
    {
        var stage = new Stage();
        await using var voice = Create(new(), stage);
        var calls = 0;
        voice.Bind(() => Admission, (_, _) => { calls++; return Task.CompletedTask; }, static () => true);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        var generation = opened.Binding.Generation;
        opened.Wake.SetResult(new(invalid is "stale-wake" ? generation - 1 : generation,
            invalid is "negative-boundary" ? -1 : 3, invalid is not "playback"));
        opened.Result.TrySetResult(new(invalid is "empty" ? " " : invalid is "large" ? new string('x', 4097) : "help",
            invalid is "confidence" ? -1 : invalid is "nan" ? float.NaN : 1,
            invalid is "stale-result" ? generation - 1 : generation));
        await voice.Completion;
        calls.Should().Be(0);
        voice.State.Should().Be(HandsFreeVoiceState.RecoveryRequired);
        var implicitRecovery = () => voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        await implicitRecovery.Should().ThrowAsync<InvalidOperationException>();
        await voice.StartHandsFreeAsync(explicitRecovery: true, TestContext.Current.CancellationToken);
        await stage.NextAsync();
        await voice.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Governed_stop_does_not_wait_on_itself_or_rearm_or_replay()
    {
        var stage = new Stage();
        await using var voice = Create(new(), stage);
        voice.Bind(() => Admission, async (_, _) => await voice.StopAsync(TestContext.Current.CancellationToken), static () => true);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        opened.Wake.SetResult(new(opened.Binding.Generation, 2, true));
        opened.Result.SetResult(new("disable listening", 1, opened.Binding.Generation));
        await voice.Completion;
        stage.Opens.Should().Be(1);
        voice.State.Should().Be(HandsFreeVoiceState.Stopped);
    }

    [Fact]
    public async Task Empty_activation_expires_locally_and_rearms_without_dispatch_or_context()
    {
        var stage = new Stage();
        await using var voice = Create(new(), stage);
        var calls = 0;
        voice.Bind(() => Admission, (_, _) => { calls++; return Task.CompletedTask; }, static () => true);
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        opened.Wake.SetResult(new(opened.Binding.Generation, 2, true));
        opened.Result.SetResult(null);
        await stage.NextAsync();
        calls.Should().Be(0);
        await voice.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Unavailable_production_boundary_never_opens_audio_and_manual_capture_remains_a_fallback()
    {
        var stage = new Stage { IsAvailable = false };
        var native = new Activated();
        await using var voice = Create(native, stage);
        voice.State.Should().Be(HandsFreeVoiceState.Unavailable);
        voice.IsAmbientListeningAvailable.Should().BeFalse();
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        stage.Opens.Should().Be(0);
        await voice.BeginPushToTalkAsync(Admission.Microphone, ["help"], "Kora", TestContext.Current.CancellationToken);
        voice.Generation.Should().Be(native.Generation);
        voice.IsListening.Should().BeTrue();
        voice.IsCaptureQuiescent.Should().BeFalse();
        voice.AcceptCaptureGeneration(native.Generation).Should().BeTrue();
        await voice.EndPushToTalkAsync(TestContext.Current.CancellationToken);
        await voice.BeginReplyAsync(Admission.Microphone, ["Yes", "No"], static () => true, TestContext.Current.CancellationToken);
        native.ReplyCalls.Should().Be(1);
        voice.GetMicrophones().Should().ContainSingle();
        voice.GetDefaultMicrophone().Should().Be(Admission.Microphone);
        await voice.EndCaptureAsync(TestContext.Current.CancellationToken);
        await voice.StartAsync(Admission.Microphone, ["help"], cancellationToken: TestContext.Current.CancellationToken);
        native.PushToTalkCalls.Should().Be(2);
    }

    [Fact]
    public async Task Host_question_can_pause_only_wake_and_join_capture_before_prefix_free_reply()
    {
        var stage = new Stage();
        var native = new Activated();
        await using var voice = Create(native, stage);
        voice.Bind(() => Admission, (_, _) => Task.CompletedTask, static () => true);
        await voice.PauseWakeForReplyAsync();
        await voice.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        var opened = await stage.NextAsync();
        await voice.PauseWakeForReplyAsync();
        opened.IsQuiescent.Should().BeTrue();
        await voice.BeginReplyAsync(Admission.Microphone, ["Yes"], static () => true, TestContext.Current.CancellationToken);
        native.ReplyCalls.Should().Be(1);
        stage.Opens.Should().Be(1);
    }

    private static HandsFreeVoiceRecognitionService Create(Activated native, Stage stage) =>
        new(native, stage, NullLogger<HandsFreeVoiceRecognitionService>.Instance);

    private static HandsFreeVoiceAdmission Admission { get; } =
        new(new("mic", "Microphone"), "Kora", new(1, SpeechInputSelection.Default), 1, 1, 1, 1);

    private sealed class Stage : IHandsFreeAudioStage
    {
        private readonly Channel<AudioSession> openings = Channel.CreateUnbounded<AudioSession>();
        public bool IsAvailable { get; init; } = true;
        public int Opens { get; private set; }
        public Exception? OpenFailure { get; init; }
        public bool HoldOpen { get; init; }
        public TaskCompletionSource ReleaseOpen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IHandsFreeAudioSession> OpenAsync(HandsFreeAudioBinding binding, CancellationToken cancellationToken)
        {
            Opens++;
            if (OpenFailure is not null) { throw OpenFailure; }
            var session = new AudioSession(binding);
            openings.Writer.TryWrite(session).Should().BeTrue();
            if (HoldOpen) { await ReleaseOpen.Task.WaitAsync(TestContext.Current.CancellationToken); }
            return session;
        }
        public async Task<AudioSession> NextAsync() => await openings.Reader.ReadAsync(TestContext.Current.CancellationToken);
    }

    private sealed class AudioSession(HandsFreeAudioBinding binding) : IHandsFreeAudioSession
    {
        public HandsFreeAudioBinding Binding { get; } = binding;
        public TaskCompletionSource<WakeWordObservation> Wake { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<VoiceTranscriptEventArgs?> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CaptureStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CloseStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseClose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldClose { get; set; }
        public bool ConfirmQuiescence { get; set; } = true;
        public bool FailClose { get; set; }
        public bool IsQuiescent { get; private set; }
        public bool Invalidated { get; private set; }
        public int CaptureCalls { get; private set; }
        public Task<WakeWordObservation> WaitForWakeAsync(CancellationToken cancellationToken) => Wake.Task.WaitAsync(cancellationToken);
        public async Task<VoiceTranscriptEventArgs?> CaptureCommandAsync(WakeWordObservation wake, CancellationToken cancellationToken)
        {
            CaptureCalls++;
            CaptureStarted.TrySetResult();
            return await Result.Task.WaitAsync(cancellationToken);
        }
        public void Invalidate() => Invalidated = true;
        public async ValueTask DisposeAsync()
        {
            CloseStarted.TrySetResult();
            if (HoldClose) { await ReleaseClose.Task.WaitAsync(TestContext.Current.CancellationToken); }
            if (FailClose) { throw new InvalidOperationException("Capture release failed."); }
            IsQuiescent = ConfirmQuiescence;
        }
    }

    private sealed class Activated : IQuestionVoiceRecognitionService, ISpeechInputProfileRuntime
    {
        public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized { add { } remove { } }
        public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed { add { } remove { } }
        public event EventHandler<VoiceCaptureStateChangedEventArgs>? CaptureStateChanged { add { } remove { } }
        public event EventHandler<VoiceRecognitionCompletedEventArgs>? RecognitionCompleted { add { } remove { } }
        public event EventHandler<VoiceCaptureStateChangedEventArgs>? ReplySpeechStarted { add { } remove { } }
        public bool IsAmbientListeningAvailable => false;
        public bool IsCaptureQuiescent => !IsListening;
        public bool IsListening { get; private set; }
        public long Generation { get; private set; }
        public long CaptureGeneration => Generation;
        public int ReplyCalls { get; private set; }
        public int PushToTalkCalls { get; private set; }
        public SpeechInputGeneration ProfileGeneration { get; private set; } = new(1, SpeechInputSelection.Default);
        public bool IsProfileAvailable { get; private set; } = true;
        public int ProfileRetireCalls { get; private set; }
        public void InitializeProfile(SpeechInputSelection? selection) => IsProfileAvailable = selection is not null;
        public Task<ISpeechInputProfileSwitch> RetireProfileAsync(CancellationToken cancellationToken)
        {
            IsCaptureQuiescent.Should().BeTrue();
            ProfileRetireCalls++;
            IsProfileAvailable = false;
            return Task.FromResult<ISpeechInputProfileSwitch>(new ProfileSwitch(this));
        }
        private sealed class ProfileSwitch(Activated owner) : ISpeechInputProfileSwitch
        {
            public void Publish(SpeechInputGeneration generation) { owner.ProfileGeneration = generation; owner.IsProfileAvailable = true; }
            public void HoldUnavailable() => owner.IsProfileAvailable = false;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
        public bool AcceptCaptureGeneration(long generation) => generation == Generation;
        public IReadOnlyList<MicrophoneDevice> GetMicrophones() => [Admission.Microphone];
        public MicrophoneDevice? GetDefaultMicrophone() => Admission.Microphone;
        public void InvalidateCapture() { Generation++; IsListening = false; }
        public Task StopAsync(CancellationToken cancellationToken = default) { IsListening = false; return Task.CompletedTask; }
        public Task EndCaptureAsync(CancellationToken cancellationToken = default) => StopAsync(cancellationToken);
        public Task EndPushToTalkAsync(CancellationToken cancellationToken = default) => StopAsync(cancellationToken);
        public Task StartAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, string? assistantName = null,
            CancellationToken cancellationToken = default) => BeginPushToTalkAsync(microphone, phrases, assistantName, cancellationToken);
        public Task BeginPushToTalkAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, string? assistantName = null,
            CancellationToken cancellationToken = default)
        {
            PushToTalkCalls++; Generation++; IsListening = true; return Task.CompletedTask;
        }
        public Task BeginReplyAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, Func<bool> isAdmitted,
            CancellationToken cancellationToken)
        {
            ReplyCalls++; Generation++; IsListening = isAdmitted(); return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
