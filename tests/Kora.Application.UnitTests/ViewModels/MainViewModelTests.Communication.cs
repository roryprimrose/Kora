using AwesomeAssertions;
using Kora.Core.Commands;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(CallState.Unavailable, false)]
    [InlineData(CallState.Clear, false)]
    [InlineData(CallState.Active, true)]
    [InlineData(CallState.Suspected, true)]
    [InlineData(CallState.Unknown, true)]
    [InlineData((CallState)99, true)]
    public async Task Native_manual_controls_layer_with_automatic_evidence(CallState automatic, bool remainsProtected)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        if (Enum.IsDefined(automatic)) { fixture.CallState.SetState(automatic); }
        else { fixture.CallState.SetInvalidObservation(); }
        await fixture.ViewModel.CallClosureTask;
        await fixture.ViewModel.EnableManualCallCommand.ExecuteAsync();
        fixture.ViewModel.IsManualCallActive.Should().BeTrue();
        fixture.ViewModel.CallManualStatus.Should().Contain("active for this run");
        fixture.ViewModel.CallStateStatus.Should().StartWith("Manual call mode");
        fixture.ViewModel.CallObservation.ManualActive.Should().BeTrue();
        fixture.ViewModel.AutomaticCallState.Should().Be(automatic);
        fixture.ViewModel.CurrentCallState.Should().Be(CallState.Active);
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeFalse();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.AreReusableGrantsIgnored.Should().BeTrue();
        await fixture.ViewModel.ClearManualCallCommand.ExecuteAsync();
        fixture.ViewModel.IsManualCallActive.Should().BeFalse();
        fixture.ViewModel.IsProtectedCall.Should().Be(remainsProtected);
        fixture.ViewModel.CurrentCallState.Should().Be(automatic);
        fixture.ViewModel.IsCallDetected.Should().Be(automatic is CallState.Active or CallState.Suspected);
        fixture.CallPreferences.SavedSettings.Should().BeNull();
        fixture.ViewModel.CallProtectionLimitations.Should().Contain("unavailable");
        fixture.ViewModel.CallManualStatus.Should().Contain("not saved");
        if (!Enum.IsDefined(automatic)) { fixture.ViewModel.CallStateStatus.Should().Contain("invalid"); }
        fixture.ViewModel.CanEnableCallVisualProtection.Should().BeFalse();
        fixture.ViewModel.CanDisableCallVoiceActivation.Should().BeTrue();
        fixture.ViewModel.CallVoiceActivationButtonText.Should().Be("Disable voice activation during calls");
    }

    [Fact]
    public async Task Voice_manual_clear_cannot_be_relabelled_by_later_UI_confirmation()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.EnableManualCallCommand.ExecuteAsync();
        using (var voice = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request))
        {
            await fixture.ViewModel.SetManualCallAsync(false, RequestOrigin.LocalUi, fixture.ViewModel.CallPolicyRevision,
                TestContext.Current.CancellationToken);
            await fixture.ViewModel.ClearManualCallCommand.ExecuteAsync();
            await fixture.ViewModel.SetShowVisualTextDuringCallsAsync(false);
            await fixture.ViewModel.SetAllowVoiceActivationDuringCallsAsync(false);
        }
        fixture.ViewModel.IsManualCallActive.Should().BeTrue();
        fixture.ViewModel.ShowVisualTextDuringCalls.Should().BeTrue();
        fixture.ViewModel.AllowVoiceActivationDuringCalls.Should().BeTrue();
        fixture.CallPreferences.SavedSettings.Should().BeNull();
        fixture.ViewModel.ResponseBody.Should().Contain("later confirmation");
        await fixture.ViewModel.ClearManualCallCommand.ExecuteAsync();
        fixture.ViewModel.IsManualCallActive.Should().BeFalse();
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData((RequestOrigin)99)]
    public async Task Unknown_original_manual_request_fails_closed(RequestOrigin origin)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        if (!Enum.IsDefined(origin))
        {
            var invalid = () => fixture.ViewModel.SetManualCallAsync(true, origin, fixture.ViewModel.CallPolicyRevision,
                TestContext.Current.CancellationToken);
            await invalid.Should().ThrowAsync<ArgumentOutOfRangeException>();
            fixture.ViewModel.IsManualCallActive.Should().BeFalse();
            return;
        }
        await fixture.ViewModel.SetManualCallAsync(true, origin, fixture.ViewModel.CallPolicyRevision, TestContext.Current.CancellationToken);
        fixture.ViewModel.IsManualCallActive.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Call-sensitive change was not applied.");
    }

    [Fact]
    public async Task Manual_change_rechecks_revision_ownership_privacy_cancellation_and_disposal()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var revision = fixture.ViewModel.CallPolicyRevision;
        fixture.CallState.SetState(CallState.Unknown);
        await fixture.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi, revision, TestContext.Current.CancellationToken);
        fixture.ViewModel.IsManualCallActive.Should().BeFalse();
        fixture.ViewModel.ResponseBody.Should().Contain("Call policy changed");
        fixture.ViewModel.BindCallOwnershipGate(static () => false);
        await fixture.ViewModel.EnableManualCallCommand.ExecuteAsync();
        fixture.ViewModel.IsManualCallActive.Should().BeFalse();
        fixture.ViewModel.BindCallOwnershipGate(static () => true);
        fixture.Session.IsUnlocked = false;
        await fixture.ViewModel.EnableManualCallCommand.ExecuteAsync();
        fixture.ViewModel.IsManualCallActive.Should().BeFalse();
        fixture.Session.IsUnlocked = true;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var cancelled = () => fixture.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi,
            fixture.ViewModel.CallPolicyRevision, cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        fixture.ViewModel.Dispose();
        await fixture.ViewModel.EnableManualCallCommand.ExecuteAsync();
        fixture.CallState.SetState(CallState.Active);
        fixture.ViewModel.IsManualCallActive.Should().BeFalse();
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Unknown)]
    public async Task All_voice_option_mutations_are_denied_while_protected_but_safety_and_status_remain(CallState state)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.CallState.SetState(state);
        using var voice = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request);
        var microphone = fixture.ViewModel.SelectedMicrophone;
        var output = fixture.ViewModel.SelectedOutputDevice;
        var provider = fixture.ViewModel.SelectedSpeechProvider;
        var selectedVoice = fixture.ViewModel.SelectedVoice;
        fixture.ViewModel.SelectedMicrophone = null;
        fixture.ViewModel.SelectedOutputDevice = null;
        fixture.ViewModel.SelectedSpeechProvider = null;
        fixture.ViewModel.SelectedVoice = null;
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VisualOnly;
        fixture.ViewModel.QueueResponseMode = ResponseOutputMode.VisualOnly;
        fixture.ViewModel.TaskResponseMode = ResponseOutputMode.VisualOnly;
        fixture.ViewModel.FallbackToVisualWhenOutputMuted = false;
        fixture.ViewModel.RequireAssistantNameForVoiceApproval = false;
        await fixture.ViewModel.SetAssistantNameAsync("Nova");
        await fixture.ViewModel.SetVoiceConsentAsync(false);
        fixture.ViewModel.SelectedMicrophone.Should().Be(microphone);
        fixture.ViewModel.SelectedOutputDevice.Should().Be(output);
        fixture.ViewModel.SelectedSpeechProvider.Should().Be(provider);
        fixture.ViewModel.SelectedVoice.Should().Be(selectedVoice);
        fixture.ViewModel.DefaultResponseMode.Should().Be(ResponseOutputMode.Hybrid);
        fixture.ViewModel.QueueResponseMode.Should().BeNull();
        fixture.ViewModel.TaskResponseMode.Should().BeNull();
        fixture.ViewModel.FallbackToVisualWhenOutputMuted.Should().BeTrue();
        fixture.ViewModel.AssistantName.Should().Be("Kora");
        fixture.ViewModel.RequireAssistantNameForVoiceApproval.Should().BeTrue();
        fixture.VoiceConsent.Consent.Should().BeTrue();
        await fixture.ViewModel.ExecuteAsync(fixture.Catalog.GetCommands().Single(c => c.Action == BuiltInAction.StopSpeaking));
        await fixture.ViewModel.CancelCurrentTaskAsync();
        await fixture.ViewModel.ExecuteAsync(fixture.Catalog.GetCommands().Single(c => c.Action == BuiltInAction.ShowStatus));
        fixture.ViewModel.IsProtectedCall.Should().BeTrue();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Fact]
    public async Task Call_entry_closes_pending_output_before_any_UI_dispatch_and_clear_never_replays_it()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = fixture.RunAsync("what power action is pending");
        await fixture.TextToSpeech.SpeakStarted.Task;
        fixture.Dispatcher.BeforeInvoke = () =>
        {
            fixture.Events.Should().Contain("speech.invalidate");
            fixture.TextToSpeech.IsSpeaking.Should().BeFalse();
            fixture.TextToSpeech.StopCalls.Should().Be(1);
        };
        fixture.CallState.SetState(CallState.Unknown);
        await fixture.ViewModel.CallClosureTask;
        await response;
        var speech = fixture.TextToSpeech.SpokenText;
        fixture.CallState.SetState(CallState.Clear);
        await fixture.ViewModel.CallClosureTask;
        fixture.TextToSpeech.SpokenText.Should().Be(speech);
        fixture.ViewModel.IsSpeaking.Should().BeFalse();
    }

    [Fact]
    public async Task Preview_and_provider_start_respect_call_policy_even_when_call_enters_at_start()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.TextToSpeech.BeforeSpeak = () => fixture.CallState.SetState(CallState.Active);
        await fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.TextToSpeech.BeforeSpeak = null;
        await fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        fixture.ViewModel.ResponseTitle.Should().Be("Voice preview is suppressed.");
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Fact]
    public async Task Stale_call_UI_callback_and_post_disposal_observation_cannot_reopen_output()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Dispatcher.BeforeInvoke = () => fixture.CallState.SetState(CallState.Unknown);
        fixture.CallState.SetState(CallState.Active);
        await fixture.ViewModel.CallClosureTask;
        fixture.ViewModel.CurrentCallState.Should().Be(CallState.Unknown);
        fixture.ViewModel.IsProtectedCall.Should().BeTrue();
        fixture.ViewModel.Dispose();
        fixture.CallState.SetState(CallState.Clear);
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Protected_calls_ignore_legacy_reuse_without_revoking_and_pre_call_approvals_are_stale()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local model", DependencyReadiness.Ready, "Ready");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        fixture.ApprovalPreferences.Save(new(true, [BuiltInAction.LockMachine]));
        await fixture.ViewModel.InitializeAsync();
        fixture.CallState.SetState(CallState.Unknown);
        await fixture.RunAsync("please secure this computer");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        fixture.ViewModel.AlwaysAllowedModelActions.Should().ContainSingle();
        fixture.ViewModel.GetGrantDocument().Should().Contain("Ignored during call");
        await fixture.ViewModel.ApproveModelActionAlwaysCommand.ExecuteAsync();
        fixture.Session.LockCalls.Should().Be(0);
        fixture.CallState.SetState(CallState.Clear);
        await fixture.ViewModel.ApproveModelActionCommand.ExecuteAsync();
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
        await fixture.RunAsync("please secure this computer");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.Session.LockCalls.Should().Be(1);
    }

    [Fact]
    public async Task Voice_initiator_without_ambient_context_and_unknown_ownership_are_denied()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.CallState.SetState(CallState.Suspected);
        await fixture.ViewModel.SetAssistantNameAsync("Nova", SecurityAuditInitiator.VoiceCommand);
        await fixture.ViewModel.SetAssistantNameAsync("Nova", SecurityAuditInitiator.System);
        await fixture.ViewModel.SetAssistantNameAsync("Nova", SecurityAuditInitiator.ModelSuggestion);
        fixture.ViewModel.AssistantName.Should().Be("Kora");
        fixture.ViewModel.BindCallOwnershipGate(static () => false);
        await fixture.ViewModel.SetAllowVoiceActivationDuringCallsAsync(false);
        fixture.ViewModel.AllowVoiceActivationDuringCalls.Should().BeTrue();
    }

    [Fact]
    public async Task Known_typed_name_request_is_admitted_without_ambient_context()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.SetAssistantNameAsync("Nova", SecurityAuditInitiator.TypedCommand);
        fixture.ViewModel.AssistantName.Should().Be("Nova");
    }

    [Fact]
    public async Task Settings_and_manual_controls_recheck_call_revision_after_audit_admission()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Audit.BeforeWrite = audit =>
        {
            if (audit.Outcome == SecurityAuditOutcome.Requested) { fixture.CallState.SetState(CallState.Unknown); }
        };
        await fixture.ViewModel.SetAllowVoiceActivationDuringCallsAsync(false);
        fixture.CallPreferences.SavedSettings.Should().BeNull();
        fixture.ViewModel.AllowVoiceActivationDuringCalls.Should().BeTrue();
        fixture.CallState.SetState(CallState.Clear);
        await fixture.ViewModel.EnableManualCallCommand.ExecuteAsync();
        fixture.ViewModel.IsManualCallActive.Should().BeFalse();
        fixture.ViewModel.ResponseBody.Should().Contain("Call policy changed");
    }

    [Fact]
    public async Task Settings_save_failure_retains_both_preference_and_effective_policy()
    {
        var fixture = new Fixture();
        fixture.CallPreferences.Settings = new(false, false);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.CanEnableCallVisualProtection.Should().BeTrue();
        fixture.ViewModel.CanDisableCallVoiceActivation.Should().BeFalse();
        fixture.ViewModel.CallVoiceActivationStatus.Should().StartWith("Off");
        fixture.ViewModel.CallVoiceActivationButtonText.Should().Be("Keep voice activation during calls");
        fixture.ViewModel.CallVisualOverrideStatus.Should().StartWith("Off");
        fixture.CallPreferences.SaveException = new IOException("cannot write");
        await fixture.ViewModel.SetShowVisualTextDuringCallsAsync(true);
        fixture.ViewModel.ShowVisualTextDuringCalls.Should().BeFalse();
        fixture.CallPreferences.Settings.Should().Be(new CallAwareSettings(false, false));
        fixture.ViewModel.ResponseTitle.Should().Be("The call-aware settings could not be saved.");
        fixture.CallPreferences.SaveException = null;
        await fixture.ViewModel.SetShowVisualTextDuringCallsAsync(false);
        await fixture.ViewModel.SetShowVisualTextDuringCallsAsync(true);
        fixture.ViewModel.ShowVisualTextDuringCalls.Should().BeTrue();
        fixture.ViewModel.CallVisualOverrideStatus.Should().StartWith("On");
    }

    [Fact]
    public async Task Native_observer_does_not_publish_after_disposal_or_reveal_locked_call_content()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Session.IsUnlocked = false;
        fixture.WindowActions.Clear();
        fixture.CallState.SetState(CallState.Active);
        await fixture.ViewModel.CallClosureTask;
        fixture.WindowActions.Should().NotContain(Kora.Application.ViewModels.WindowAction.Show);
        fixture.Session.IsUnlocked = true;
        fixture.Dispatcher.BeforeInvoke = fixture.ViewModel.Dispose;
        fixture.CallState.SetState(CallState.Suspected);
        await fixture.ViewModel.CallClosureTask;
        fixture.ViewModel.CurrentCallState.Should().Be(CallState.Active);

        var failing = await Fixture.CreateInitializedAsync();
        failing.TextToSpeech.BeforeStop = () =>
        {
            failing.ViewModel.Dispose();
            throw new InvalidOperationException("late stop failed");
        };
        failing.CallState.SetState(CallState.Active);
        var closure = () => failing.ViewModel.CallClosureTask;
        await closure.Should().ThrowAsync<InvalidOperationException>();
        failing.ViewModel.ResponseTitle.Should().NotBe("The call-aware output policy could not be applied.");
    }

    [Fact]
    public async Task Model_dispatch_without_an_admitted_call_revision_is_denied()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ExecuteAsync(fixture.Catalog.GetCommands().Single(c => c.Action == BuiltInAction.LockMachine),
            SecurityAuditInitiator.ModelSuggestion);
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.ResponseTitle.Should().Be("Call policy changed.");
    }

    [Theory]
    [InlineData(BuiltInAction.LockMachine)]
    [InlineData(BuiltInAction.RestartApplication)]
    public async Task Legacy_reusable_authorization_is_rechecked_after_asynchronous_audio_shutdown(BuiltInAction action)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local model", DependencyReadiness.Ready, "Ready");
        fixture.Reasoner.Action = action;
        fixture.ApprovalPreferences.Save(new(true, [action]));
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.BeforeStop = () => fixture.CallState.SetState(CallState.Unknown);
        await fixture.RunAsync("carry out the requested action");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.Session.LockCalls.Should().Be(0);
        fixture.Events.Should().NotContain("process.restart");
        fixture.Audit.Events.Should().Contain(e => e.Outcome == SecurityAuditOutcome.Denied && e.ReasonCode == "call-policy-changed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reuse_or_ownership_loss_between_model_decision_and_dispatch_does_not_execute(bool ownershipLost)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local model", DependencyReadiness.Ready, "Ready");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        fixture.ApprovalPreferences.Save(new(true, [BuiltInAction.LockMachine]));
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(fixture.ViewModel.IsLocalTaskCancellable), StringComparison.Ordinal)
                && !fixture.ViewModel.IsLocalTaskCancellable)
            {
                if (ownershipLost) { fixture.ViewModel.BindCallOwnershipGate(static () => false); }
                else { fixture.CallState.SetState(CallState.Unknown); }
            }
        };
        await fixture.RunAsync("carry out the requested action");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.ResponseTitle.Should().Be("Saved permission is not eligible.");
    }

    [Fact]
    public async Task Call_entry_during_pending_once_confirmation_cannot_dispatch_or_grant_reuse()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local model", DependencyReadiness.Ready, "Ready");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("carry out the requested action");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(fixture.ViewModel.IsModelActionApprovalPending), StringComparison.Ordinal)
                && !fixture.ViewModel.IsModelActionApprovalPending) { fixture.CallState.SetState(CallState.Active); }
        };
        await fixture.ViewModel.ApproveModelActionCommand.ExecuteAsync();
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
        fixture.ViewModel.ResponseTitle.Should().Be("Call policy changed.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Voice_provider_install_remove_and_reenable_are_not_call_gate_bypasses(bool installed)
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones = [new MicrophoneDevice("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";
        fixture.TextToSpeech.Providers = [CreateWindowsProvider(), CreateKokoroProvider(installed)];
        fixture.TextToSpeech.Voices =
        [
            new SpeechVoice("female", "Windows voice", "en-US", SpeechVoiceGender.Female),
            CreateKokoroVoice("af_heart", "Heart"),
        ];
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedSpeechProvider = fixture.ViewModel.SpeechProviders.Single(p =>
            string.Equals(p.Id, SpeechProviderIds.Kokoro, StringComparison.Ordinal));
        fixture.CallState.SetState(CallState.Active);
        using var voice = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request);
        await (installed ? fixture.ViewModel.RemoveSpeechProviderCommand : fixture.ViewModel.DownloadSpeechProviderCommand).ExecuteAsync();
        fixture.TextToSpeech.InstallProviderCalls.Should().Be(0);
        fixture.TextToSpeech.RemoveProviderCalls.Should().Be(0);
        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Pending_voice_name_change_is_rejected_after_call_entry_before_any_persistence_or_apply()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.Voice.StopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var voice = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request))
        {
            var pending = fixture.ViewModel.SetAssistantNameAsync("Nova");
            pending.IsCompleted.Should().BeFalse();
            fixture.NamePreferences.SavedName.Should().BeNull();
            fixture.CallState.SetState(CallState.Active);
            fixture.Voice.StopGate.SetResult();
            await pending;
            fixture.NamePreferences.SavedName.Should().BeNull();
            fixture.ViewModel.AssistantName.Should().Be("Kora");
            fixture.ViewModel.ResponseBody.Should().Contain("Call policy changed");
        }
        await fixture.ViewModel.SetAssistantNameAsync("Nova");
        fixture.ViewModel.AssistantName.Should().Be("Nova");
    }

    [Fact]
    public async Task Failed_capture_release_cannot_commit_a_pending_name_mutation()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.Voice.StopException = new InvalidOperationException("capture remains open");
        await fixture.ViewModel.SetAssistantNameAsync("Nova");
        fixture.NamePreferences.SavedName.Should().BeNull();
        fixture.ViewModel.AssistantName.Should().Be("Kora");
        fixture.ViewModel.ResponseTitle.Should().Be("The assistant name could not be changed.");
        fixture.Audit.Events.Should().Contain(e => e.ReasonCode == "capture-stop-failed"
            && e.Outcome == SecurityAuditOutcome.Failed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Voice_enablement_rechecks_original_call_observation_after_readiness_work(bool consentRoute)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.PrivacyObservation.BeforeRefresh = () => fixture.CallState.SetState(CallState.Active);
        using var voice = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request);
        if (consentRoute) { await fixture.ViewModel.SetVoiceConsentAsync(true); }
        else { await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Voice.StartCalls.Should().Be(0);
        fixture.ViewModel.ResponseBody.Should().Contain("Call policy changed");
    }

    [Fact]
    public async Task Legacy_dispatch_rechecks_ownership_after_audio_shutdown()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local model", DependencyReadiness.Ready, "Ready");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        fixture.ApprovalPreferences.Save(new(true, [BuiltInAction.LockMachine]));
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.BeforeStop = () => fixture.ViewModel.BindCallOwnershipGate(static () => false);
        await fixture.RunAsync("carry out the requested action");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.Session.LockCalls.Should().Be(0);
        fixture.Audit.Events.Should().Contain(e => e.Outcome == SecurityAuditOutcome.Denied);
    }
}
