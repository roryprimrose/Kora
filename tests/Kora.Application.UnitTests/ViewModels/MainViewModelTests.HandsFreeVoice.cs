using System.Threading.Channels;
using System.Reflection;
using AwesomeAssertions;
using Kora.Application.ViewModels;
using Kora.Application.Voice;
using Kora.Application.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("Nova", "version")]
    [InlineData("Nova", "request")]
    [InlineData("Nova", "new-request")]
    [InlineData("Nova", "refine")]
    [InlineData("Nova", "restore")]
    [InlineData("Nova", "rename")]
    [InlineData("Nova", "recover-rename")]
    [InlineData("Stop", "collision")]
    public async Task ReviewedCustomNameCutoverJoinsOldGenerationAndRetainsActualFinalText(string name, string operation)
    {
        var stage = new HandsFreeStage { SupportedNames = ["Nova", "Stop"] };
        HandsFreeVoiceRecognitionService? decorated = null;
        var fixture = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var configuration = fixture.InputConfiguration!;
        await using var voice = decorated!;
        await fixture.ViewModel.InitializeAsync();
        var old = await stage.NextAsync();
        old.HoldClose = true;
        var rename = fixture.ViewModel.SetAssistantNameAsync(name);
        await old.Closing.Task.WaitAsync(TestContext.Current.CancellationToken);
        fixture.NamePreferences.SavedName.Should().BeNull();
        stage.OpenCount.Should().Be(1);
        old.ReleaseClose.SetResult();
        await rename;
        old.IsQuiescent.Should().BeTrue();
        fixture.ViewModel.AssistantName.Should().Be(name);
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.SpeechInputResources.Should().Contain(SpeechInputResource.Cpu);
        fixture.ViewModel.SelectedSpeechInputProfile = SpeechInputProfileCatalogue.NativeWakeProfile;
        fixture.ViewModel.SelectedSpeechInputResource = SpeechInputResource.Cpu;
        await fixture.ViewModel.SaveSpeechInputCommand.ExecuteAsync();
        fixture.ViewModel.HasSpeechInputReview.Should().BeTrue();
        await fixture.ViewModel.ConfirmSpeechInputCommand.ExecuteAsync();
        configuration.Get().Effective!.Selection.Should().Be(SpeechInputProfileCatalogue.NativeWakeSelection);
        fixture.ViewModel.SpeechInputStatus.Should().Contain("Exact qualified local wake profile");
        stage.OpenCount.Should().Be(1);
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        var current = await stage.NextAsync();
        current.Binding.AssistantName.Should().Be(name);
        current.Binding.Profile.Selection.Should().Be(SpeechInputProfileCatalogue.NativeWakeSelection);
        current.Binding.Generation.Should().NotBe(old.Binding.Generation);
        old.Result.TrySetResult(new("what version are you running", 1, old.Binding.Generation));
        var routed = new List<string>();
        var navigated = 0;
        fixture.ViewModel.NewRequestOpeningRequested += admitted =>
        {
            if (admitted()) { navigated++; }
            return Task.FromResult(true);
        };
        Kora.Core.Hosting.RequestOrigin? routedOrigin = null;
        bool? routedAdmission = null;
        string? instructionOperation = null;
        string? instructionId = null;
        fixture.ViewModel.OwnedInstructionEditorRequested += (requested, id) =>
        {
            instructionOperation = requested;
            instructionId = id;
            routedOrigin = HostActivity.RequireCurrent().Request.Origin;
            routedAdmission = operation is "restore"
                ? fixture.ViewModel.CaptureInstructionRestoreEligibility()()
                : fixture.ViewModel.CaptureInstructionRefinementEligibility()();
            return Task.CompletedTask;
        };
        fixture.ViewModel.BindSessionRouting((text, origin, admitted) =>
        {
            routedOrigin = origin;
            routedAdmission = admitted();
            routed.Add(text);
            return Task.FromResult<Kora.Application.Hosting.SessionRouting.SessionRoutingReceipt?>(null);
        });
        current.Wake.SetResult(new(current.Binding.Generation, 12, true));
        await current.CaptureStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        var final = operation switch
        {
            "request" => "read a fixture", "new-request" => "open new request",
            "refine" => "instructions refine", "rename" => "instructions rename renamed",
            "restore" => "instructions restore",
            "recover-rename" => "instructions recover-rename",
            "collision" => "listening", _ => "what version are you running",
        };
        current.Result.SetResult(new(final, 1, current.Binding.Generation));
        if (operation is "collision")
        {
            await voice.Completion;
            fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
            stage.OpenCount.Should().Be(2);
        }
        else
        {
            await stage.NextAsync();
            if (operation is "request")
            {
                routed.Should().Equal([final], because: fixture.ViewModel.ResponseTitle + " / " + fixture.ViewModel.Transcript);
                routedOrigin.Should().Be(Kora.Core.Hosting.RequestOrigin.ActivatedVoice);
                routedAdmission.Should().BeTrue();
            }
            else if (operation is "new-request") { navigated.Should().Be(1); }
            else if (operation is "refine" or "restore" or "rename" or "recover-rename")
            {
                instructionOperation.Should().Be(operation switch
                {
                    "refine" => "Refine", "restore" => "Restore previous saved content",
                    "rename" => "Rename", _ => "Recover rename",
                });
                instructionId.Should().Be(operation is "rename" ? "renamed" : null);
                routedOrigin.Should().Be(RequestOrigin.ActivatedVoice);
                routedAdmission.Should().BeTrue();
                routed.Should().BeEmpty();
            }
            else { fixture.ViewModel.ResponseTitle.Should().Be(name + " version"); }
        }
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task HandsFree_wake_resume_waits_for_reviewed_command_owner_even_after_physical_capture_release()
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var fixture = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        fixture.ViewModel.BindReviewedVoiceCommand(NullLogger<ReviewedVoiceCommandCapture>.Instance, TimeProvider.System);
        await fixture.ViewModel.InitializeAsync();
        var wake = await stage.NextAsync();
        await voice.PauseWakeForReplyAsync();
        await fixture.ViewModel.BeginReviewedVoiceCommandAsync(static () => true);
        wake.IsQuiescent.Should().BeTrue();
        fixture.Voice.RaiseTranscript("Kora, what version are you running", 1);
        await fixture.ViewModel.FinishReviewedVoiceCommandAsync();
        fixture.Voice.IsCaptureQuiescent.Should().BeTrue();
        fixture.ViewModel.HasReviewedVoiceCommand.Should().BeTrue();
        await voice.ResumeAfterActivatedInputAsync();
        stage.OpenCount.Should().Be(1);
        fixture.ViewModel.ResponseTitle.Should().NotBe("Kora version");
        await fixture.ViewModel.CancelReviewedVoiceCommandAsync();
        await voice.ResumeAfterActivatedInputAsync();
        await stage.NextAsync();
        stage.OpenCount.Should().Be(2);
        fixture.ViewModel.ReviewedVoiceCommandText.Should().BeEmpty();
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("exact")]
    [InlineData("different-text")]
    [InlineData("different-request")]
    [InlineData("no-host")]
    [InlineData("different-channel")]
    [InlineData("new-channel-operation")]
    [InlineData("generation")]
    [InlineData("wake-generation")]
    [InlineData("playback")]
    [InlineData("span")]
    [InlineData("policy")]
    public async Task WakeAdmissionRemainsExactAndCannotLeakToAnotherInput(string change)
    {
        var stage = new HandsFreeStage { IsAvailable = false };
        HandsFreeVoiceRecognitionService? decorated = null;
        var fixture = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        await fixture.ViewModel.InitializeAsync();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var admission = (HandsFreeVoiceAdmission)typeof(MainViewModel).GetMethod("ObserveHandsFreeVoiceAdmission", flags)!
            .Invoke(fixture.ViewModel, null)!;
        var origin = change is "different-channel" ? RequestOrigin.LocalUi : RequestOrigin.ActivatedVoice;
        var request = HostRequest.Create(origin);
        var transcript = new VoiceTranscriptEventArgs("read a fixture", 1,
            change is "generation" ? voice.Generation - 1 : voice.Generation);
        var wake = new WakeWordObservation(change is "wake-generation" ? voice.Generation - 1 : transcript.Generation,
            change is "span" ? -1 : 12, change is not "playback");
        var activation = new HandsFreeCommandActivation(admission, wake, request);
        var scope = typeof(MainViewModel).GetField("wakeDispatch", flags)!.GetValue(fixture.ViewModel)!;
        var value = scope.GetType().GetProperty("Value")!;
        var record = typeof(MainViewModel).GetNestedType("WakeDispatch", BindingFlags.NonPublic)!;
        value.SetValue(scope, Activator.CreateInstance(record, transcript, activation));
        try
        {
            if (change is "policy") { fixture.ViewModel.BindVoiceOwnershipGate(static () => false); }
            using var host = change is "no-host" ? null
                : HostActivity.BeginRoot(change is "different-request" ? HostRequest.Create(origin) : request,
                    HostActivityLayer.Application, HostOperation.Request);
            var text = change is "different-text" ? "read another fixture" : transcript.Transcript;
            typeof(MainViewModel).GetMethod("IsWakeCommand", flags)!.Invoke(fixture.ViewModel, [text])
                .Should().Be(change is "exact" or "new-channel-operation");
            var operationOrigin = change is "new-channel-operation" ? RequestOrigin.LocalUi : origin;
            HostRequest? observed = null;
            Func<Task> operation = () =>
            {
                observed = HostActivity.RequireCurrent().Request;
                return Task.CompletedTask;
            };
            await (Task)typeof(MainViewModel).GetMethod("RunTranscriptOperationAsync", flags)!
                .Invoke(fixture.ViewModel, [operationOrigin, operation])!;
            if (change is "different-request" or "different-channel" or "new-channel-operation" or "no-host")
            {
                observed.Should().NotBeSameAs(request);
            }
            else { observed.Should().BeSameAs(request); }
            host?.Complete(HostOperationOutcome.Completed);
        }
        finally { value.SetValue(scope, null); }
        typeof(MainViewModel).GetMethod("IsWakeCommand", flags)!.Invoke(fixture.ViewModel, [transcript.Transcript]).Should().Be(false);
        fixture.ViewModel.Dispose();
    }

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
        var callback = (Func<VoiceTranscriptEventArgs, Kora.Core.Hosting.HostRequest, HandsFreeCommandActivation, Task>)
            typeof(HandsFreeVoiceRecognitionService).GetField("dispatch", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(voice)!;
        var request = Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.ActivatedVoice);
        var activation = new HandsFreeCommandActivation(new(new("mic", "Headset"), "Kora",
            new(0, SpeechInputSelection.Default), 0, 0, 0, 0), new(voice.Generation, 12, true), request);
        var foreign = () => callback(new("help", 1, voice.Generation - 1), request, activation);
        await foreign.Should().ThrowAsync<InvalidOperationException>();
        fixture.ViewModel.BindVoiceOwnershipGate(static () => false);
        var revoked = () => callback(new("help", 1, voice.Generation), request, activation);
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
        public IReadOnlyList<string> SupportedNames { get; init; } = [];
        public bool SupportsProfile(string assistantName, SpeechInputSelection selection) =>
            SupportedNames.Contains(assistantName, StringComparer.Ordinal) && selection == SpeechInputProfileCatalogue.NativeWakeSelection;
        public Task<IAsyncDisposable> PrepareProfileAsync(string assistantName, SpeechInputSelection selection, CancellationToken cancellationToken) =>
            Task.FromResult<IAsyncDisposable>(new PreparedProfile());
        private sealed class PreparedProfile : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
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
