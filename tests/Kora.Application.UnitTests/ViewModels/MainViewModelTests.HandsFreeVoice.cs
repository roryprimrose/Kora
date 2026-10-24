using System.Threading.Channels;
using System.Reflection;
using AwesomeAssertions;
using Kora.Application.ViewModels;
using Kora.Application.Voice;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task Configured_name_wake_completes_existing_operation_without_push_to_talk_and_rearms_only_afterward()
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var fixture = CreateHandsFreeFixture(stage, voice => decorated = voice);
        var voice = decorated!;
        await using var lifetime = voice;
        await fixture.ViewModel.InitializeAsync();
        var first = await stage.NextAsync();
        fixture.ViewModel.CanPushToTalk.Should().BeTrue();
        first.Binding.AssistantName.Should().Be("Kora");
        fixture.ViewModel.ListeningStatus.Should().Be("Wake listening - local name detection only");
        fixture.ViewModel.TrayInputStatus.Should().Be("Wake listening - local name detection only");
        fixture.Voice.StartCalls.Should().Be(0);
        var completingStatus = string.Empty;
        EventHandler observe = (_, _) =>
        {
            if (voice.State == HandsFreeVoiceState.CompletingOperation)
            {
                completingStatus = fixture.ViewModel.ListeningStatus;
            }
        };
        voice.Changed += observe;
        first.Wake.SetResult(new(first.Binding.Generation, 12, true));
        await first.CaptureStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        fixture.ViewModel.ListeningStatus.Should().Be("Capturing command - bounded local transcription");
        first.Result.SetResult(new("what version are you running", 1, first.Binding.Generation));
        var second = await stage.NextAsync();
        fixture.ViewModel.ResponseTitle.Should().Be("Kora version");
        completingStatus.Should().Be("Microphone closed - completing activated command");
        voice.Changed -= observe;
        fixture.Reasoner.Requests.Should().BeEmpty();
        first.IsQuiescent.Should().BeTrue();
        second.Binding.Generation.Should().NotBe(first.Binding.Generation);
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        await voice.Completion;
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        voice.IsCaptureQuiescent.Should().BeTrue();
        fixture.ViewModel.ListeningStatus.Should().NotContain("Wake listening");
        fixture.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("not-bound")]
    [InlineData("no-profile")]
    [InlineData("disabled")]
    [InlineData("host")]
    [InlineData("ownership")]
    [InlineData("catalog")]
    [InlineData("topology")]
    [InlineData("consent")]
    public async Task HandsFree_admission_requires_each_fresh_host_owned_gate(string missing)
    {
        var stage = new HandsFreeStage { IsAvailable = false };
        HandsFreeVoiceRecognitionService? decorated = null;
        var fixture = CreateHandsFreeFixture(stage, value => decorated = value, enableProfile: missing is not "no-profile");
        await using var voice = decorated!;
        await fixture.ViewModel.InitializeAsync();
        if (missing is not "no-profile")
        {
            var field = missing switch
            {
                "not-bound" => "handsFreeVoice",
                "disabled" => "voiceEnabled",
                "host" => "privacyPresentationHeld",
                "catalog" => "microphoneCatalogCurrent",
                "topology" => "catalogPrivacyRevision",
                "consent" => "voiceConsent",
                _ => null,
            };
            if (field is not null)
            {
                var target = typeof(MainViewModel).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!;
                target.SetValue(fixture.ViewModel, missing switch
                {
                    "not-bound" => null,
                    "disabled" => 0,
                    "host" => 1,
                    "topology" => -1L,
                    _ => false,
                });
            }
            else { fixture.ViewModel.BindVoiceOwnershipGate(static () => false); }
        }
        var observe = typeof(MainViewModel).GetMethod("ObserveHandsFreeVoiceAdmission", BindingFlags.Instance | BindingFlags.NonPublic)!;
        observe.Invoke(fixture.ViewModel, null).Should().BeNull();
        stage.OpenCount.Should().Be(0);
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task HandsFree_manual_fallback_preserves_native_generation_and_can_resume_only_after_its_command()
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var fixture = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        await fixture.ViewModel.InitializeAsync();
        var wake = await stage.NextAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        wake.IsQuiescent.Should().BeTrue();
        fixture.ViewModel.ListeningStatus.Should().Contain("Recording");
        fixture.ViewModel.CanPushToTalk.Should().BeFalse();
        await fixture.ViewModel.EndPushToTalkAsync();
        await fixture.Voice.RaiseTranscriptAsync("what version are you running", 1);
        await stage.NextAsync();
        fixture.ViewModel.ResponseTitle.Should().Be("Kora version");
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task HandsFree_manual_transition_cannot_open_native_capture_when_wake_cleanup_fails()
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var fixture = CreateHandsFreeFixture(stage, value => decorated = value);
        var voice = decorated!;
        await fixture.ViewModel.InitializeAsync();
        var wake = await stage.NextAsync();
        wake.FailClose = true;
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.Voice.StartCalls.Should().Be(0);
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Microphone cleanup needs attention.");
        voice.IsCaptureQuiescent.Should().BeFalse();
        fixture.ViewModel.Dispose();
        var dispose = async () => await voice.DisposeAsync();
        await dispose.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task HandsFree_manual_input_is_unavailable_until_an_explicit_stop_has_joined_capture()
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var fixture = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        await fixture.ViewModel.InitializeAsync();
        var wake = await stage.NextAsync();
        wake.HoldClose = true;
        var stop = fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        await wake.Closing.Task.WaitAsync(TestContext.Current.CancellationToken);
        fixture.ViewModel.CanPushToTalk.Should().BeFalse();
        wake.ReleaseClose.SetResult();
        await stop;
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task HandsFree_unknown_call_cannot_become_ambient_activation_even_when_call_listening_preference_allows()
    {
        var stage = new HandsFreeStage { IsAvailable = false };
        HandsFreeVoiceRecognitionService? decorated = null;
        var fixture = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.AllowVoiceActivationDuringCalls.Should().BeTrue();
        fixture.CallState.SetState(Kora.Core.Communication.CallState.Unknown);
        var observe = typeof(MainViewModel).GetMethod("ObserveHandsFreeVoiceAdmission", BindingFlags.Instance | BindingFlags.NonPublic)!;
        observe.Invoke(fixture.ViewModel, null).Should().BeNull();
        stage.OpenCount.Should().Be(0);
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task HandsFree_retired_dispatch_callback_rejects_foreign_generation_and_lost_policy()
    {
        var stage = new HandsFreeStage { IsAvailable = false };
        HandsFreeVoiceRecognitionService? decorated = null;
        var fixture = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        await fixture.ViewModel.InitializeAsync();
        // Supplement live lifecycle tests by exercising a callback already queued at the host boundary.
        var callback = (Func<VoiceTranscriptEventArgs, Kora.Core.Hosting.HostRequest, Task>)
            typeof(HandsFreeVoiceRecognitionService).GetField("dispatch", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(voice)!;
        var request = Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.ActivatedVoice);
        var foreign = () => callback(new("help", 1, voice.Generation - 1), request);
        await foreign.Should().ThrowAsync<InvalidOperationException>();
        fixture.ViewModel.BindVoiceOwnershipGate(static () => false);
        var revoked = () => callback(new("help", 1, voice.Generation), request);
        await revoked.Should().ThrowAsync<InvalidOperationException>();
        fixture.ViewModel.Dispose();
        var changed = typeof(MainViewModel).GetMethod("OnHandsFreeVoiceStateChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
        changed.Invoke(fixture.ViewModel, [voice, EventArgs.Empty]);
        fixture.Voice.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task Rebinding_ownership_retires_wake_without_replaying_the_old_command()
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var fixture = CreateHandsFreeFixture(stage, voice => decorated = voice);
        await using var voice = decorated!;
        await fixture.ViewModel.InitializeAsync();
        var old = await stage.NextAsync();
        fixture.ViewModel.BindVoiceOwnershipGate(static () => false);
        await voice.Completion;
        old.Invalidated.Should().BeTrue();
        old.Wake.TrySetResult(new(old.Binding.Generation, 12, true));
        old.Result.TrySetResult(new("what version are you running", 1, old.Binding.Generation));
        fixture.ViewModel.ResponseTitle.Should().NotBe("Kora version");
        stage.OpenCount.Should().Be(1);
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task Failure_is_visible_and_disposal_retires_callbacks_and_closes_owned_input()
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var fixture = CreateHandsFreeFixture(stage, voice => decorated = voice);
        await using var voice = decorated!;
        await fixture.ViewModel.InitializeAsync();
        var first = await stage.NextAsync();
        first.Wake.SetResult(new(first.Binding.Generation, 12, false));
        await voice.Completion;
        fixture.ViewModel.ResponseTitle.Should().Be("Hands-free voice recovery required.");
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.ListeningStatus.Should().Contain("recovery required");
        fixture.ViewModel.Dispose();
        voice.RequireExplicitRecovery();
        fixture.ViewModel.ResponseTitle.Should().Be("Hands-free voice recovery required.");
        first.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task Unqualified_adapter_keeps_existing_fallback_truthful_and_does_not_open_audio()
    {
        var stage = new HandsFreeStage { IsAvailable = false };
        HandsFreeVoiceRecognitionService? decorated = null;
        var fixture = CreateHandsFreeFixture(stage, voice => decorated = voice);
        await using var voice = decorated!;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
        fixture.ViewModel.ListeningStatus.Should().Contain("Push-to-talk ready");
        fixture.ViewModel.TrayInputStatus.Should().Contain("wake unavailable");
        fixture.ViewModel.BindVoiceOwnershipGate(static () => true);
        stage.OpenCount.Should().Be(0);
        fixture.ViewModel.Dispose();
    }

    private static Fixture CreateHandsFreeFixture(IHandsFreeAudioStage stage,
        Action<HandsFreeVoiceRecognitionService> created, bool enableProfile = true)
    {
        var fixture = new Fixture(enableSpeechInput: enableProfile, voiceDecorator: native =>
        {
            var voice = new HandsFreeVoiceRecognitionService(native, stage,
                NullLogger<HandsFreeVoiceRecognitionService>.Instance);
            created(voice);
            return voice;
        });
        fixture.VoiceConsent.Consent = true;
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        return fixture;
    }

    private sealed class HandsFreeStage : IHandsFreeAudioStage
    {
        private readonly Channel<HandsFreeSession> openings = Channel.CreateUnbounded<HandsFreeSession>();
        public bool IsAvailable { get; init; } = true;
        public int OpenCount { get; private set; }
        public Task<IHandsFreeAudioSession> OpenAsync(HandsFreeAudioBinding binding, CancellationToken cancellationToken)
        {
            OpenCount++;
            var opened = new HandsFreeSession(binding);
            openings.Writer.TryWrite(opened).Should().BeTrue();
            return Task.FromResult<IHandsFreeAudioSession>(opened);
        }
        public async Task<HandsFreeSession> NextAsync() => await openings.Reader.ReadAsync(TestContext.Current.CancellationToken);
    }

    private sealed class HandsFreeSession(HandsFreeAudioBinding binding) : IHandsFreeAudioSession
    {
        public HandsFreeAudioBinding Binding { get; } = binding;
        public TaskCompletionSource<WakeWordObservation> Wake { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<VoiceTranscriptEventArgs?> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CaptureStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Invalidated { get; private set; }
        public bool FailClose { get; set; }
        public bool HoldClose { get; set; }
        public TaskCompletionSource Closing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseClose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsQuiescent { get; private set; }
        public Task<WakeWordObservation> WaitForWakeAsync(CancellationToken cancellationToken) => Wake.Task.WaitAsync(cancellationToken);
        public Task<VoiceTranscriptEventArgs?> CaptureCommandAsync(WakeWordObservation wake, CancellationToken cancellationToken)
        {
            CaptureStarted.TrySetResult();
            return Result.Task.WaitAsync(cancellationToken);
        }
        public void Invalidate() => Invalidated = true;
        public async ValueTask DisposeAsync()
        {
            Closing.TrySetResult();
            if (HoldClose) { await ReleaseClose.Task.WaitAsync(TestContext.Current.CancellationToken); }
            if (FailClose) { throw new InvalidOperationException("Wake release failed."); }
            IsQuiescent = true;
        }
    }
}
