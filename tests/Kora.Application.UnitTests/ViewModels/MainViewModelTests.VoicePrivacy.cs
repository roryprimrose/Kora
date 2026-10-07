using AwesomeAssertions;

using Kora.Core;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Voice;
using Kora.Core.Platform;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private static Fixture CreateVoicePrivacyFixture(bool? consent = true,
        Kora.Application.Voice.BoundedMicrophoneCatalog? microphoneCatalog = null)
    {
        var fixture = new Fixture(microphoneCatalog: microphoneCatalog);
        fixture.VoiceConsent.Consent = consent;
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        return fixture;
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task No_saved_consent_never_arms_or_opens_input(bool? consent)
    {
        var fixture = CreateVoicePrivacyFixture(consent);
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        await fixture.ViewModel.RefreshMicrophonesAsync();

        fixture.ViewModel.HasVoiceConsent.Should().BeFalse();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Voice.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task Explicit_consent_arms_PTT_but_records_nothing_until_activation()
    {
        var fixture = CreateVoicePrivacyFixture(null);
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.SetVoiceConsentAsync(true);

        fixture.VoiceConsent.Consent.Should().BeTrue();
        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
        fixture.Voice.StartCalls.Should().Be(0);
        fixture.ViewModel.ListeningStatus.Should().Contain("production wake unavailable");

        await fixture.ViewModel.BeginPushToTalkAsync();

        fixture.Voice.StartCalls.Should().Be(1);
        fixture.ViewModel.IsListening.Should().BeTrue();
        await fixture.ViewModel.EndPushToTalkAsync();
        fixture.ViewModel.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task Unactivated_transcript_cannot_enter_the_command_pipeline()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();

        await fixture.Voice.RaiseTranscriptAsync("lock the machine", 1);

        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.Transcript.Should().Be("No command heard yet.");
    }

    [Fact]
    public async Task PTT_uses_the_existing_command_pipeline_once_per_activation()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        await fixture.ViewModel.EndPushToTalkAsync();

        await fixture.Voice.RaiseTranscriptAsync("help", 1);
        fixture.ViewModel.ResponseTitle.Should().Be("Built-in commands are ready.");
        await fixture.Voice.RaiseTranscriptAsync("lock the machine", 1);
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Manual_disable_refresh_selection_and_reinitialize_cannot_release_a_run_hold()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        await fixture.ViewModel.RefreshMicrophonesAsync();
        fixture.ViewModel.SelectedMicrophone = fixture.ViewModel.Microphones[1];
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();

        fixture.VoiceConsent.Consent.Should().BeTrue();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Voice.StartCalls.Should().Be(0);

        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
        fixture.Voice.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task Withdrawal_closes_capture_and_is_not_renewed_by_enable_or_restart()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        await fixture.ViewModel.SetVoiceConsentAsync(false);
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        await fixture.ViewModel.InitializeAsync();

        fixture.VoiceConsent.Consent.Should().BeFalse();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Voice.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task Stale_device_menu_and_duplicate_friendly_names_do_not_select_a_substitute()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        var device = fixture.ViewModel.Microphones[1];
        var revision = fixture.ViewModel.MicrophoneTopologyRevision;
        fixture.Voice.Microphones = [new MicrophoneDevice("other", device.Name)];
        fixture.Voice.DefaultMicrophoneId = "other";
        await fixture.ViewModel.RefreshMicrophonesAsync();

        await fixture.ViewModel.SelectMicrophoneAsync(device, revision);

        fixture.ViewModel.SelectedMicrophone.Should().Be(SystemAudioDevices.Microphone);
        fixture.ViewModel.ResponseTitle.Should().Be("The microphone selection is no longer current.");
    }

    [Fact]
    public async Task Locked_session_cannot_use_PTT_enablement_or_native_consent()
    {
        var fixture = CreateVoicePrivacyFixture();
        fixture.Session.IsUnlocked = false;
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.SetVoiceConsentAsync(true);
        await fixture.ViewModel.BeginPushToTalkAsync();

        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Voice.StartCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("Windows session locked", true)]
    [InlineData("Windows disconnected", true)]
    [InlineData("Windows session unknown", true)]
    [InlineData("Windows suspended", true)]
    [InlineData("Microphone permission lost", false)]
    [InlineData("Selected endpoint removed", false)]
    public async Task Observed_privacy_events_invalidate_before_async_cleanup_and_require_explicit_recovery(
        string reason, bool hide)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        var oldGeneration = fixture.Voice.Generation;

        fixture.ViewModel.CloseForObservedPrivacyEvent(reason, hide);

        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Voice.IsListening.Should().BeFalse();
        fixture.Voice.Generation.Should().BeGreaterThan(oldGeneration);
        fixture.Events.Should().Contain("speech.invalidate");
        await fixture.ViewModel.PrivacyClosureTask;
        await fixture.Voice.RaiseTranscriptAsync("lock the machine", 1);
        await fixture.ViewModel.RefreshMicrophonesAsync();
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();

        fixture.ViewModel.ShowApplication();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
        fixture.ViewModel.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task A_delayed_capture_open_cannot_restore_enablement_after_lock()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.Voice.StartGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opening = fixture.ViewModel.BeginPushToTalkAsync();
        fixture.ViewModel.CloseForObservedPrivacyEvent("Windows session locked", true);
        fixture.Voice.StartGate.TrySetResult();

        await opening;
        await fixture.ViewModel.PrivacyClosureTask;

        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.Voice.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task Speech_cleanup_cannot_delay_capture_invalidation()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.TextToSpeech.StopGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        fixture.ViewModel.CloseForObservedPrivacyEvent("Windows session locked", true);

        fixture.Voice.IsListening.Should().BeFalse();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.PrivacyClosureTask.IsCompleted.Should().BeFalse();
        fixture.TextToSpeech.StopGate.SetResult();
        await fixture.ViewModel.PrivacyClosureTask;
    }

    [Fact]
    public async Task Native_microphone_recovery_needs_no_model_or_speech_provider()
    {
        var fixture = CreateVoicePrivacyFixture(false);
        fixture.TextToSpeech.Providers = [];
        fixture.TextToSpeech.Voices = [];
        await fixture.ViewModel.RefreshMicrophonesAsync();
        await fixture.ViewModel.SelectMicrophoneAsync(fixture.ViewModel.Microphones[1],
            fixture.ViewModel.MicrophoneTopologyRevision);

        fixture.ViewModel.SelectedMicrophone!.Id.Should().Be("mic");
        fixture.Voice.StartCalls.Should().Be(0);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Fact]
    public async Task Unknown_permission_is_not_ongoing_recording_authority()
    {
        var fixture = CreateVoicePrivacyFixture();
        fixture.MicrophoneAccess.Status = new MicrophoneAccessStatus(MicrophoneAccessState.Unknown,
            "Windows permission could not be confirmed.");
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();

        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Voice.StartCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("corrupt")]
    public async Task Unreadable_consent_fails_closed_with_an_explicit_error(string failure)
    {
        var fixture = CreateVoicePrivacyFixture();
        fixture.VoiceConsent.Failure = failure switch
        {
            "access" => new UnauthorizedAccessException("consent unreadable"),
            "corrupt" => new InvalidDataException("consent unreadable"),
            _ => new IOException("consent unreadable"),
        };

        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.ResponseTitle.Should().Be("Voice consent could not be read.");
        fixture.ViewModel.ResponseBody.Should().Contain("consent unreadable");
        await fixture.ViewModel.BeginPushToTalkAsync();

        fixture.ViewModel.HasVoiceConsent.Should().BeFalse();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Voice.StartCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Consent_persistence_failure_never_keeps_capture_enabled(bool consent)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.VoiceConsent.Failure = new IOException("cannot save");

        await fixture.ViewModel.SetVoiceConsentAsync(consent);

        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Voice.IsListening.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Voice consent could not be saved.");
        fixture.ViewModel.ResponseBody.Should().Contain("cannot save");
    }

    [Fact]
    public async Task Release_during_capture_open_cancels_and_requires_explicit_recovery()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.Voice.StartGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opening = fixture.ViewModel.BeginPushToTalkAsync();

        await fixture.ViewModel.EndPushToTalkAsync();
        await opening;
        await fixture.ViewModel.EndPushToTalkAsync();

        fixture.Voice.IsListening.Should().BeFalse();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.IsBusy.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Command capture did not start.");
    }

    [Fact]
    public async Task Capture_completion_failure_invalidates_the_command_and_reports_the_error()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.Voice.EndException = new IOException("cannot finish");

        await fixture.ViewModel.EndPushToTalkAsync();
        await fixture.Voice.RaiseTranscriptAsync("lock the machine", 1);

        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.ResponseTitle.Should().Be("Command capture failed.");
    }

    [Fact]
    public async Task Native_recovery_is_unavailable_while_Windows_is_locked()
    {
        var fixture = CreateVoicePrivacyFixture();
        var recoveryRequests = 0;
        fixture.ViewModel.VoiceRecoveryRequested += (_, _) => recoveryRequests++;
        fixture.Session.IsUnlocked = false;

        fixture.ViewModel.RequestVoiceRecovery();
        fixture.ViewModel.ShowApplication();
        fixture.ViewModel.ShowSettings();

        recoveryRequests.Should().Be(0);
        fixture.WindowActions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handoff_quiesces_input_and_abandonment_keeps_explicit_recovery_required()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();

        (await fixture.ViewModel.TryPrepareHandoffAsync()).Should().BeTrue();
        fixture.ViewModel.ShowApplication();
        await fixture.RunAsync("lock the machine");
        fixture.Session.LockCalls.Should().Be(0);
        fixture.Voice.IsListening.Should().BeFalse();
        fixture.ViewModel.CanRevealPrivatePresentation.Should().BeFalse();

        fixture.ViewModel.AbandonHandoffPreparation();
        fixture.ViewModel.AbandonHandoffPreparation();

        fixture.ViewModel.CanRevealPrivatePresentation.Should().BeTrue();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
        fixture.Voice.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task Locked_or_busy_host_cannot_approve_handoff()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.Session.IsUnlocked = false;
        (await fixture.ViewModel.TryPrepareHandoffAsync()).Should().BeFalse();
        fixture.Session.IsUnlocked = true;
        fixture.Voice.StartGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opening = fixture.ViewModel.BeginPushToTalkAsync();
        (await fixture.ViewModel.TryPrepareHandoffAsync()).Should().BeFalse();
        await fixture.ViewModel.EndPushToTalkAsync();
        await opening;
    }

    [Fact]
    public async Task Handoff_stop_failure_retains_native_recovery_without_reopening_capture()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.Voice.StopException = new IOException("capture release failed");

        (await fixture.ViewModel.TryPrepareHandoffAsync()).Should().BeFalse();

        fixture.ViewModel.CanRevealPrivatePresentation.Should().BeTrue();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Handoff could not release host resources.");
    }

    [Fact]
    public async Task Enumeration_failure_holds_input_and_reports_native_recovery_error()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.Voice.GetMicrophonesException = new IOException("enumeration failed");

        await fixture.ViewModel.RefreshMicrophonesAsync();

        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Voice.IsListening.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Microphone recovery needs attention.");
    }

    [Fact]
    public async Task Speech_preview_is_not_available_during_explicit_command_capture()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();

        fixture.ViewModel.PreviewVoiceCommand.CanExecute(null).Should().BeFalse();
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(WindowsSessionState.Locked)]
    [InlineData(WindowsSessionState.Disconnected)]
    [InlineData(WindowsSessionState.SignedOut)]
    [InlineData(WindowsSessionState.Suspended)]
    [InlineData(WindowsSessionState.Unknown)]
    public async Task Observed_negative_session_snapshot_is_not_lost_to_a_newer_unlock(
        WindowsSessionState sessionState)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        var previous = fixture.PrivacyObservation.Current;
        var observed = new WindowsPrivacySnapshot(sessionState, MicrophoneAccessState.Allowed,
            previous.TopologyRevision, ["mic"], "mic", "0");
        fixture.PrivacyObservation.Publish(new WindowsPrivacyChangedEventArgs(
            previous: previous, current: observed, reason: WindowsPrivacyChangeReason.Session));

        fixture.PrivacyObservation.Current.SessionState.Should().Be(WindowsSessionState.Unlocked);
        fixture.ViewModel.IsPrivacyPresentationHeld.Should().BeTrue();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Voice.IsListening.Should().BeFalse();
        await fixture.ViewModel.PrivacyClosureTask;
        fixture.ViewModel.Transcript.Should().Be("No command heard yet.");
    }

    [Theory]
    [InlineData(MicrophoneAccessState.Denied)]
    [InlineData(MicrophoneAccessState.Unknown)]
    public async Task Permission_event_closes_capture_without_revealing_or_rearming_on_restore(
        MicrophoneAccessState permission)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        var previous = fixture.PrivacyObservation.Current;
        var blocked = new WindowsPrivacySnapshot(WindowsSessionState.Unlocked, permission,
            previous.TopologyRevision, ["mic"], "mic", "0");
        fixture.PrivacyObservation.Publish(new WindowsPrivacyChangedEventArgs(
            previous: previous, current: blocked, reason: WindowsPrivacyChangeReason.MicrophonePermission));
        await fixture.ViewModel.PrivacyClosureTask;

        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.IsPrivacyPresentationHeld.Should().BeFalse();
        fixture.PrivacyObservation.Publish(new WindowsPrivacyChangedEventArgs(
            previous: blocked, current: previous, reason: WindowsPrivacyChangeReason.MicrophonePermission));
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Natural_capture_completion_updates_recording_state_but_accepts_its_final_result()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.Voice.CompleteCapture();
        fixture.Voice.PublishCaptureState(new VoiceCaptureStateChangedEventArgs(
            generation: fixture.Voice.Generation, isListening: false));

        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
        await fixture.Voice.RaiseTranscriptAsync("help", 1);
        fixture.ViewModel.ResponseTitle.Should().Be("Built-in commands are ready.");
    }

    [Fact]
    public async Task Late_capture_state_event_cannot_close_a_new_command()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        var oldGeneration = fixture.Voice.Generation;
        await fixture.ViewModel.EndPushToTalkAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.Voice.PublishCaptureState(new VoiceCaptureStateChangedEventArgs(
            generation: oldGeneration, isListening: false));
        fixture.Voice.PublishCaptureState(new VoiceCaptureStateChangedEventArgs(
            generation: fixture.Voice.Generation, isListening: true));

        fixture.ViewModel.IsListening.Should().BeTrue();
        fixture.Voice.IsListening.Should().BeTrue();
    }

    [Fact]
    public async Task Failure_of_a_retired_activation_is_reported_and_requires_explicit_recovery()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();

        fixture.Voice.RaiseRetiredFailure("capture buffer failed");

        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Command not recognized.");
        fixture.ViewModel.ResponseBody.Should().Contain("capture buffer failed");
        await fixture.Voice.RaiseTranscriptAsync("lock the machine", 1);
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Disposal_unsubscribes_native_privacy_and_capture_callbacks()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.ViewModel.Dispose();
        fixture.ViewModel.Dispose();
        var previous = fixture.PrivacyObservation.Current;
        var locked = new WindowsPrivacySnapshot(WindowsSessionState.Locked, MicrophoneAccessState.Allowed,
            0, ["mic"], "mic", "0");
        fixture.PrivacyObservation.Publish(new WindowsPrivacyChangedEventArgs(
            previous: previous, current: locked, reason: WindowsPrivacyChangeReason.Session));
        fixture.Voice.RaiseFailure("late failure");
        fixture.Voice.CompleteCapture();
        fixture.Voice.PublishCaptureState(new VoiceCaptureStateChangedEventArgs(
            generation: fixture.Voice.Generation, isListening: false));

        fixture.ViewModel.IsPrivacyPresentationHeld.Should().BeFalse();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task Nonquiescent_capture_blocks_transfer_even_when_recording_is_false()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.Voice.PreventQuiescence = true;

        (await fixture.ViewModel.TryPrepareHandoffAsync()).Should().BeFalse();

        fixture.ViewModel.CanRevealPrivatePresentation.Should().BeTrue();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Handoff could not confirm quiescence.");
    }

    [Fact]
    public async Task Early_bounded_result_is_released_only_after_application_activation_admission()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.Voice.EarlyTranscript = "help";

        await fixture.ViewModel.BeginPushToTalkAsync();
        await fixture.Dispatcher.LastInvocation;

        fixture.ViewModel.IsBusy.Should().BeFalse();
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Built-in commands are ready.");
        fixture.ViewModel.Transcript.Should().Contain("help");
    }

    [Fact]
    public async Task Retired_activation_cannot_be_acknowledged_or_release_buffered_commands()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.Voice.EarlyTranscript = "lock the machine";
        fixture.Voice.RejectAcknowledgement = true;

        await fixture.ViewModel.BeginPushToTalkAsync();

        fixture.Voice.IsListening.Should().BeFalse();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.ResponseTitle.Should().Be("Command activation is no longer current.");
    }

    [Fact]
    public async Task Retired_activation_cleanup_failure_is_reported_without_crashing_the_UI()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.Voice.RejectAcknowledgement = true;
        fixture.Voice.StopException = new InvalidOperationException("native capture is still closing");

        var action = fixture.ViewModel.BeginPushToTalkAsync;

        await action.Should().NotThrowAsync();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Microphone cleanup needs attention.");
        fixture.ViewModel.ResponseBody.Should().Contain("Restart Kora");
    }

    [Fact]
    public async Task Unexpected_completion_failure_is_reported_without_escaping_the_PTT_handler()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.Voice.EndException = new NotSupportedException("unexpected completion failure");

        var action = fixture.ViewModel.EndPushToTalkAsync;

        await action.Should().NotThrowAsync();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Command capture failed.");
    }

    [Fact]
    public async Task Disable_listening_cleanup_failure_is_visible_without_escaping_the_command()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.Voice.StopException = new NotSupportedException("native release failed");

        var action = fixture.ViewModel.ToggleListeningCommand.ExecuteAsync;

        await action.Should().NotThrowAsync();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Microphone cleanup needs attention.");
        fixture.ViewModel.ResponseBody.Should().Contain("Restart Kora");
    }

    [Theory]
    [InlineData(VoiceRecognitionCompletionReason.EmptySpeechTimeout)]
    [InlineData(VoiceRecognitionCompletionReason.NoSpeechRecognized)]
    public async Task Empty_completion_closes_recording_without_granting_a_late_transcript(
        VoiceRecognitionCompletionReason reason)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.Voice.CompleteCapture();
        fixture.Voice.PublishCompletion(new VoiceRecognitionCompletedEventArgs(
            generation: fixture.Voice.Generation, reason: reason));
        await fixture.Voice.RaiseTranscriptAsync("lock the machine", 1);

        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.ResponseTitle.Should().Be("No command was heard.");
    }

    [Fact]
    public async Task Duration_completion_preserves_only_its_own_final_transcript()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.Voice.CompleteCapture();
        fixture.Voice.PublishCompletion(new VoiceRecognitionCompletedEventArgs(
            generation: fixture.Voice.Generation, reason: VoiceRecognitionCompletionReason.MaximumDuration));

        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Command capture reached its duration limit.");
        await fixture.Voice.RaiseTranscriptAsync("help", 1);
        fixture.ViewModel.ResponseTitle.Should().Be("Built-in commands are ready.");
    }

    [Fact]
    public async Task Stale_completion_cannot_change_a_new_activation_or_its_status()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        var oldGeneration = fixture.Voice.Generation;
        await fixture.ViewModel.EndPushToTalkAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.Voice.PublishCompletion(new VoiceRecognitionCompletedEventArgs(
            generation: oldGeneration, reason: VoiceRecognitionCompletionReason.NoSpeechRecognized));
        fixture.Voice.PublishCompletion(new VoiceRecognitionCompletedEventArgs(
            generation: fixture.Voice.Generation, reason: VoiceRecognitionCompletionReason.Recognized));

        fixture.ViewModel.IsListening.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be("Capturing your command.");
    }

    [Fact]
    public async Task Empty_timeout_of_a_retired_generation_still_reports_closed_recording()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        var generation = fixture.Voice.Generation;
        fixture.Voice.InvalidateCapture();
        fixture.Voice.PublishCompletion(new VoiceRecognitionCompletedEventArgs(
            generation: generation, reason: VoiceRecognitionCompletionReason.EmptySpeechTimeout));

        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be("No command was heard.");
    }

    [Theory]
    [InlineData("grant", false)]
    [InlineData("grant", true)]
    [InlineData("action", false)]
    [InlineData("action", true)]
    public async Task Native_approval_rechecks_session_before_and_after_stopping_the_prompt(string proposal, bool afterStop)
    {
        var fixture = CreateVoicePrivacyFixture();
        fixture.Probe.Status = new DependencyStatus("local.inference", "Inference", DependencyReadiness.Ready, "Ready");
        if (string.Equals(proposal, "grant", StringComparison.Ordinal))
        {
            fixture.Reasoner.GrantChange = new GrantChange(
                GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always);
        }
        else
        {
            fixture.Reasoner.Action = BuiltInAction.LockMachine;
        }
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.RunAsync("please review this request");
        await fixture.TextToSpeech.SpeakStarted.Task;
        if (afterStop)
        {
            fixture.TextToSpeech.BeforeStop = () => fixture.Session.IsUnlocked = false;
        }
        else
        {
            fixture.Session.IsUnlocked = false;
        }

        if (string.Equals(proposal, "grant", StringComparison.Ordinal))
        {
            await fixture.ViewModel.ConfirmGrantChangeAsync();
        }
        else
        {
            await fixture.ViewModel.ApproveModelActionCommand.ExecuteAsync();
        }
        fixture.TextToSpeech.SpeakGate.TrySetResult();
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.Session.LockCalls.Should().Be(0);
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("action")]
    [InlineData("grant")]
    [InlineData("question")]
    public async Task Privacy_closure_clears_each_pending_interaction(string proposal)
    {
        var fixture = CreateVoicePrivacyFixture();
        fixture.Probe.Status = new DependencyStatus("local.inference", "Inference", DependencyReadiness.Ready, "Ready");
        switch (proposal)
        {
            case "action":
                fixture.Reasoner.Action = BuiltInAction.LockMachine;
                break;
            case "grant":
                fixture.Reasoner.GrantChange = new GrantChange(
                    GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always);
                break;
            case "question":
                fixture.Reasoner.Question = new LocalModelQuestion("Which option?", ["One", "Two"]);
                break;
        }
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("please review this request");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.ViewModel.IsResponseInteractionPending.Should().BeTrue();

        fixture.ViewModel.CloseForObservedPrivacyEvent("lock", true);
        await fixture.ViewModel.PrivacyClosureTask;

        fixture.ViewModel.IsResponseInteractionPending.Should().BeFalse();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Recognition_failure_explains_an_unanswered_question_without_choosing_it()
    {
        var fixture = CreateVoicePrivacyFixture();
        fixture.Probe.Status = new DependencyStatus("local.inference", "Inference", DependencyReadiness.Ready, "Ready");
        fixture.Reasoner.Question = new LocalModelQuestion("Which option?", ["One", "Two"]);
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("please ask a question");
        await fixture.ViewModel.ActiveReasoningTask!;
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.Voice.RaiseRetiredFailure("no result");

        fixture.ViewModel.ResponseTitle.Should().Be("I didn't catch an option.");
        fixture.ViewModel.IsModelQuestionPending.Should().BeTrue();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recognition_failure_does_not_reveal_private_content_after_queued_privacy_change(bool holdPresentation)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.Dispatcher.BeforePost = () =>
        {
            if (holdPresentation)
            {
                fixture.ViewModel.CloseForObservedPrivacyEvent("lock", true);
            }
            else
            {
                fixture.Session.IsUnlocked = false;
            }
        };
        fixture.Voice.RaiseFailure("private content");
        fixture.ViewModel.ResponseBody.Should().NotContain("private content");
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("generation")]
    [InlineData("handoff")]
    [InlineData("lock")]
    public async Task Transcript_rechecks_admission_after_UI_dispatch(string transition)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        await fixture.ViewModel.EndPushToTalkAsync();
        fixture.Dispatcher.BeforeInvoke = () =>
        {
            switch (transition)
            {
                case "disable":
                    fixture.ViewModel.CloseForObservedPrivacyEvent("permission lost", false);
                    break;
                case "generation":
                    fixture.Voice.AdvanceGeneration();
                    break;
                case "handoff":
                    fixture.ViewModel.TryPrepareHandoffAsync().GetAwaiter().GetResult();
                    break;
                case "lock":
                    fixture.Session.IsUnlocked = false;
                    break;
            }
        };
        await fixture.Voice.RaiseTranscriptAsync("lock the machine", 1);
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Transcript_stops_playback_that_started_after_activation()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        await fixture.ViewModel.EndPushToTalkAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preview = fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        await fixture.TextToSpeech.SpeakStarted.Task;

        await fixture.Voice.RaiseTranscriptAsync("help", 1);
        await preview;

        fixture.ViewModel.IsSpeaking.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Built-in commands are ready.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Speech_cancellation_is_reported_as_a_privacy_transition_without_clearing_the_voice(bool preview)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakException = new OperationCanceledException("privacy transition");
        if (preview)
        {
            await fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        }
        else
        {
            await fixture.RunAsync("help");
        }
        fixture.ViewModel.IsSpeaking.Should().BeFalse();
        fixture.ViewModel.SelectedVoice.Should().NotBeNull();
    }

    [Fact]
    public async Task Locked_host_denies_preview_and_direct_dispatch()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.Session.IsUnlocked = false;
        await fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        await fixture.ViewModel.ExecuteAsync(fixture.Catalog.GetCommands("Kora")
            .Single(command => command.Action == BuiltInAction.LockMachine));
        fixture.ViewModel.ShowPresentation();
        fixture.Session.LockCalls.Should().Be(0);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Fact]
    public async Task Capture_invalidation_failure_is_an_explicit_native_recovery_error()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.Voice.StopException = new IOException("release failed");
        fixture.ViewModel.CloseForObservedPrivacyEvent("lock", true);
        await fixture.ViewModel.PrivacyClosureTask;
        fixture.ViewModel.ResponseTitle.Should().Be("Audio privacy closure needs attention.");
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Handoff_hold_denies_native_recovery_and_consent_until_abandoned()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        (await fixture.ViewModel.TryPrepareHandoffAsync()).Should().BeTrue();
        fixture.ViewModel.RequestVoiceRecovery();
        await fixture.ViewModel.SetVoiceConsentAsync(false);
        fixture.ViewModel.ShowSettings();
        fixture.VoiceConsent.Consent.Should().BeTrue();
        fixture.ViewModel.AbandonHandoffPreparation();
    }

    [Fact]
    public async Task Disposal_cancels_pending_capture_open()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.Voice.StartGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opening = fixture.ViewModel.BeginPushToTalkAsync();
        fixture.ViewModel.Dispose();
        await opening;
        fixture.Voice.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task Disposal_retires_a_transcript_already_queued_for_UI_dispatch()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        await fixture.ViewModel.EndPushToTalkAsync();
        fixture.Dispatcher.BeforeInvoke = fixture.ViewModel.Dispose;

        await fixture.Voice.RaiseTranscriptAsync("lock the machine", 1);

        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.Transcript.Should().Be("No command heard yet.");
    }

    [Fact]
    public async Task Disposal_cannot_be_undone_by_a_native_open_that_ignores_cancellation()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.Voice.StartGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Voice.IgnoreStartCancellation = true;
        var opening = fixture.ViewModel.BeginPushToTalkAsync();
        fixture.ViewModel.Dispose();
        fixture.Voice.StartGate.SetResult();

        await opening;
        await fixture.ViewModel.BeginPushToTalkAsync();

        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Voice.IsListening.Should().BeFalse();
        fixture.Voice.StartCalls.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposal_rejects_already_queued_capture_state_and_completion(bool completion)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        var generation = fixture.Voice.Generation;
        var title = fixture.ViewModel.ResponseTitle;
        fixture.Voice.CompleteCapture();
        fixture.Dispatcher.BeforePost = fixture.ViewModel.Dispose;

        if (completion)
        {
            fixture.Voice.PublishCompletion(new VoiceRecognitionCompletedEventArgs(
                generation, VoiceRecognitionCompletionReason.NoSpeechRecognized));
        }
        else
        {
            fixture.Voice.PublishCaptureState(new VoiceCaptureStateChangedEventArgs(generation, false));
        }

        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Voice.Generation.Should().BeGreaterThan(generation);
    }

    [Fact]
    public async Task Disposal_rejects_a_topology_refresh_already_queued_for_UI_dispatch()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        var title = fixture.ViewModel.ResponseTitle;
        fixture.Dispatcher.BeforePost = fixture.ViewModel.Dispose;
        fixture.Voice.GetMicrophonesException = new IOException("Disposed capture service");

        await PublishTopologyAsync(fixture, 1);

        fixture.ViewModel.ResponseTitle.Should().Be(title);
    }

    [Fact]
    public async Task Disposal_rejects_recognition_failure_presentation_already_queued_for_UI_dispatch()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        var title = fixture.ViewModel.ResponseTitle;
        fixture.Dispatcher.BeforePost = () => fixture.Dispatcher.BeforePost = fixture.ViewModel.Dispose;

        fixture.Voice.RaiseFailure("retired private recognition detail");

        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.ViewModel.ResponseBody.Should().NotContain("retired private recognition detail");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unavailable_default_output_closes_System_before_queued_enumeration_but_preserves_pinned_output(
        bool pinned)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        if (pinned)
        {
            await LoadSavedOutputAsync(fixture, "0");
        }
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preview = fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        await fixture.TextToSpeech.SpeakStarted.Task;
        fixture.Events.Clear();
        var closedBeforeDispatch = false;
        fixture.Dispatcher.BeforePost = () =>
            closedBeforeDispatch = fixture.Events.Contains("speech.invalidate", StringComparer.Ordinal);
        if (!pinned)
        {
            fixture.TextToSpeech.OutputDevices = [];
        }
        fixture.TextToSpeech.DefaultOutputDeviceId = null;

        await PublishTopologyAsync(fixture, 1, WindowsPrivacyChangeReason.DefaultSpeaker | WindowsPrivacyChangeReason.OutputTopology);
        await fixture.ViewModel.PrivacyClosureTask;
        closedBeforeDispatch.Should().Be(!pinned);
        fixture.TextToSpeech.IsSpeaking.Should().Be(pinned);
        fixture.TextToSpeech.SpeakGate.TrySetResult();
        await preview;

        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
        fixture.Voice.StartCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("ArgumentOutOfRange")]
    [InlineData("InvalidOperation")]
    public async Task Enablement_probe_errors_are_visible_and_keep_capture_closed(string kind)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.PrivacyObservation.RefreshException = string.Equals(kind, "ArgumentOutOfRange", StringComparison.Ordinal)
            ? new ArgumentOutOfRangeException(nameof(kind)) : new InvalidOperationException("privacy observation failed");
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
    }

    [Fact]
    public async Task Synchronous_privacy_change_during_enablement_cannot_release_a_run_hold()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.PrivacyObservation.BeforeRefresh = () =>
            fixture.ViewModel.CloseForObservedPrivacyEvent("permission changed", false);
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Readiness changed.");
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("device")]
    [InlineData("policy")]
    [InlineData("session")]
    public async Task Capture_open_rechecks_each_fresh_gate(string gate)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.SetAllowVoiceActivationDuringCallsAsync(false);
        fixture.PrivacyObservation.BeforeRefresh = () =>
        {
            switch (gate)
            {
                case "permission":
                    fixture.MicrophoneAccess.Status = new MicrophoneAccessStatus(MicrophoneAccessState.Denied, "Denied");
                    break;
                case "device":
                    fixture.ViewModel.SelectedMicrophone = null;
                    break;
                case "policy":
                    fixture.CallState.SetState(CallState.Active);
                    break;
                case "session":
                    fixture.PrivacyObservation.Current = new WindowsPrivacySnapshot(
                        WindowsSessionState.Locked, MicrophoneAccessState.Allowed, 0, ["mic"], "mic", "0");
                    break;
            }
        };
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Voice.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task Repeated_push_to_talk_hold_does_not_open_a_second_capture()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.Voice.StartCalls.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Successful_but_late_open_cannot_restore_closed_capture(bool release)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.Voice.StartGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Voice.IgnoreStartCancellation = true;
        var opening = fixture.ViewModel.BeginPushToTalkAsync();
        if (release)
        {
            await fixture.ViewModel.EndPushToTalkAsync();
        }
        else
        {
            fixture.ViewModel.CloseForObservedPrivacyEvent("lock", true);
        }
        fixture.Voice.StartGate.SetResult();
        await opening;
        fixture.Voice.IsListening.Should().BeFalse();
        fixture.ViewModel.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task Session_lock_after_native_open_closes_before_acknowledging_transcription()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.Voice.AfterStart = () => fixture.Session.IsUnlocked = false;
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.Voice.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task Null_and_stale_selections_do_not_substitute_endpoints()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedMicrophone = null;
        fixture.ViewModel.SelectedMicrophone = new MicrophoneDevice("missing", "Missing");
        fixture.ViewModel.SelectedMicrophone.Should().BeNull();
        await fixture.ViewModel.SelectMicrophoneAsync(new MicrophoneDevice("missing", "Missing"),
            fixture.ViewModel.MicrophoneTopologyRevision);
        fixture.ViewModel.SelectedMicrophone.Should().BeNull();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.ListeningStatus.Should().Contain("use Enable listening");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Topology_refresh_preserves_missing_pinned_output_without_substitution(bool pinned)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedMicrophone = null;
        await LoadSavedOutputAsync(fixture, pinned ? "0" : "unavailable");
        fixture.TextToSpeech.OutputDevices = [];
        await PublishTopologyAsync(fixture, 1);
        fixture.ViewModel.SelectedOutputDevice?.Id.Should().Be(pinned ? "0" : null);
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Topology_refresh_restores_endpoints_after_native_controls_clear_their_selections(
        bool captureActive)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await LoadSavedOutputAsync(fixture, "0");
        var microphoneId = fixture.ViewModel.SelectedMicrophone!.Id;
        var microphoneName = fixture.ViewModel.SelectedMicrophone.Name;
        var outputId = fixture.ViewModel.SelectedOutputDevice!.Id;
        if (captureActive)
        {
            await fixture.ViewModel.BeginPushToTalkAsync();
        }
        fixture.ViewModel.Microphones.CollectionChanged += (_, _) =>
        {
            if (fixture.ViewModel.Microphones.Count == 0)
            {
                fixture.ViewModel.SelectedMicrophone = null;
                fixture.ViewModel.ListeningStatus.Should().Be(captureActive
                    ? "Push-to-talk capture active · microphone selection is refreshing"
                    : "Microphone closed · selected microphone is unavailable");
            }
        };
        fixture.ViewModel.OutputDevices.CollectionChanged += (_, _) =>
        {
            if (fixture.ViewModel.OutputDevices.Count == 0)
            {
                fixture.ViewModel.SelectedOutputDevice = null;
            }
        };

        await PublishTopologyAsync(fixture, 1);

        fixture.ViewModel.SelectedMicrophone!.Id.Should().Be(microphoneId);
        fixture.ViewModel.SelectedOutputDevice!.Id.Should().Be(outputId);
        fixture.ViewModel.IsListening.Should().Be(captureActive);
        fixture.ViewModel.ListeningStatus.Should().Contain(microphoneName);
    }

    [Fact]
    public async Task Output_enumeration_failure_is_visible_on_native_refresh()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.OutputEnumerationException = new IOException("render endpoints unavailable");
        await PublishTopologyAsync(fixture, 1);
        fixture.ViewModel.ResponseTitle.Should().Be("Audio output is unavailable.");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Unavailable_output_invalidates_active_speech_and_reports_failed_stop(bool failure, bool pinned)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        if (pinned)
        {
            await LoadSavedOutputAsync(fixture, "0");
        }
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preview = fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        await fixture.TextToSpeech.SpeakStarted.Task;
        if (failure)
        {
            fixture.TextToSpeech.StopException = new IOException("render release failed");
        }
        fixture.TextToSpeech.OutputDevices = pinned
            ? [new AudioOutputDevice("new-default", "Other available output")] : [];
        fixture.TextToSpeech.DefaultOutputDeviceId = "new-default";
        await PublishTopologyAsync(fixture, 1);
        await fixture.ViewModel.PrivacyClosureTask;
        fixture.TextToSpeech.SpeakGate.TrySetResult();
        await preview;
        fixture.ViewModel.IsSpeaking.Should().BeFalse();
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();
        fixture.ViewModel.SelectedOutputDevice!.Id.Should().Be(
            pinned ? "0" : SystemAudioDevices.Output.Id);
        if (failure)
        {
            fixture.ViewModel.ResponseTitle.Should().Be("Audio output route changed.");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Eligible_default_and_unrelated_topology_changes_preserve_active_output(
        bool pinned, bool inputChange)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        if (pinned)
        {
            await LoadSavedOutputAsync(fixture, "0");
        }
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preview = fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        await fixture.TextToSpeech.SpeakStarted.Task;
        fixture.Events.Clear();
        var stopCalls = fixture.TextToSpeech.StopCalls;
        if (!inputChange)
        {
            fixture.TextToSpeech.OutputDevices =
            [
                new AudioOutputDevice("0", "Default output"),
                new AudioOutputDevice("new-default", "New default"),
            ];
            fixture.TextToSpeech.DefaultOutputDeviceId = "new-default";
        }

        await PublishTopologyAsync(fixture, 1, inputChange
            ? WindowsPrivacyChangeReason.InputTopology : WindowsPrivacyChangeReason.DefaultSpeaker);

        fixture.TextToSpeech.IsSpeaking.Should().BeTrue();
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeTrue();
        fixture.TextToSpeech.StopCalls.Should().Be(stopCalls);
        fixture.Events.Should().NotContain("speech.invalidate");
        fixture.ViewModel.SelectedOutputDevice!.Id.Should().Be(
            pinned ? "0" : SystemAudioDevices.Output.Id);
        fixture.TextToSpeech.SpeakGate.TrySetResult();
        await preview;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Muted_effective_output_stops_active_speech_without_substitution(bool pinned)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        if (pinned)
        {
            await LoadSavedOutputAsync(fixture, "0");
        }
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preview = fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        await fixture.TextToSpeech.SpeakStarted.Task;
        fixture.TextToSpeech.OutputDevices = [new AudioOutputDevice("0", "Default output", IsMuted: true)];

        await PublishTopologyAsync(fixture, 1);
        await fixture.ViewModel.PrivacyClosureTask;
        await preview;

        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();
        fixture.TextToSpeech.IsSpeaking.Should().BeFalse();
        fixture.Events.Should().Contain("speech.invalidate");
    }

    [Fact]
    public async Task Output_enumeration_failure_invalidates_active_speech()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preview = fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        await fixture.TextToSpeech.SpeakStarted.Task;
        fixture.TextToSpeech.OutputEnumerationException = new IOException("render endpoints unavailable");

        await PublishTopologyAsync(fixture, 1);
        await fixture.ViewModel.PrivacyClosureTask;
        await preview;

        fixture.TextToSpeech.IsSpeaking.Should().BeFalse();
        fixture.Events.Should().Contain("speech.invalidate");
        fixture.ViewModel.ResponseTitle.Should().Be("Audio output is unavailable.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Valid_default_microphone_change_preserves_activation_without_releasing_a_run_hold(bool disabled)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        if (disabled)
        {
            await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        }
        var generation = fixture.Voice.Generation;
        fixture.Voice.Microphones = [new MicrophoneDevice("new-mic", "New default microphone")];
        fixture.Voice.DefaultMicrophoneId = "new-mic";
        var previous = fixture.PrivacyObservation.Current;
        var current = previous with
        {
            TopologyRevision = previous.TopologyRevision + 1,
            ActiveMicrophoneIds = ["new-mic"],
            DefaultMicrophoneId = "new-mic",
        };
        fixture.PrivacyObservation.Current = current;

        fixture.PrivacyObservation.Publish(new WindowsPrivacyChangedEventArgs(
            previous, current, WindowsPrivacyChangeReason.DefaultMicrophone));

        fixture.ViewModel.IsVoiceEnabled.Should().Be(!disabled);
        fixture.ViewModel.IsListening.Should().Be(!disabled);
        fixture.Voice.Generation.Should().Be(generation);
        fixture.Voice.StartCalls.Should().Be(1);
        await fixture.ViewModel.EndPushToTalkAsync();
    }

    private static Task PublishTopologyAsync(
        Fixture fixture, long revision,
        WindowsPrivacyChangeReason reason = WindowsPrivacyChangeReason.OutputTopology)
    {
        var previous = fixture.PrivacyObservation.Current;
        var current = new WindowsPrivacySnapshot(WindowsSessionState.Unlocked, MicrophoneAccessState.Allowed,
            revision, ["mic"], "mic", fixture.TextToSpeech.DefaultOutputDeviceId);
        fixture.PrivacyObservation.Current = current;
        fixture.PrivacyObservation.Publish(new WindowsPrivacyChangedEventArgs(previous, current,
            reason));
        return fixture.ViewModel.MicrophoneRefreshTask;
    }

    [Fact]
    public async Task A_result_consumed_during_dispatch_cannot_be_dispatched_twice()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        await fixture.ViewModel.EndPushToTalkAsync();
        fixture.Dispatcher.BeforeInvoke = () => fixture.Voice.RaiseTranscript("help", 1);

        await fixture.Voice.RaiseTranscriptAsync("lock the machine", 1);

        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.ResponseTitle.Should().Be("Built-in commands are ready.");
    }

    [Fact]
    public async Task Service_retirement_before_UI_dispatch_rejects_the_original_accepted_generation()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        await fixture.ViewModel.EndPushToTalkAsync();
        var generation = fixture.Voice.Generation;
        fixture.Voice.AdvanceGeneration();
        fixture.Voice.RaiseTranscriptForGeneration(generation);
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Native_recovery_emits_an_explicit_request_without_releasing_privacy_holds()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        var requests = 0;
        fixture.ViewModel.VoiceRecoveryRequested += (_, _) => requests++;
        fixture.ViewModel.RequestVoiceRecovery();
        requests.Should().Be(1);
        fixture.Voice.StartCalls.Should().Be(0);
        fixture.ViewModel.CloseForObservedPrivacyEvent("lock", true);
        fixture.ViewModel.ShowPresentation();
        fixture.ViewModel.IsPrivacyPresentationHeld.Should().BeTrue();
    }

    [Fact]
    public async Task Pinned_microphone_removed_during_refresh_remains_unavailable_without_substitution()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedMicrophone = fixture.ViewModel.Microphones[1];
        fixture.Voice.Microphones = [];
        fixture.Voice.DefaultMicrophoneId = null;
        await fixture.ViewModel.RefreshMicrophonesAsync();
        fixture.ViewModel.SelectedMicrophone!.Id.Should().Be("mic");
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Eligible_endpoint_selection_is_denied_during_lock_or_handoff(bool handoff)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        if (handoff)
        {
            (await fixture.ViewModel.TryPrepareHandoffAsync()).Should().BeTrue();
        }
        else
        {
            fixture.Session.IsUnlocked = false;
        }
        await fixture.ViewModel.SelectMicrophoneAsync(fixture.ViewModel.Microphones[1],
            fixture.ViewModel.MicrophoneTopologyRevision);
        fixture.ViewModel.SelectedMicrophone.Should().Be(SystemAudioDevices.Microphone);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Closed_status_distinguishes_call_policy_without_a_saved_pause_reason(bool blockCall)
    {
        var fixture = CreateVoicePrivacyFixture();
        fixture.Voice.Microphones = [];
        await fixture.ViewModel.InitializeAsync();
        if (blockCall)
        {
            await fixture.ViewModel.SetAllowVoiceActivationDuringCallsAsync(false);
            fixture.CallState.SetState(CallState.Active);
            await fixture.Dispatcher.LastInvocation;
        }
        fixture.ViewModel.ListeningStatus.Should().Be(blockCall
            ? "Microphone closed · voice activation paused during detected call" : "Microphone closed");
    }

    [Fact]
    public async Task Friendly_name_change_for_the_same_endpoint_does_not_withdraw_enablement()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedMicrophone = fixture.ViewModel.Microphones[1];
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        var renamed = new MicrophoneDevice("mic", "Renamed headset");
        fixture.ViewModel.Microphones.Add(renamed);
        fixture.ViewModel.SelectedMicrophone = renamed;
        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Windows_permission_denial_has_a_distinct_closed_status()
    {
        var fixture = CreateVoicePrivacyFixture();
        fixture.MicrophoneAccess.Status = new MicrophoneAccessStatus(MicrophoneAccessState.Denied, "Denied");
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.ListeningStatus.Should().Be("Microphone closed · Windows access is blocked");
    }

    [Fact]
    public async Task Unactivated_failure_during_armed_idle_cannot_consume_a_command_generation()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.Voice.RaiseFailure("ambient failure");
        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
        fixture.ViewModel.ResponseBody.Should().NotContain("ambient failure");
    }

    [Fact]
    public async Task Topology_callback_during_selection_clear_cannot_infer_an_endpoint_or_open_capture()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.PropertyChanged += (_, change) =>
        {
            if (string.Equals(change.PropertyName, nameof(fixture.ViewModel.SelectedMicrophone), StringComparison.Ordinal)
                && fixture.ViewModel.SelectedMicrophone is null)
            {
                fixture.ViewModel.ListeningStatus.Should().Contain("use Enable listening");
                fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
                _ = PublishTopologyAsync(fixture, 2);
            }
        };
        fixture.ViewModel.SelectedMicrophone = null;
        await fixture.ViewModel.MicrophoneRefreshTask;
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Voice.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task Handoff_refuses_synchronous_model_dispatch_and_accepts_only_completed_work()
    {
        var fixture = CreateVoicePrivacyFixture();
        fixture.Probe.Status = new DependencyStatus("local.inference", "Inference", DependencyReadiness.Ready, "Ready");
        fixture.Reasoner.Action = BuiltInAction.ShowHelp;
        await fixture.ViewModel.InitializeAsync();
        bool? handoffDuringDispatch = null;
        fixture.ViewModel.PropertyChanged += (_, change) =>
        {
            if (string.Equals(change.PropertyName, nameof(fixture.ViewModel.IsSpeaking), StringComparison.Ordinal)
                && fixture.ViewModel.IsSpeaking)
            {
                handoffDuringDispatch = fixture.ViewModel.TryPrepareHandoffAsync().GetAwaiter().GetResult();
            }
        };
        await fixture.RunAsync("please describe available features");
        await fixture.ViewModel.ActiveReasoningTask!;
        handoffDuringDispatch.Should().BeFalse();
        (await fixture.ViewModel.TryPrepareHandoffAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task Recording_and_unconsented_statuses_are_truthful_without_UI_bindings()
    {
        var fixture = CreateVoicePrivacyFixture(false);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.ListeningStatus.Should().Be("Microphone closed · ongoing voice consent not granted");
        fixture.Voice.RaiseFailure("unconsented result");
        fixture.ViewModel.HasVoiceConsent.Should().BeFalse();
        await fixture.ViewModel.SetVoiceConsentAsync(true);
        fixture.ViewModel.ListeningStatus.Should().Contain("Push-to-talk ready");
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.ViewModel.ListeningStatus.Should().Contain("Push-to-talk capture");
        await fixture.ViewModel.EndPushToTalkAsync();
    }
}
