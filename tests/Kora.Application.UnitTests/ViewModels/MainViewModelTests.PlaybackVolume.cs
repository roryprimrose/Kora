using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Commands;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Communication;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private sealed class FakePlaybackVolumePreferences : IPlaybackVolumePreferences
    {
        public PlaybackVolume? Value { get; set; }
        public Exception? ReadFailure { get; set; }
        public Exception? WriteFailure { get; set; }
        public Action? AfterWrite { get; set; }
        public bool Pending { get; set; }
        public PlaybackVolume? Load()
        {
            if (Pending) { throw new InvalidDataException("Unconfirmed write"); }
            return ReadBack();
        }
        public PlaybackVolume? ReadBack() { if (ReadFailure is { } failure) { throw failure; } return Value; }
        public void BeginWrite() => Pending = true;
        public void ConfirmWrite() => Pending = false;
        public void Save(PlaybackVolume volume) { if (WriteFailure is { } failure) { throw failure; } Value = volume; AfterWrite?.Invoke(); }
        public void Reset() { Value = null; AfterWrite?.Invoke(); }
    }

    [Fact]
    public async Task Native_typed_and_current_name_activated_discovery_get_set_reset_share_audited_volume_without_synthesis()
    {
        var fixture = new Fixture(enablePlaybackVolume: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        var microphones = fixture.Voice.StartCalls;
        await fixture.RunAsync("Kora, list volume settings");
        fixture.ViewModel.ResponseBody.Should().Contain("speech.playback-volume").And.Contain("\"minimum\":0").And.Contain("\"maximum\":100");
        await fixture.RunAsync("set speech.playback-volume to 1");
        fixture.VolumePreferences.Value.Should().Be(new PlaybackVolume(1));
        await fixture.RunAsync("status speech.playback-volume");
        fixture.ViewModel.ResponseBody.Should().Contain("\"source\":\"saved\"");
        fixture.ViewModel.SelectedPlaybackVolume = 0;
        await fixture.ViewModel.SavePlaybackVolumeCommand.ExecuteAsync();
        fixture.VolumePreferences.Value.Should().Be(new PlaybackVolume(0));
        fixture.ViewModel.PlaybackVolumeChoices.Should().HaveCount(101);
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        await fixture.ViewModel.RefreshPlaybackVolumeCommand.ExecuteAsync();
        await fixture.ViewModel.ResetPlaybackVolumeCommand.ExecuteAsync();
        fixture.VolumePreferences.Value.Should().BeNull();
        fixture.ViewModel.SelectedPlaybackVolume.Should().Be(100);
        fixture.ViewModel.PlaybackVolumeStatus.Should().Contain("\"source\":\"default\"");
        fixture.Voice.StartCalls.Should().Be(microphones);
        await fixture.RunAsync("set assistant.name to Nova");
        fixture.TextToSpeech.ClearSpokenResponse();
        await fixture.RaiseActivatedTranscriptAsync("Nova, set speech.playback-volume to 30", 1);
        fixture.VolumePreferences.Value.Should().Be(new PlaybackVolume(30));
        await fixture.RaiseActivatedTranscriptAsync("Nova, reset speech.playback-volume", 1);
        fixture.VolumePreferences.Value.Should().BeNull();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.TextToSpeech.InstallProviderCalls.Should().Be(0);
        fixture.TextToSpeech.RemoveProviderCalls.Should().Be(0);
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
        fixture.Preferences.SavedVoiceId.Should().BeNull();
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task Zero_blocks_ordinary_and_preview_synthesis_and_raise_reset_never_replays_output()
    {
        var fixture = new Fixture(enablePlaybackVolume: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("set speech.playback-volume to 0");
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.ViewModel.TaskResponseMode = ResponseOutputMode.VoiceOnly;
        await fixture.RunAsync("what power action is pending");
        fixture.ViewModel.ResponseTitle.Should().Be("No power action is pending.");
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        await fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        fixture.ViewModel.ResponseBody.Should().Contain("Kora playback volume is zero or unavailable");
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        await fixture.RunAsync("set speech.playback-volume to 100");
        await fixture.RunAsync("reset speech.playback-volume");
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeTrue();
        await fixture.RunAsync("what power action is pending");
        fixture.TextToSpeech.SpokenText.Should().Contain("No power action");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saving_volume_retires_inflight_output_retains_complete_response_and_never_changes_capture(bool failure)
    {
        var fixture = new Fixture(enablePlaybackVolume: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var speaking = fixture.RunAsync("what power action is pending");
        await fixture.TextToSpeech.SpeakStarted.Task;
        var response = fixture.ViewModel.ResponseBody;
        var starts = fixture.Voice.StartCalls;
        fixture.ViewModel.SelectedPlaybackVolume = 0;
        if (failure) { fixture.VolumePreferences.WriteFailure = new IOException("owned persistence failure"); }
        await fixture.ViewModel.SavePlaybackVolumeCommand.ExecuteAsync();
        fixture.TextToSpeech.SpeakGate.TrySetResult();
        await speaking;
        fixture.ViewModel.ResponseBody.Should().Be(response);
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.Voice.StartCalls.Should().Be(starts);
        fixture.ViewModel.Transcript.Should().Contain(failure ? "not confirmed" : "speech.playback-volume");
        fixture.VolumePreferences.WriteFailure = null;
        fixture.VolumePreferences.Pending = false;
        await fixture.ViewModel.ResetPlaybackVolumeCommand.ExecuteAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Fact]
    public async Task Invalid_saved_volume_and_failed_refresh_never_default_or_silently_activate_output()
    {
        var fixture = new Fixture(enablePlaybackVolume: true, volumeReadFailure: new InvalidDataException("corrupt gain"));
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();
        fixture.ViewModel.PlaybackVolumeStatus.Should().Contain("unconfirmed");
        fixture.VolumePreferences.ReadFailure = new UnauthorizedAccessException();
        await fixture.RunAsync("get speech.playback-volume");
        fixture.ViewModel.ResponseTitle.Should().Be("Volume preference not confirmed.");
        fixture.VolumePreferences.ReadFailure = null;
        await fixture.RunAsync("get speech.playback-volume");
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeTrue();
        await fixture.RunAsync("set speech.playback-volume to 01");
        fixture.ViewModel.ResponseTitle.Should().Contain("exact playback percent");
        fixture.VolumePreferences.Value.Should().BeNull();
        await fixture.RunAsync("get speech.playback-volume trailing");
        fixture.ViewModel.ResponseTitle.Should().Be("Clarify the volume setting.");
        await fixture.ViewModel.ExecutePlaybackVolumeCommandAsync(new(AppearanceCommandOperation.Set),
            SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        fixture.ViewModel.ResponseTitle.Should().Be("Volume preference not confirmed.");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await fixture.ViewModel.ExecutePlaybackVolumeCommandAsync(new(AppearanceCommandOperation.Get),
            SecurityAuditInitiator.TypedCommand, cancelled.Token);
        fixture.ViewModel.PlaybackVolumeStatus.Should().Contain("\"source\":\"unavailable\"");
    }

    [Fact]
    public async Task Pending_exact_approval_and_unbound_locked_disposed_hosts_cannot_mutate_volume()
    {
        var unbound = new Fixture();
        await using var unboundAdmission = unbound.OutputAdmission;
        unbound.ViewModel.PlaybackVolumeStatus.Should().Contain("unavailable");
        await unbound.ViewModel.RefreshPlaybackVolumeCommand.ExecuteAsync();
        unbound.ViewModel.Transcript.Should().Contain("owning unlocked host");
        var fixture = new Fixture(enablePlaybackVolume: true);
        await using var admission = fixture.OutputAdmission;
        fixture.Probe.Status = new("local.inference", "Local model inference (Ollama)", Kora.Core.Dependencies.DependencyReadiness.Ready, "ready");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("lock this workstation please");
        await fixture.ViewModel.ActiveReasoningTask!;
        var preview = fixture.ViewModel.ResponseBody;
        await fixture.RunAsync("reset speech.playback-volume");
        fixture.ViewModel.CanChangePlaybackVolume.Should().BeFalse();
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        fixture.ViewModel.ResponseBody.Should().Be(preview);
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.Dispose();
        await fixture.ViewModel.RefreshPlaybackVolumeCommand.ExecuteAsync();
        fixture.VolumeConfiguration!.HoldUnavailable();
        fixture.ViewModel.ResponseBody.Should().Be(preview);
    }

    [Fact]
    public async Task Activated_origin_under_protected_unknown_call_or_relabelled_context_cannot_write()
    {
        var fixture = new Fixture(enablePlaybackVolume: true);
        await using var admission = fixture.OutputAdmission;
        fixture.Voice.Microphones = [new("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.SetVoiceConsentAsync(true);
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        await fixture.ViewModel.SetAllowVoiceActivationDuringCallsAsync(false);
        fixture.CallState.SetState(CallState.Unknown);
        await fixture.Dispatcher.LastInvocation;
        await fixture.ViewModel.ExecutePlaybackVolumeCommandAsync(new(AppearanceCommandOperation.Set, "0"),
            SecurityAuditInitiator.VoiceCommand, TestContext.Current.CancellationToken);
        fixture.VolumePreferences.Value.Should().BeNull();
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request))
        {
            await fixture.ViewModel.ExecutePlaybackVolumeCommandAsync(new(AppearanceCommandOperation.Set, "0"),
                SecurityAuditInitiator.VoiceCommand, TestContext.Current.CancellationToken);
        }
        fixture.VolumePreferences.Value.Should().BeNull();
        await fixture.RunAsync("reset speech.playback-volume");
        fixture.VolumePreferences.Value.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_volume_failure_keeps_independent_voice_and_output_selection(bool preview)
    {
        var fixture = new Fixture(enablePlaybackVolume: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        var voice = fixture.ViewModel.SelectedVoice;
        var output = fixture.ViewModel.SelectedOutputDevice;
        fixture.TextToSpeech.SpeakException = new PlaybackVolumeUnavailableException("owned unavailable gain");
        if (preview) { await fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync(); }
        else { await fixture.RunAsync("what power action is pending"); }
        fixture.ViewModel.SelectedVoice.Should().Be(voice);
        fixture.ViewModel.SelectedOutputDevice.Should().Be(output);
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
    }

    [Fact]
    public async Task Audit_changed_host_or_disposal_and_busy_native_request_never_publish_late_success()
    {
        var fixture = new Fixture(enablePlaybackVolume: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.Audit.BeforeWrite = item =>
        {
            if (item.Outcome == SecurityAuditOutcome.Requested && string.Equals(item.ActionId, "configuration.playback-volume", StringComparison.Ordinal))
            {
                fixture.ViewModel.RefreshPlaybackVolumeCommand.ExecuteAsync().GetAwaiter().GetResult();
            }
        };
        await fixture.RunAsync("set speech.playback-volume to 1");
        fixture.VolumePreferences.Value.Should().Be(new PlaybackVolume(1));
        fixture.Audit.BeforeWrite = item =>
        {
            if (item.Outcome == SecurityAuditOutcome.Succeeded && string.Equals(item.ActionId, "configuration.playback-volume", StringComparison.Ordinal)) { fixture.ViewModel.Dispose(); }
        };
        var before = fixture.ViewModel.ResponseBody;
        await fixture.RunAsync("set speech.playback-volume to 0");
        fixture.VolumePreferences.Value.Should().Be(new PlaybackVolume(0));
        fixture.ViewModel.ResponseBody.Should().Be(before);
        fixture.ViewModel.CanChangePlaybackVolume.Should().BeFalse();
    }

    [Fact]
    public async Task Confirmed_notification_call_change_does_not_publish_stale_command_success()
    {
        var fixture = new Fixture(enablePlaybackVolume: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        var original = fixture.ViewModel.ResponseBody;
        fixture.VolumeConfiguration!.Changed += (_, _) =>
        {
            if (fixture.VolumePreferences.Value is not null) { fixture.CallState.SetState(CallState.Active); }
        };
        await fixture.RunAsync("set speech.playback-volume to 1");
        fixture.VolumePreferences.Value.Should().Be(new PlaybackVolume(1));
        fixture.ViewModel.ResponseBody.Should().Be(original);
    }

    [Fact]
    public async Task Invalid_volume_while_speaking_retains_the_original_complete_visual_response()
    {
        var fixture = new Fixture(enablePlaybackVolume: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var speaking = fixture.RunAsync("what power action is pending");
        await fixture.TextToSpeech.SpeakStarted.Task;
        var original = fixture.ViewModel.ResponseBody;
        await fixture.ViewModel.ExecutePlaybackVolumeCommandAsync(new(AppearanceCommandOperation.Set, "101"),
            SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        fixture.TextToSpeech.SpeakGate.TrySetResult();
        await speaking;
        fixture.ViewModel.ResponseBody.Should().Be(original);
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.VolumePreferences.Value.Should().BeNull();
    }

    [Fact]
    public async Task Protected_call_denies_volume_mutation_even_when_activated_input_itself_remains_eligible()
    {
        var fixture = new Fixture(enablePlaybackVolume: true);
        await using var admission = fixture.OutputAdmission;
        fixture.Voice.Microphones = [new("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.SetVoiceConsentAsync(true);
        if (!fixture.ViewModel.IsVoiceEnabled) { await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
        fixture.CallState.SetState(CallState.Active);
        await fixture.Dispatcher.LastInvocation;
        await fixture.ViewModel.ExecutePlaybackVolumeCommandAsync(new(AppearanceCommandOperation.Set, "0"),
            SecurityAuditInitiator.VoiceCommand, TestContext.Current.CancellationToken);
        fixture.VolumePreferences.Value.Should().BeNull();
        fixture.ViewModel.ResponseBody.Should().Contain("\"outcome\":\"denied\"");
        fixture.ViewModel.SelectedPlaybackVolume.Should().Be(100);
    }

    [Fact]
    public async Task Call_revision_change_during_output_cleanup_preserves_original_response_without_late_success()
    {
        var fixture = new Fixture(enablePlaybackVolume: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var speaking = fixture.RunAsync("what power action is pending");
        await fixture.TextToSpeech.SpeakStarted.Task;
        var original = fixture.ViewModel.ResponseBody;
        fixture.TextToSpeech.BeforeStop = () => fixture.CallState.SetState(CallState.Active);
        await fixture.ViewModel.ExecutePlaybackVolumeCommandAsync(new(AppearanceCommandOperation.Set, "0"),
            SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        fixture.TextToSpeech.SpeakGate.TrySetResult();
        await speaking;
        fixture.VolumePreferences.Value.Should().Be(new PlaybackVolume(0));
        fixture.ViewModel.ResponseBody.Should().Be(original);
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("owner")]
    [InlineData("topology")]
    public async Task Original_native_host_privacy_and_topology_are_revalidated_after_requested_audit(string changed)
    {
        var fixture = new Fixture(enablePlaybackVolume: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.Audit.BeforeWrite = item =>
        {
            if (item.Outcome != SecurityAuditOutcome.Requested || !string.Equals(item.ActionId, "configuration.playback-volume", StringComparison.Ordinal)) { return; }
            if (changed is "locked") { fixture.Session.IsUnlocked = false; }
            if (changed is "owner") { fixture.ViewModel.BindCallOwnershipGate(static () => false); }
            if (changed is "topology") { fixture.PrivacyObservation.Current = fixture.PrivacyObservation.Current with { TopologyRevision = 100 }; }
        };
        await fixture.RunAsync("set speech.playback-volume to 0");
        fixture.VolumePreferences.Value.Should().BeNull();
        fixture.TextToSpeech.Volume.Should().Be(PlaybackVolume.Default);
    }
}
