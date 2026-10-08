using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private sealed class FakeAuditRetentionPreferences : IAuditRetentionPreferences
    {
        internal AuditRetentionDays? Value { get; set; }
        internal Exception? ReadFailure { get; set; }
        internal Exception? WriteFailure { get; set; }
        internal Action? AfterWrite { get; set; }
        internal bool Pending { get; set; }
        public AuditRetentionDays? Load()
        {
            if (Pending) { throw new InvalidDataException("Unconfirmed write"); }
            return ReadBack();
        }
        public AuditRetentionDays? ReadBack() { if (ReadFailure is { } failure) { throw failure; } return Value; }
        public void BeginWrite() => Pending = true;
        public void ConfirmWrite() => Pending = false;
        public void Save(AuditRetentionDays value) { if (WriteFailure is { } failure) { throw failure; } Value = value; AfterWrite?.Invoke(); }
        public void Reset() { Value = null; AfterWrite?.Invoke(); }
    }

    [Fact]
    public async Task Native_typed_current_name_ACTIVATED_audit_and_complete_logging_discovery_share_policy_without_other_effects()
    {
        var fixture = new Fixture(enableDiagnosticRetention: true, enableAuditRetention: true);
        await using var auditAdmission = fixture.AuditAdmission;
        await using var diagnosticAdmission = fixture.DiagnosticAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.ListeningButtonText.Should().Be("Enable listening");
        fixture.ViewModel.AuditRetentionChoices.Should().HaveCount(336).And.StartWith(30).And.EndWith(365);
        fixture.ViewModel.CanChangeAuditRetentionNative.Should().BeFalse();
        await fixture.ViewModel.SaveAuditRetentionCommand.ExecuteAsync();
        fixture.AuditPreferences.Value.Should().BeNull();
        fixture.ViewModel.BindAuditRetentionNativeLifetime(static () => true);
        await fixture.RunAsync("Kora, list logging settings");
        fixture.ViewModel.ResponseBody.Should().Contain(AuditRetentionCommand.OptionId)
            .And.Contain(DiagnosticRetentionCommand.OptionId).And.Contain("\"applyNowAvailable\":false");
        await fixture.RunAsync("set " + AuditRetentionCommand.OptionId + " to 30");
        fixture.AuditPreferences.Value.Should().Be(new AuditRetentionDays(30));
        await fixture.RunAsync("status " + AuditRetentionCommand.OptionId);
        fixture.ViewModel.ResponseBody.Should().Contain("\"source\":\"saved\"");
        fixture.ViewModel.SelectedAuditRetentionDays = 365;
        await fixture.ViewModel.SaveAuditRetentionCommand.ExecuteAsync();
        fixture.AuditPreferences.Value.Should().Be(new AuditRetentionDays(365));
        await fixture.ViewModel.RefreshAuditRetentionCommand.ExecuteAsync();
        await fixture.ViewModel.ResetAuditRetentionCommand.ExecuteAsync();
        fixture.ViewModel.SelectedAuditRetentionDays.Should().Be(90);
        fixture.ViewModel.AuditRetentionStatus.Should().Contain("\"source\":\"default\"");
        await fixture.RunAsync("set assistant.name to Nova");
        fixture.TextToSpeech.ClearSpokenResponse();
        var starts = fixture.Voice.StartCalls;
        await fixture.RaiseActivatedTranscriptAsync("Nova, set " + AuditRetentionCommand.OptionId + " to 90", 1);
        fixture.AuditPreferences.Value.Should().Be(new AuditRetentionDays(90));
        await fixture.RaiseActivatedTranscriptAsync("Nova, reset " + AuditRetentionCommand.OptionId, 1);
        fixture.ViewModel.ListeningButtonText.Should().Be("Disable listening");
        fixture.AuditPreferences.Value.Should().BeNull();
        fixture.Voice.StartCalls.Should().Be(starts + 2);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.DiagnosticPreferences.Value.Should().BeNull();
        fixture.DiagnosticPolicy.Effective.Should().Be(DiagnosticRetentionDays.Default);
        fixture.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
        fixture.ViewModel.Dispose();
        fixture.AuditConfiguration!.HoldUnavailable();
        await fixture.ViewModel.RefreshAuditRetentionCommand.ExecuteAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Corrupt_saved_values_exact_grammar_atomic_failure_and_pending_approval_never_show_success(bool invalid)
    {
        var fixture = new Fixture(enableAuditRetention: true,
            auditRetentionReadFailure: invalid ? new InvalidDataException("corrupt audit days") : null);
        await using var admission = fixture.AuditAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindAuditRetentionNativeLifetime(static () => true);
        if (invalid)
        {
            fixture.ViewModel.AuditRetentionStatus.Should().Contain("unconfirmed");
            await fixture.RunAsync("get " + AuditRetentionCommand.OptionId);
            fixture.ViewModel.ResponseTitle.Should().Contain("not confirmed");
            fixture.AuditPreferences.ReadFailure = null;
        }
        await fixture.RunAsync("get " + AuditRetentionCommand.OptionId);
        await fixture.RunAsync("set " + AuditRetentionCommand.OptionId + " to 29");
        fixture.ViewModel.ResponseTitle.Should().Contain("exact integer");
        await fixture.RunAsync("set " + AuditRetentionCommand.OptionId + " to 30 apply now");
        fixture.AuditPreferences.Value.Should().BeNull();
        await fixture.RunAsync("reset " + AuditRetentionCommand.OptionId + " now");
        fixture.ViewModel.ResponseTitle.Should().Contain("Clarify");
        fixture.AuditPreferences.WriteFailure = new IOException("atomic audit save failed");
        await fixture.RunAsync("set " + AuditRetentionCommand.OptionId + " to 30");
        fixture.ViewModel.ResponseTitle.Should().Contain("not confirmed");
        fixture.AuditPreferences.WriteFailure = null;
        fixture.AuditPreferences.Pending = false;
        fixture.AuditPreferences.AfterWrite = () => fixture.ViewModel.BindAuditRetentionNativeLifetime(static () => false);
        fixture.ViewModel.SelectedAuditRetentionDays = 30;
        await fixture.ViewModel.SaveAuditRetentionCommand.ExecuteAsync();
        fixture.AuditPreferences.Pending.Should().BeTrue();
        fixture.AuditPolicy.Effective.Should().BeNull();
        fixture.AuditPreferences.Pending = false;
        fixture.AuditPreferences.AfterWrite = null;
        fixture.Probe.Status = new("local.inference", "Local model inference (Ollama)", Kora.Core.Dependencies.DependencyReadiness.Ready, "ready");
        fixture.Reasoner.Action = Kora.Core.Commands.BuiltInAction.LockMachine;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("lock this workstation please");
        await fixture.ViewModel.ActiveReasoningTask!;
        var preview = fixture.ViewModel.ResponseBody;
        await fixture.RunAsync("reset " + AuditRetentionCommand.OptionId);
        fixture.ViewModel.ResponseBody.Should().Be(preview);
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        fixture.Session.LockCalls.Should().Be(0);
        typeof(Kora.Application.ViewModels.MainViewModel).GetMethod("PresentAuditRetentionFailure",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(fixture.ViewModel, ["Audit retention not confirmed.", "owned late failure"]);
        fixture.ViewModel.ResponseBody.Should().Be(preview);
        fixture.ViewModel.Transcript.Should().Contain("owned late failure");
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task Hidden_reopened_native_window_does_not_revive_original_save_and_disposed_callbacks_are_inert()
    {
        var fixture = new Fixture(enableAuditRetention: true);
        await using var admission = fixture.AuditAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindAuditRetentionNativeLifetime(static () => true);
        fixture.AuditPreferences.AfterWrite = () =>
        {
            fixture.ViewModel.BindAuditRetentionNativeLifetime(static () => false);
            fixture.ViewModel.BindAuditRetentionNativeLifetime(() => fixture.ViewModel.CanRevealPrivatePresentation);
        };
        fixture.ViewModel.SelectedAuditRetentionDays = 30;
        await fixture.ViewModel.SaveAuditRetentionCommand.ExecuteAsync();
        fixture.AuditPreferences.Pending.Should().BeTrue();
        fixture.AuditPolicy.Effective.Should().BeNull();
        fixture.ViewModel.ResponseTitle.Should().Contain("not confirmed");
        fixture.ViewModel.Dispose();
        await fixture.ViewModel.RefreshAuditRetentionCommand.ExecuteAsync();
    }

    [Fact]
    public async Task Absent_locked_unknown_host_late_disposal_and_unavailable_prefix_are_closed()
    {
        var absent = new Fixture();
        await using var absentAdmission = absent.AuditAdmission;
        absent.ViewModel.AuditRetentionStatus.Should().Contain("unavailable");
        absent.ViewModel.CanChangeAuditRetentionNative.Should().BeFalse();
        await absent.ViewModel.RefreshAuditRetentionCommand.ExecuteAsync();
        var invalidPrefix = new Fixture(enableAuditRetention: true, enableDiagnosticRetention: true);
        await using var prefixAdmission = invalidPrefix.AuditAdmission;
        await using var invalidDiagnosticAdmission = invalidPrefix.DiagnosticAdmission;
        invalidPrefix.NamePreferences.Name = "Supported Commands";
        await invalidPrefix.ViewModel.InitializeAsync();
        await invalidPrefix.RunAsync("Kora, get " + AuditRetentionCommand.OptionId);
        invalidPrefix.ViewModel.Transcript.Should().Contain("prefix routing is unavailable");
        await invalidPrefix.RunAsync("get " + AuditRetentionCommand.OptionId);
        invalidPrefix.ViewModel.ResponseBody.Should().Contain(AuditRetentionCommand.OptionId);
        await invalidPrefix.RunAsync("Kora, list logging settings");
        invalidPrefix.ViewModel.Transcript.Should().Contain("prefix routing is unavailable");
        await invalidPrefix.RunAsync("list logging settings");
        invalidPrefix.ViewModel.ResponseBody.Should().Contain(DiagnosticRetentionCommand.OptionId);
        invalidPrefix.ViewModel.Dispose();
        var fixture = new Fixture(enableAuditRetention: true);
        await using var admission = fixture.AuditAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindAuditRetentionNativeLifetime(static () => true);
        fixture.Session.IsUnlocked = false;
        await fixture.RunAsync("set " + AuditRetentionCommand.OptionId + " to 30");
        fixture.AuditPreferences.Value.Should().BeNull();
        fixture.Session.IsUnlocked = true;
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request))
        {
            await fixture.ViewModel.ExecuteAuditRetentionCommandAsync(new(AppearanceCommandOperation.Set, "30"),
                SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        }
        fixture.AuditPreferences.Value.Should().BeNull();
        fixture.Audit.BeforeWrite = item =>
        {
            if (item.ActionId is "configuration.audit-retention" && item.Outcome == SecurityAuditOutcome.Requested)
            {
                fixture.ViewModel.RefreshAuditRetentionCommand.ExecuteAsync().GetAwaiter().GetResult();
            }
            if (item.ActionId is "configuration.audit-retention" && item.Outcome == SecurityAuditOutcome.Succeeded) { fixture.ViewModel.Dispose(); }
        };
        await fixture.RunAsync("set " + AuditRetentionCommand.OptionId + " to 30");
        fixture.AuditPolicy.Effective.Should().BeNull();
        fixture.ViewModel.CanChangeAuditRetention.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Complete_response_and_inflight_audio_survive_success_or_invalid_audit_input(bool invalid)
    {
        var fixture = new Fixture(enableAuditRetention: true);
        await using var admission = fixture.AuditAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var speaking = fixture.RunAsync("what power action is pending");
        await fixture.TextToSpeech.SpeakStarted.Task;
        var original = fixture.ViewModel.ResponseBody;
        var stops = fixture.TextToSpeech.StopCalls;
        await fixture.ViewModel.ExecuteAuditRetentionCommandAsync(new(AppearanceCommandOperation.Set, invalid ? "366" : "30"),
            SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        fixture.ViewModel.ResponseBody.Should().Be(original);
        fixture.TextToSpeech.StopCalls.Should().Be(stops);
        fixture.ViewModel.Transcript.Should().Contain(invalid ? "exact integer" : AuditRetentionCommand.OptionId);
        fixture.TextToSpeech.SpeakGate.TrySetResult();
        await speaking;
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task Missing_value_and_confirmed_call_change_cannot_publish_stale_success()
    {
        var fixture = new Fixture(enableAuditRetention: true);
        await using var admission = fixture.AuditAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.ExecuteAuditRetentionCommandAsync(new(AppearanceCommandOperation.Set),
            SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        fixture.ViewModel.ResponseTitle.Should().Contain("not confirmed");
        var previous = fixture.ViewModel.ResponseBody;
        fixture.AuditConfiguration!.Changed += (_, _) =>
        {
            if (fixture.AuditPreferences.Value is not null) { fixture.CallState.SetState(CallState.Active); }
        };
        await fixture.RunAsync("set " + AuditRetentionCommand.OptionId + " to 30");
        fixture.ViewModel.ResponseBody.Should().Be(previous);
        fixture.AuditPreferences.Value.Should().Be(new AuditRetentionDays(30));
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task Protected_call_activated_original_channel_is_denied_without_audit_preference_write()
    {
        var fixture = new Fixture(enableAuditRetention: true);
        await using var admission = fixture.AuditAdmission;
        fixture.Voice.Microphones = [new("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.SetVoiceConsentAsync(true);
        if (!fixture.ViewModel.IsVoiceEnabled) { await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
        fixture.CallState.SetState(CallState.Active);
        await fixture.Dispatcher.LastInvocation;
        await fixture.ViewModel.ExecuteAuditRetentionCommandAsync(new(AppearanceCommandOperation.Set, "30"),
            SecurityAuditInitiator.VoiceCommand, TestContext.Current.CancellationToken);
        fixture.AuditPreferences.Value.Should().BeNull();
        fixture.ViewModel.ResponseBody.Should().Contain("denied");
        fixture.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("locked")]
    [InlineData("privacy-unknown")]
    [InlineData("topology")]
    [InlineData("voice-generation")]
    [InlineData("name")]
    public async Task Original_owner_privacy_topology_input_generation_and_name_revision_are_revalidated_after_requested_audit(string changed)
    {
        var fixture = new Fixture(enableAuditRetention: true);
        await using var admission = fixture.AuditAdmission;
        await fixture.ViewModel.InitializeAsync();
        if (changed is "voice-generation")
        {
            fixture.Voice.Microphones = [new("0", "Headset")];
            fixture.Voice.DefaultMicrophoneId = "0";
            await fixture.ViewModel.InitializeAsync();
            await fixture.ViewModel.SetVoiceConsentAsync(true);
            if (!fixture.ViewModel.IsVoiceEnabled) { await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
        }
        fixture.Audit.BeforeWrite = item =>
        {
            if (item.Outcome != SecurityAuditOutcome.Requested || !string.Equals(item.ActionId, "configuration.audit-retention", StringComparison.Ordinal)) { return; }
            if (changed is "owner") { fixture.ViewModel.BindCallOwnershipGate(static () => false); }
            if (changed is "locked") { fixture.Session.IsUnlocked = false; }
            if (changed is "privacy-unknown")
            {
                fixture.PrivacyObservation.Current = fixture.PrivacyObservation.Current with { SessionState = Kora.Core.Platform.WindowsSessionState.Unknown };
            }
            if (changed is "topology") { fixture.PrivacyObservation.Current = fixture.PrivacyObservation.Current with { TopologyRevision = 100 }; }
            if (changed is "voice-generation") { fixture.Voice.AdvanceGeneration(); }
            if (changed is "name")
            {
                fixture.ViewModel.AssistantNameInput = "Nova";
                fixture.ViewModel.ApplyAssistantNameCommand.ExecuteAsync().IsCompletedSuccessfully.Should().BeTrue();
            }
        };
        await fixture.ViewModel.ExecuteAuditRetentionCommandAsync(new(AppearanceCommandOperation.Set, "30"),
            changed is "voice-generation" ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand,
            TestContext.Current.CancellationToken);
        fixture.AuditPreferences.Value.Should().BeNull();
        fixture.AuditPreferences.Pending.Should().BeFalse();
        fixture.Audit.Events.Last(item => string.Equals(item.ActionId, "configuration.audit-retention", StringComparison.Ordinal))
            .Outcome.Should().Be(SecurityAuditOutcome.Denied);
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task Complete_registry_discovery_reports_held_audit_without_changing_confirmed_ordinary_policy()
    {
        var fixture = new Fixture(enableAuditRetention: true, enableDiagnosticRetention: true);
        await using var auditAdmission = fixture.AuditAdmission;
        await using var diagnosticAdmission = fixture.DiagnosticAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("set " + DiagnosticRetentionCommand.OptionId + " to 14");
        fixture.AuditPreferences.ReadFailure = new InvalidDataException("damaged independent audit preference");
        await fixture.RunAsync("list logging settings");
        fixture.ViewModel.ResponseBody.Should().Contain("\"outcome\":\"unavailable\"")
            .And.Contain(AuditRetentionCommand.OptionId).And.Contain(DiagnosticRetentionCommand.OptionId);
        fixture.DiagnosticPolicy.Effective.Should().Be(new DiagnosticRetentionDays(14));
        fixture.DiagnosticConfiguration!.Get().Source.Should().Be("saved");
        fixture.AuditPolicy.Effective.Should().BeNull();
        fixture.ViewModel.Dispose();
    }
}
