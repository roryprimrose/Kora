using AwesomeAssertions;
using Kora.Core.Commands;
using Kora.Application.Configuration;
using Kora.Application.Voice;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Platform;
using Kora.Core.Communication;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private static async Task LoadSavedOutputAsync(Fixture fixture, string outputId)
    {
        fixture.AudioPreferences.OutputDeviceId = outputId;
        await fixture.ViewModel.RefreshCommand.ExecuteAsync();
    }

    [Fact]
    public async Task Output_exact_commands_and_native_choice_save_reset_share_real_admission_without_autoplay()
    {
        var fixture = new Fixture(enableOutputConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        var microphones = fixture.Voice.StartCalls;
        await fixture.RunAsync("Kora, list output settings");
        fixture.ViewModel.ResponseBody.Should().Contain("speech.output-device").And.Contain("exact-endpoint-choice");
        await fixture.RunAsync("set speech.output-device to 0");
        fixture.AudioPreferences.SavedOutputDeviceId.Should().Be("0");
        fixture.ViewModel.SelectedOutputDevice!.Id.Should().Be("0");
        await fixture.RunAsync("status speech.output-device");
        fixture.ViewModel.ResponseBody.Should().Contain("\"source\":\"saved\"").And.Contain("\"effective\":\"0\"");
        fixture.ViewModel.SelectedOutputChoice = fixture.ViewModel.OutputDeviceChoices.Single(item => item.Device.IsSystemDefault);
        await fixture.ViewModel.SaveOutputDeviceCommand.ExecuteAsync();
        fixture.AudioPreferences.OutputDeviceId.Should().BeNull();
        await fixture.ViewModel.RefreshOutputDevicesCommand.ExecuteAsync();
        fixture.ViewModel.SelectedOutputChoice = fixture.ViewModel.OutputDeviceChoices.Single(item => !item.Device.IsSystemDefault);
        await fixture.ViewModel.SaveOutputDeviceCommand.ExecuteAsync();
        await fixture.ViewModel.ResetOutputDeviceCommand.ExecuteAsync();
        fixture.ViewModel.SelectedOutputDevice.Should().Be(SystemAudioDevices.Output);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Voice.StartCalls.Should().Be(microphones);
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.Preferences.SavedVoiceId.Should().BeNull();
    }

    [Fact]
    public async Task Activated_output_mutation_preserves_original_channel_and_protected_call_refusal()
    {
        var fixture = new Fixture(enableOutputConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RaiseActivatedTranscriptAsync("Kora, list output settings", 1);
        await fixture.RaiseActivatedTranscriptAsync("Kora, set speech.output-device to 0", 1);
        fixture.AudioPreferences.SavedOutputDeviceId.Should().Be("0");
        fixture.CallState.SetState(CallState.Active);
        await fixture.ViewModel.SetAllowVoiceActivationDuringCallsAsync(false);
        await fixture.Dispatcher.LastInvocation;
        await fixture.RaiseActivatedTranscriptAsync("Kora, reset speech.output-device", 1);
        fixture.AudioPreferences.OutputDeviceId.Should().Be("0");
        await fixture.RunAsync("reset speech.output-device");
        fixture.AudioPreferences.OutputDeviceId.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Output_command_does_not_answer_or_overwrite_exact_pending_approval()
    {
        var fixture = new Fixture(enableOutputConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        fixture.Probe.Status = new("local.inference", "Local model inference (Ollama)", Kora.Core.Dependencies.DependencyReadiness.Ready, "ready");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("lock this workstation please");
        await fixture.ViewModel.ActiveReasoningTask!;
        var preview = fixture.ViewModel.ResponseBody;
        fixture.ViewModel.CanChangeAudioOutputDevice.Should().BeFalse();
        await fixture.RunAsync("reset speech.output-device");
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        fixture.ViewModel.ResponseBody.Should().Be(preview);
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_output_change_retires_active_response_without_capture_or_replay(bool fail)
    {
        var fixture = new Fixture(enableOutputConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.RefreshOutputDevicesCommand.ExecuteAsync();
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var speaking = fixture.RunAsync("what power action is pending");
        await fixture.TextToSpeech.SpeakStarted.Task;
        var body = fixture.ViewModel.ResponseBody;
        var starts = fixture.Voice.StartCalls;
        if (fail) { fixture.AudioPreferences.OutputDeviceSaveException = new IOException("owned storage failure"); }
        fixture.ViewModel.SelectedOutputChoice = fixture.ViewModel.OutputDeviceChoices.Single(item => !item.Device.IsSystemDefault);
        await fixture.ViewModel.SaveOutputDeviceCommand.ExecuteAsync();
        fixture.TextToSpeech.SpeakGate.TrySetResult();
        await speaking;
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.Voice.StartCalls.Should().Be(starts);
        fixture.AudioPreferences.SavedOutputDeviceId.Should().Be(fail ? null : "0");
        if (fail) { fixture.ViewModel.Transcript.Should().Contain("not confirmed"); }
    }

    [Fact]
    public async Task Native_metadata_missing_saved_pin_and_default_refresh_remain_truthful_with_no_implicit_choice()
    {
        var fixture = new Fixture(enableOutputConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        fixture.AudioPreferences.OutputDeviceId = "missing";
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.CanChangeAudioOutputDevice.Should().BeTrue();
        fixture.ViewModel.AudioOutputConfigurationStatus.Should().Contain("\"source\":\"saved\"").And.Contain("\"effective\":null");
        fixture.ViewModel.SelectedOutputChoice.Should().BeNull();
        fixture.ViewModel.SelectedOutputDevice!.Id.Should().Be("missing");
        var notifications = 0;
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(fixture.ViewModel.SelectedOutputDevice), StringComparison.Ordinal))
            {
                notifications++;
                fixture.ViewModel.SelectedOutputDevice = new("forged", "Fake");
            }
        };
        await fixture.RunAsync("set speech.output-device to default output");
        fixture.ViewModel.State.Should().Be(Kora.Core.AssistantState.Failure);
        fixture.AudioPreferences.OutputDeviceId.Should().Be("missing");
        await fixture.RunAsync("reset speech.output-device");
        fixture.ViewModel.SelectedOutputDevice.Should().Be(SystemAudioDevices.Output);
        fixture.TextToSpeech.OutputDevices = [];
        await fixture.ViewModel.RefreshOutputDevicesCommand.ExecuteAsync();
        fixture.ViewModel.AudioOutputConfigurationStatus.Should().Contain("\"available\":false");
        fixture.ViewModel.CanChangeAudioOutputDevice.Should().BeTrue();
        fixture.ViewModel.SelectedOutputDevice = fixture.ViewModel.SelectedOutputDevice;
        notifications.Should().BeGreaterThan(0);
        fixture.ViewModel.Dispose();
        fixture.ViewModel.CanChangeAudioOutputDevice.Should().BeFalse();
        await fixture.ViewModel.RefreshOutputDevicesCommand.ExecuteAsync();
    }

    [Fact]
    public async Task Unbound_or_unknown_host_and_pending_controls_never_acquire_configuration_authority()
    {
        var unbound = new Fixture();
        await using var unboundAdmission = unbound.OutputAdmission;
        unbound.ViewModel.CanChangeAudioOutputDevice.Should().BeFalse();
        unbound.ViewModel.OutputDeviceChoices.Should().BeEmpty();
        unbound.ViewModel.AudioOutputConfigurationStatus.Should().Contain("unavailable");
        await unbound.ViewModel.RefreshOutputDevicesCommand.ExecuteAsync();
        await unbound.ViewModel.ExecuteOutputDeviceCommandAsync(new(AppearanceCommandOperation.List), SecurityAuditInitiator.LocalUser, TestContext.Current.CancellationToken);
        unbound.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
        var fixture = new Fixture(enableOutputConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        fixture.Session.IsUnlocked = false;
        fixture.ViewModel.CanChangeAudioOutputDevice.Should().BeFalse();
        await fixture.ViewModel.RefreshOutputDevicesCommand.ExecuteAsync();
        await fixture.ViewModel.ExecuteOutputDeviceCommandAsync(new(AppearanceCommandOperation.Get), SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        fixture.ViewModel.Dispose();
        fixture.OutputConfiguration!.Observe(new([], null));
    }

    [Fact]
    public async Task Native_detection_failure_before_first_snapshot_exposes_recovery_and_never_defaults_corrupt_state()
    {
        var fixture = new Fixture(enableOutputConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        fixture.TextToSpeech.OutputEnumerationException = new IOException("owned metadata unavailable");
        await fixture.ViewModel.RefreshOutputDevicesCommand.ExecuteAsync();
        fixture.ViewModel.State.Should().Be(Kora.Core.AssistantState.Failure);
        fixture.ViewModel.SelectedOutputDevice.Should().BeNull();
        fixture.ViewModel.AudioOutputConfigurationStatus.Should().Contain("\"source\":\"unavailable\"");
        fixture.ViewModel.AudioOutputConfigurationStatus.Should().Contain("not confirmed");
        var privacy = fixture.PrivacyObservation.Current;
        fixture.PrivacyObservation.Publish(new(privacy, privacy, WindowsPrivacyChangeReason.DefaultSpeaker));
        await fixture.ViewModel.ExecuteOutputDeviceCommandAsync(new(AppearanceCommandOperation.Clarify, Error: "Exact output syntax"), SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        fixture.ViewModel.ResponseBody.Should().Be("Exact output syntax");
        fixture.TextToSpeech.OutputEnumerationException = null;
        await fixture.ViewModel.SaveOutputDeviceCommand.ExecuteAsync();
        fixture.ViewModel.ResponseBody.Should().Contain("Refresh output metadata");
        await fixture.RunAsync("list output settings");
        fixture.ViewModel.ResponseTitle.Should().Be("Output configuration.");
    }

    [Fact]
    public async Task Protected_voice_control_denial_and_host_changes_during_detection_cannot_save_or_reveal()
    {
        var fixture = new Fixture(enableOutputConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        fixture.Voice.Microphones = [new("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.SetVoiceConsentAsync(true);
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        await fixture.ViewModel.SetAllowVoiceActivationDuringCallsAsync(false);
        fixture.CallState.SetState(CallState.Unknown);
        await fixture.Dispatcher.LastInvocation;
        await fixture.ViewModel.ExecuteOutputDeviceCommandAsync(new(AppearanceCommandOperation.Reset), SecurityAuditInitiator.VoiceCommand, TestContext.Current.CancellationToken);
        fixture.ViewModel.ResponseTitle.Should().Be("Output preference not confirmed.");
        fixture.ViewModel.AudioOutputConfigurationStatus.Should().Contain("\"source\":\"default\"");
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request))
        {
            await fixture.ViewModel.ExecuteOutputDeviceCommandAsync(new(AppearanceCommandOperation.Set, "0"), SecurityAuditInitiator.VoiceCommand, TestContext.Current.CancellationToken);
        }
        fixture.ViewModel.ResponseTitle.Should().Be("Output preference not confirmed.");
        fixture.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
        fixture.Audit.BeforeWrite = item =>
        {
            if (string.Equals(item.ActionId, "configuration.audio-output", StringComparison.Ordinal)
                && item.Outcome == SecurityAuditOutcome.Requested) { fixture.CallState.SetState(CallState.Active); }
        };
        await fixture.ViewModel.ExecuteOutputDeviceCommandAsync(new(AppearanceCommandOperation.Reset), SecurityAuditInitiator.LocalUser, TestContext.Current.CancellationToken);
        fixture.ViewModel.ResponseTitle.Should().Be("Output preference denied.");
    }

    [Fact]
    public async Task Disposed_host_during_committed_choice_does_not_publish_late_success()
    {
        var fixture = new Fixture(enableOutputConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("list output settings");
        var before = fixture.ViewModel.ResponseBody;
        fixture.Audit.BeforeWrite = item =>
        {
            if (string.Equals(item.ActionId, "configuration.audio-output", StringComparison.Ordinal) && item.Outcome == SecurityAuditOutcome.Succeeded)
            {
                fixture.ViewModel.Dispose();
            }
        };
        await fixture.RunAsync("set speech.output-device to 0");
        fixture.AudioPreferences.SavedOutputDeviceId.Should().Be("0");
        fixture.ViewModel.ResponseBody.Should().Be(before);
        fixture.ViewModel.CanChangeAudioOutputDevice.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public async Task Output_cancelled_or_failed_playback_preserves_full_response_behind_locked_privacy(
        bool cancellation, bool pinned, bool locked)
    {
        var fixture = new Fixture(subscribeToWindowActions: locked, enableOutputConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        if (pinned) { await LoadSavedOutputAsync(fixture, "0"); }
        fixture.ViewModel.TaskResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.WindowActions.Clear();
        fixture.TextToSpeech.BeforeSpeak = () => fixture.Session.IsUnlocked = !locked;
        fixture.TextToSpeech.SpeakException = cancellation ? new OperationCanceledException()
            : new AudioOutputDeviceUnavailableException(AudioOutputFailureReason.Unavailable, "owned output open failure");
        await fixture.RunAsync("what power action is pending");
        fixture.ViewModel.ResponseTitle.Should().Be("No power action is pending.");
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.CanRevealPrivatePresentation.Should().Be(!locked);
        fixture.WindowActions.Should().NotContain(WindowAction.Show);
    }

    [Fact]
    public async Task Native_single_control_and_disposed_metadata_callback_never_publish_stale_choices()
    {
        var fixture = new Fixture(enableOutputConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        Task? denied = null;
        fixture.TextToSpeech.BeforeOutputEnumeration = () =>
        {
            fixture.ViewModel.CanChangeAudioOutputDevice.Should().BeFalse();
            denied = fixture.ViewModel.SaveOutputDeviceCommand.ExecuteAsync();
        };
        await fixture.ViewModel.RefreshOutputDevicesCommand.ExecuteAsync();
        await denied!;
        fixture.Dispatcher.BeforePost = fixture.ViewModel.Dispose;
        fixture.OutputConfiguration!.Observe(new([], null));
        fixture.ViewModel.CanChangeAudioOutputDevice.Should().BeFalse();
        fixture.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
    }

    [Theory]
    [InlineData(typeof(IOException), false)]
    [InlineData(typeof(UnauthorizedAccessException), false)]
    [InlineData(typeof(InvalidOperationException), false)]
    [InlineData(typeof(OperationCanceledException), false)]
    [InlineData(typeof(TimeoutException), false)]
    [InlineData(typeof(IOException), true)]
    public async Task Exact_command_detection_failure_or_disposal_is_not_success_shaped(Type exceptionType, bool dispose)
    {
        var fixture = new Fixture(enableOutputConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.OutputEnumerationException = (Exception)Activator.CreateInstance(exceptionType)!;
        if (dispose) { fixture.TextToSpeech.BeforeOutputEnumeration = fixture.ViewModel.Dispose; }
        await fixture.RunAsync("list output settings");
        if (!dispose) { fixture.ViewModel.ResponseTitle.Should().Be("Output preference not confirmed."); }
        fixture.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Exact_output_save_during_spoken_response_stops_retired_playback_and_keeps_full_visual_result()
    {
        var fixture = new Fixture(enableOutputConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("list output settings");
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = fixture.RunAsync("what power action is pending");
        await fixture.TextToSpeech.SpeakStarted.Task;
        var body = fixture.ViewModel.ResponseBody;
        await fixture.ViewModel.ExecuteOutputDeviceCommandAsync(new(AppearanceCommandOperation.Reset), SecurityAuditInitiator.LocalUser, TestContext.Current.CancellationToken);
        fixture.TextToSpeech.SpeakGate.TrySetResult();
        await pending;
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.ViewModel.Transcript.Should().Contain("\"outcome\":\"saved\"");
        fixture.AudioPreferences.OutputDeviceId.Should().BeNull();
        fixture.TextToSpeech.StopCalls.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Synthetic_pinned_preview_failure_has_explicit_recovery_without_endpoint_substitution()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await using var admission = fixture.OutputAdmission;
        await LoadSavedOutputAsync(fixture, "0");
        fixture.TextToSpeech.SpeakException = new AudioOutputDeviceUnavailableException(AudioOutputFailureReason.PlaybackFailed, "owned synthetic failure");
        await fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        fixture.ViewModel.SelectedOutputDevice.Should().BeNull();
        fixture.AudioPreferences.OutputDeviceId.Should().Be("0");
        fixture.ViewModel.ResponseTitle.Should().Be("The selected audio output is unavailable.");
    }
}
