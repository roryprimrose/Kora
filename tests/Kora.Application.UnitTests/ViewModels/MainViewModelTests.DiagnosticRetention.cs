using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private sealed class FakeDiagnosticRetentionPreferences : IDiagnosticRetentionPreferences
    {
        internal DiagnosticRetentionDays? Value { get; set; }
        internal Exception? ReadFailure { get; set; }
        internal Exception? WriteFailure { get; set; }
        internal Action? AfterWrite { get; set; }
        internal bool Pending { get; set; }
        public DiagnosticRetentionDays? Load()
        {
            if (Pending) { throw new InvalidDataException("Unconfirmed write"); }
            return ReadBack();
        }
        public DiagnosticRetentionDays? ReadBack() { if (ReadFailure is { } failure) { throw failure; } return Value; }
        public void BeginWrite() => Pending = true;
        public void ConfirmWrite() => Pending = false;
        public void Save(DiagnosticRetentionDays value) { if (WriteFailure is { } failure) { throw failure; } Value = value; AfterWrite?.Invoke(); }
        public void Reset() { Value = null; AfterWrite?.Invoke(); }
    }

    [Fact]
    public async Task Native_typed_current_name_ACTIVATED_retention_parity_has_no_audio_model_or_grant_side_effects()
    {
        var fixture = new Fixture(enableDiagnosticRetention: true);
        await using var admission = fixture.DiagnosticAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.DiagnosticRetentionChoices.Should().HaveCount(365);
        fixture.ViewModel.CanChangeDiagnosticRetentionNative.Should().BeFalse();
        await fixture.ViewModel.SaveDiagnosticRetentionCommand.ExecuteAsync();
        fixture.DiagnosticPreferences.Value.Should().BeNull();
        fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => true);
        await fixture.RunAsync("Kora, list logging settings");
        fixture.ViewModel.ResponseBody.Should().Contain(DiagnosticRetentionCommand.OptionId).And.Contain("\"applyNowAvailable\":false");
        await fixture.RunAsync("set " + DiagnosticRetentionCommand.OptionId + " to 1");
        fixture.DiagnosticPreferences.Value.Should().Be(new DiagnosticRetentionDays(1));
        await fixture.RunAsync("status " + DiagnosticRetentionCommand.OptionId);
        fixture.ViewModel.ResponseBody.Should().Contain("\"source\":\"saved\"");
        fixture.ViewModel.SelectedDiagnosticRetentionDays = 365;
        await fixture.ViewModel.SaveDiagnosticRetentionCommand.ExecuteAsync();
        fixture.DiagnosticPreferences.Value.Should().Be(new DiagnosticRetentionDays(365));
        await fixture.ViewModel.RefreshDiagnosticRetentionCommand.ExecuteAsync();
        await fixture.ViewModel.ResetDiagnosticRetentionCommand.ExecuteAsync();
        fixture.ViewModel.SelectedDiagnosticRetentionDays.Should().Be(30);
        fixture.ViewModel.DiagnosticRetentionStatus.Should().Contain("\"source\":\"default\"");
        await fixture.RunAsync("set assistant.name to Nova");
        var starts = fixture.Voice.StartCalls;
        fixture.TextToSpeech.ClearSpokenResponse();
        await fixture.RaiseActivatedTranscriptAsync("Nova, set " + DiagnosticRetentionCommand.OptionId + " to 30", 1);
        fixture.DiagnosticPreferences.Value.Should().Be(new DiagnosticRetentionDays(30));
        await fixture.RaiseActivatedTranscriptAsync("Nova, reset " + DiagnosticRetentionCommand.OptionId, 1);
        fixture.DiagnosticPreferences.Value.Should().BeNull();
        // The activated-transcript harness enables capture for each original input; the setting itself never starts it.
        fixture.Voice.StartCalls.Should().Be(starts + 2);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
        fixture.ViewModel.Dispose();
        fixture.DiagnosticConfiguration!.HoldUnavailable();
        await fixture.ViewModel.RefreshDiagnosticRetentionCommand.ExecuteAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_saved_values_explicit_recovery_native_lifetime_and_pending_approval_never_publish_success(bool invalid)
    {
        var fixture = new Fixture(enableDiagnosticRetention: true,
            diagnosticRetentionReadFailure: invalid ? new InvalidDataException("corrupt retention") : null);
        await using var admission = fixture.DiagnosticAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => true);
        if (invalid)
        {
            fixture.ViewModel.DiagnosticRetentionStatus.Should().Contain("unconfirmed");
            await fixture.RunAsync("get " + DiagnosticRetentionCommand.OptionId);
            fixture.ViewModel.ResponseTitle.Should().Contain("not confirmed");
            fixture.DiagnosticPreferences.ReadFailure = null;
        }
        await fixture.RunAsync("get " + DiagnosticRetentionCommand.OptionId);
        await fixture.RunAsync("set " + DiagnosticRetentionCommand.OptionId + " to 0");
        fixture.ViewModel.ResponseTitle.Should().Contain("exact integer");
        await fixture.RunAsync("set " + DiagnosticRetentionCommand.OptionId + " to 1 apply now");
        fixture.DiagnosticPreferences.Value.Should().BeNull();
        await fixture.RunAsync("set logging.audit-retention-days to 1");
        fixture.ViewModel.ResponseTitle.Should().Contain("Clarify");
        fixture.DiagnosticPreferences.WriteFailure = new IOException("atomic save failed");
        await fixture.RunAsync("set " + DiagnosticRetentionCommand.OptionId + " to 1");
        fixture.ViewModel.ResponseTitle.Should().Contain("not confirmed");
        fixture.DiagnosticPreferences.WriteFailure = null;
        fixture.DiagnosticPreferences.Pending = false;
        fixture.DiagnosticPreferences.AfterWrite = () => fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => false);
        fixture.ViewModel.SelectedDiagnosticRetentionDays = 1;
        await fixture.ViewModel.SaveDiagnosticRetentionCommand.ExecuteAsync();
        fixture.DiagnosticPreferences.Pending.Should().BeTrue();
        fixture.DiagnosticPolicy.Effective.Should().BeNull();
        fixture.DiagnosticPreferences.Pending = false;
        fixture.DiagnosticPreferences.AfterWrite = null;
        fixture.Probe.Status = new("local.inference", "Local model inference (Ollama)", Kora.Core.Dependencies.DependencyReadiness.Ready, "ready");
        fixture.Reasoner.Action = Kora.Core.Commands.BuiltInAction.LockMachine;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("lock this workstation please");
        await fixture.ViewModel.ActiveReasoningTask!;
        var preview = fixture.ViewModel.ResponseBody;
        await fixture.RunAsync("reset " + DiagnosticRetentionCommand.OptionId);
        fixture.ViewModel.ResponseBody.Should().Be(preview);
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        fixture.Session.LockCalls.Should().Be(0);
        typeof(MainViewModel).GetMethod("PresentDiagnosticRetentionFailure",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(fixture.ViewModel, ["SQLite diagnostic retention not confirmed.", "owned late failure"]);
        fixture.ViewModel.ResponseBody.Should().Be(preview);
        fixture.ViewModel.Transcript.Should().Contain("owned late failure");
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task Hiding_and_reopening_native_settings_cannot_revive_the_original_inflight_binding()
    {
        var fixture = new Fixture(enableDiagnosticRetention: true);
        await using var admission = fixture.DiagnosticAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => true);
        fixture.DiagnosticPreferences.AfterWrite = () =>
        {
            fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => false);
            fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(() => fixture.ViewModel.CanRevealPrivatePresentation);
        };
        fixture.ViewModel.SelectedDiagnosticRetentionDays = 1;

        await fixture.ViewModel.SaveDiagnosticRetentionCommand.ExecuteAsync();

        fixture.DiagnosticPreferences.Pending.Should().BeTrue();
        fixture.DiagnosticPolicy.Effective.Should().BeNull();
        fixture.ViewModel.ResponseTitle.Should().Contain("not confirmed");
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task Unavailable_locked_unknown_original_and_late_disposed_callbacks_are_closed()
    {
        var absent = new Fixture();
        await using var absentAdmission = absent.DiagnosticAdmission;
        absent.ViewModel.DiagnosticRetentionStatus.Should().Contain("unavailable");
        absent.ViewModel.CanChangeDiagnosticRetentionNative.Should().BeFalse();
        await absent.ViewModel.RefreshDiagnosticRetentionCommand.ExecuteAsync();
        var fixture = new Fixture(enableDiagnosticRetention: true);
        await using var admission = fixture.DiagnosticAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => true);
        fixture.Session.IsUnlocked = false;
        await fixture.RunAsync("set " + DiagnosticRetentionCommand.OptionId + " to 1");
        fixture.DiagnosticPreferences.Value.Should().BeNull();
        fixture.Session.IsUnlocked = true;
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request))
        {
            await fixture.ViewModel.ExecuteDiagnosticRetentionCommandAsync(new(AppearanceCommandOperation.Set, "1"),
                SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        }
        fixture.DiagnosticPreferences.Value.Should().BeNull();
        fixture.Audit.BeforeWrite = item =>
        {
            if (string.Equals(item.ActionId, "configuration.sqlite-diagnostic-retention", StringComparison.Ordinal)
                && item.Outcome == SecurityAuditOutcome.Requested)
            {
                fixture.ViewModel.RefreshDiagnosticRetentionCommand.ExecuteAsync().GetAwaiter().GetResult();
            }
            if (string.Equals(item.ActionId, "configuration.sqlite-diagnostic-retention", StringComparison.Ordinal)
                && item.Outcome == SecurityAuditOutcome.Succeeded) { fixture.ViewModel.Dispose(); }
        };
        await fixture.RunAsync("set " + DiagnosticRetentionCommand.OptionId + " to 1");
        fixture.DiagnosticPolicy.Effective.Should().BeNull();
        fixture.ViewModel.CanChangeDiagnosticRetention.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retention_result_and_invalid_setting_preserve_inflight_complete_response_without_stopping_audio(bool invalid)
    {
        var fixture = new Fixture(enableDiagnosticRetention: true);
        await using var admission = fixture.DiagnosticAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var speaking = fixture.RunAsync("what power action is pending");
        await fixture.TextToSpeech.SpeakStarted.Task;
        var original = fixture.ViewModel.ResponseBody;
        var stops = fixture.TextToSpeech.StopCalls;
        await fixture.ViewModel.ExecuteDiagnosticRetentionCommandAsync(new(AppearanceCommandOperation.Set, invalid ? "366" : "1"),
            SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        fixture.ViewModel.ResponseBody.Should().Be(original);
        fixture.TextToSpeech.StopCalls.Should().Be(stops);
        fixture.ViewModel.Transcript.Should().Contain(invalid ? "exact integer" : DiagnosticRetentionCommand.OptionId);
        fixture.TextToSpeech.SpeakGate.TrySetResult();
        await speaking;
    }

    [Fact]
    public async Task Exact_missing_value_and_confirmed_call_change_cannot_publish_stale_success()
    {
        var fixture = new Fixture(enableDiagnosticRetention: true);
        await using var admission = fixture.DiagnosticAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.ExecuteDiagnosticRetentionCommandAsync(new(AppearanceCommandOperation.Set),
            SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        fixture.ViewModel.ResponseTitle.Should().Contain("not confirmed");
        var previous = fixture.ViewModel.ResponseBody;
        fixture.DiagnosticConfiguration!.Changed += (_, _) =>
        {
            if (fixture.DiagnosticPreferences.Value is not null) { fixture.CallState.SetState(Kora.Core.Communication.CallState.Active); }
        };
        await fixture.RunAsync("set " + DiagnosticRetentionCommand.OptionId + " to 1");
        fixture.ViewModel.ResponseBody.Should().Be(previous);
        fixture.DiagnosticPreferences.Value.Should().Be(new DiagnosticRetentionDays(1));
    }

    [Fact]
    public async Task Protected_call_activated_denial_does_not_write_even_when_input_remains_live()
    {
        var fixture = new Fixture(enableDiagnosticRetention: true);
        await using var admission = fixture.DiagnosticAdmission;
        fixture.Voice.Microphones = [new("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.SetVoiceConsentAsync(true);
        if (!fixture.ViewModel.IsVoiceEnabled) { await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
        fixture.CallState.SetState(Kora.Core.Communication.CallState.Active);
        await fixture.Dispatcher.LastInvocation;
        await fixture.ViewModel.ExecuteDiagnosticRetentionCommandAsync(new(AppearanceCommandOperation.Set, "1"),
            SecurityAuditInitiator.VoiceCommand, TestContext.Current.CancellationToken);
        fixture.DiagnosticPreferences.Value.Should().BeNull();
        fixture.ViewModel.ResponseBody.Should().Contain("denied");
    }
}
