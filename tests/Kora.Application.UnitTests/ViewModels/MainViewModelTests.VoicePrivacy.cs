using AwesomeAssertions;

using Kora.Core.Voice;
using Kora.Core.Platform;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private static Fixture CreateVoicePrivacyFixture(bool? consent = true)
    {
        var fixture = new Fixture();
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

    [Fact]
    public async Task Unreadable_consent_fails_closed_with_an_explicit_error()
    {
        var fixture = CreateVoicePrivacyFixture();
        fixture.VoiceConsent.Failure = new IOException("consent unreadable");

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

    [Fact]
    public async Task Permission_event_closes_capture_without_revealing_or_rearming_on_restore()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        var previous = fixture.PrivacyObservation.Current;
        var blocked = new WindowsPrivacySnapshot(WindowsSessionState.Unlocked, MicrophoneAccessState.Denied,
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
        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
        fixture.ViewModel.IsListening.Should().BeTrue();
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
}
