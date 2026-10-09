using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private sealed class FakeWindowsSpeechRatePreferences : IWindowsSpeechRatePreferences
    {
        public WindowsSpeechRate? Value { get; set; }
        public Exception? ReadFailure { get; set; }
        public Exception? WriteFailure { get; set; }
        public Action? AfterWrite { get; set; }
        public bool Pending { get; set; }
        public WindowsSpeechRate? Load() { if (Pending) { throw new InvalidDataException("Unconfirmed rate"); } return ReadBack(); }
        public WindowsSpeechRate? ReadBack() { if (ReadFailure is { } failure) { throw failure; } return Value; }
        public void BeginWrite() => Pending = true;
        public void ConfirmWrite() => Pending = false;
        public void Save(WindowsSpeechRate rate) { if (WriteFailure is { } failure) { throw failure; } Value = rate; AfterWrite?.Invoke(); }
        public void Reset() { Value = null; AfterWrite?.Invoke(); }
    }

    [Fact]
    public async Task Native_typed_current_name_activated_rate_share_audio_admission_without_synthesis_or_capture()
    {
        var fixture = new Fixture(enableWindowsSpeechRate: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        var microphones = fixture.Voice.StartCalls;
        await fixture.RunAsync("Kora, list rate settings");
        fixture.ViewModel.ResponseBody.Should().Contain("speech.windows-rate").And.Contain("\"minimum\":-10").And.Contain("\"maximum\":10")
            .And.Contain("Windows synthesizer only").And.Contain("Kokoro unchanged");
        await fixture.RunAsync("set speech.windows-rate to -10");
        fixture.RatePreferences.Value.Should().Be(new WindowsSpeechRate(-10));
        fixture.TextToSpeech.Rate.Should().Be(new WindowsSpeechRate(-10));
        await fixture.RunAsync("status speech.windows-rate");
        fixture.ViewModel.ResponseBody.Should().Contain("\"source\":\"saved\"");
        fixture.ViewModel.SelectedWindowsSpeechRate = 10;
        await fixture.ViewModel.SaveWindowsSpeechRateCommand.ExecuteAsync();
        fixture.RatePreferences.Value.Should().Be(new WindowsSpeechRate(10));
        fixture.ViewModel.WindowsSpeechRateChoices.Should().HaveCount(21);
        await fixture.ViewModel.RefreshWindowsSpeechRateCommand.ExecuteAsync();
        await fixture.ViewModel.ResetWindowsSpeechRateCommand.ExecuteAsync();
        fixture.RatePreferences.Value.Should().BeNull();
        fixture.ViewModel.SelectedWindowsSpeechRate.Should().Be(0);
        fixture.ViewModel.WindowsSpeechRateStatus.Should().Contain("\"source\":\"default\"");
        fixture.Voice.StartCalls.Should().Be(microphones);
        await fixture.RunAsync("set assistant.name to Nova");
        fixture.TextToSpeech.ClearSpokenResponse();
        await fixture.RaiseActivatedTranscriptAsync("Nova, set speech.windows-rate to -1", 1);
        fixture.RatePreferences.Value.Should().Be(new WindowsSpeechRate(-1));
        await fixture.RaiseActivatedTranscriptAsync("Nova, reset speech.windows-rate", 1);
        fixture.RatePreferences.Value.Should().BeNull();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.TextToSpeech.InstallProviderCalls.Should().Be(0);
        fixture.TextToSpeech.RemoveProviderCalls.Should().Be(0);
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
        fixture.Preferences.SavedVoiceId.Should().BeNull();
        fixture.ViewModel.Dispose();
        fixture.RateConfiguration!.HoldUnavailable();
    }

    [Fact]
    public async Task Kokoro_stays_unscaled_and_provider_switch_retires_rate_proposals_then_restores_windows_rate()
    {
        var fixture = new Fixture(enableWindowsSpeechRate: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("set speech.windows-rate to 3");
        fixture.TextToSpeech.Providers = fixture.TextToSpeech.Providers.Concat(
            [new SpeechProvider(SpeechProviderIds.Kokoro, "Kokoro", "Installed", true, false, null, "af_heart")]).ToArray();
        fixture.TextToSpeech.Voices = fixture.TextToSpeech.Voices.Concat(
            [new SpeechVoice("af_heart", "Heart", "en-US", SpeechVoiceGender.Female) { ProviderId = SpeechProviderIds.Kokoro }]).ToArray();
        fixture.SpeechConfiguration.Reload();
        await fixture.RunAsync("set speech.provider to kokoro");
        fixture.ViewModel.CanChangeWindowsSpeechRate.Should().BeFalse();
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeTrue();
        fixture.TextToSpeech.Rate.Should().BeNull();
        await fixture.RunAsync("get speech.windows-rate");
        fixture.ViewModel.ResponseBody.Should().Contain("\"available\":false").And.Contain("Kokoro synthesis is unchanged");
        await fixture.RunAsync("set speech.windows-rate to 4");
        fixture.RatePreferences.Value.Should().Be(new WindowsSpeechRate(3));
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeTrue();
        await fixture.RunAsync("what power action is pending");
        fixture.TextToSpeech.SpokenVoice!.ProviderId.Should().Be(SpeechProviderIds.Kokoro);
        await fixture.RunAsync("reset speech.provider");
        fixture.TextToSpeech.Rate.Should().Be(new WindowsSpeechRate(3));
        fixture.ViewModel.CanChangeWindowsSpeechRate.Should().BeTrue();
        fixture.TextToSpeech.ClearSpokenResponse();
        await fixture.RunAsync("reset speech.windows-rate");
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData("save")]
    [InlineData("write-failure")]
    [InlineData("invalid")]
    public async Task Inflight_rate_outcomes_preserve_the_full_original_response_and_never_replay(string outcome)
    {
        var fixture = new Fixture(enableWindowsSpeechRate: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var speaking = fixture.RunAsync("what power action is pending");
        await fixture.TextToSpeech.SpeakStarted.Task;
        var original = fixture.ViewModel.ResponseBody;
        var microphones = fixture.Voice.StartCalls;
        if (outcome is "write-failure") { fixture.RatePreferences.WriteFailure = new IOException("rate save failed"); }
        await fixture.ViewModel.ExecuteWindowsSpeechRateCommandAsync(new(AppearanceCommandOperation.Set, outcome is "invalid" ? "11" : "1"),
            SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        fixture.TextToSpeech.SpeakGate.TrySetResult();
        await speaking;
        fixture.ViewModel.ResponseBody.Should().Be(original);
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.Voice.StartCalls.Should().Be(microphones);
        fixture.ViewModel.Transcript.Should().Contain(outcome is "save" ? "speech.windows-rate" : outcome is "invalid" ? "canonical" : "not confirmed");
        fixture.RatePreferences.WriteFailure = null;
        fixture.RatePreferences.Pending = false;
        await fixture.ViewModel.ResetWindowsSpeechRateCommand.ExecuteAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Fact]
    public async Task Corrupt_saved_rate_and_failed_refresh_never_default_and_invalid_commands_are_local()
    {
        var fixture = new Fixture(enableWindowsSpeechRate: true, rateReadFailure: new InvalidDataException("corrupt rate"));
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();
        fixture.ViewModel.WindowsSpeechRateStatus.Should().Contain("unconfirmed");
        fixture.RatePreferences.ReadFailure = new UnauthorizedAccessException();
        await fixture.RunAsync("get speech.windows-rate");
        fixture.ViewModel.ResponseTitle.Should().Be("Windows rate preference not confirmed.");
        fixture.RatePreferences.ReadFailure = null;
        await fixture.RunAsync("get speech.windows-rate");
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeTrue();
        await fixture.RunAsync("set speech.windows-rate to +1");
        fixture.ViewModel.ResponseTitle.Should().Contain("Windows-native integer rate");
        await fixture.RunAsync("get speech.windows-rate trailing");
        fixture.ViewModel.ResponseTitle.Should().Be("Clarify the Windows rate setting.");
        await fixture.ViewModel.ExecuteWindowsSpeechRateCommandAsync(new(AppearanceCommandOperation.Set),
            SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        fixture.ViewModel.ResponseTitle.Should().Be("Windows rate preference not confirmed.");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await fixture.ViewModel.ExecuteWindowsSpeechRateCommandAsync(new(AppearanceCommandOperation.Get), SecurityAuditInitiator.TypedCommand, cancelled.Token);
        fixture.ViewModel.WindowsSpeechRateStatus.Should().Contain("\"source\":\"unavailable\"");
    }

    [Fact]
    public async Task Pending_approval_and_unbound_or_disposed_native_hosts_cannot_mutate_or_replace_preview()
    {
        var unbound = new Fixture();
        await using var unboundAdmission = unbound.OutputAdmission;
        unbound.ViewModel.WindowsSpeechRateStatus.Should().Contain("unavailable");
        await unbound.ViewModel.RefreshWindowsSpeechRateCommand.ExecuteAsync();
        unbound.ViewModel.Transcript.Should().Contain("owning unlocked host");
        unbound.ViewModel.Dispose();
        await unbound.ViewModel.RefreshWindowsSpeechRateCommand.ExecuteAsync();
        var fixture = new Fixture(enableWindowsSpeechRate: true);
        await using var admission = fixture.OutputAdmission;
        fixture.Probe.Status = new("local.inference", "Local model inference (Ollama)", Kora.Core.Dependencies.DependencyReadiness.Ready, "ready");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("lock this workstation please");
        await fixture.ViewModel.ActiveReasoningTask!;
        var preview = fixture.ViewModel.ResponseBody;
        await fixture.RunAsync("reset speech.windows-rate");
        fixture.ViewModel.CanChangeWindowsSpeechRate.Should().BeFalse();
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        fixture.ViewModel.ResponseBody.Should().Be(preview);
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.Dispose();
        await fixture.ViewModel.RefreshWindowsSpeechRateCommand.ExecuteAsync();
        fixture.RateConfiguration!.HoldUnavailable();
        fixture.ViewModel.ResponseBody.Should().Be(preview);
    }

    [Fact]
    public async Task Protected_original_voice_cannot_be_relabelled_to_native_rate_authority()
    {
        var fixture = new Fixture(enableWindowsSpeechRate: true);
        await using var admission = fixture.OutputAdmission;
        fixture.Voice.Microphones = [new("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.SetVoiceConsentAsync(true);
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        await fixture.ViewModel.SetAllowVoiceActivationDuringCallsAsync(false);
        fixture.CallState.SetState(CallState.Unknown);
        await fixture.Dispatcher.LastInvocation;
        await fixture.ViewModel.ExecuteWindowsSpeechRateCommandAsync(new(AppearanceCommandOperation.Set, "1"),
            SecurityAuditInitiator.VoiceCommand, TestContext.Current.CancellationToken);
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request))
        {
            await fixture.ViewModel.ExecuteWindowsSpeechRateCommandAsync(new(AppearanceCommandOperation.Set, "1"),
                SecurityAuditInitiator.VoiceCommand, TestContext.Current.CancellationToken);
        }
        fixture.RatePreferences.Value.Should().BeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Native_rate_failure_holds_only_rate_output_and_preserves_voice_output_and_complete_visual_response(bool preview, bool configured)
    {
        var fixture = new Fixture(enableWindowsSpeechRate: configured);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        var voice = fixture.ViewModel.SelectedVoice;
        var output = fixture.ViewModel.SelectedOutputDevice;
        fixture.TextToSpeech.SpeakException = new WindowsSpeechRateUnavailableException("native rate failure");
        if (preview) { await fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync(); }
        else { await fixture.RunAsync("what power action is pending"); }
        fixture.ViewModel.SelectedVoice.Should().Be(voice);
        fixture.ViewModel.SelectedOutputDevice.Should().Be(output);
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        if (configured)
        {
            fixture.ViewModel.WindowsSpeechRateStatus.Should().Contain("unconfirmed");
            fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();
        }
        fixture.RatePreferences.Pending.Should().BeFalse();
    }

    [Fact]
    public async Task Busy_or_disposed_audit_callbacks_do_not_publish_late_success()
    {
        var fixture = new Fixture(enableWindowsSpeechRate: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.Audit.BeforeWrite = item =>
        {
            if (item.Outcome == SecurityAuditOutcome.Requested && string.Equals(item.ActionId, "configuration.windows-speech-rate", StringComparison.Ordinal))
            {
                fixture.ViewModel.RefreshWindowsSpeechRateCommand.ExecuteAsync().GetAwaiter().GetResult();
            }
        };
        await fixture.ViewModel.ExecuteWindowsSpeechRateCommandAsync(new(AppearanceCommandOperation.Set, "1"),
            SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        fixture.RatePreferences.Value.Should().Be(new WindowsSpeechRate(1));
        fixture.Audit.BeforeWrite = item =>
        {
            if (item.Outcome == SecurityAuditOutcome.Succeeded && string.Equals(item.ActionId, "configuration.windows-speech-rate", StringComparison.Ordinal)) { fixture.ViewModel.Dispose(); }
        };
        var original = fixture.ViewModel.ResponseBody;
        await fixture.RunAsync("set speech.windows-rate to 2");
        fixture.ViewModel.ResponseBody.Should().Be(original);
        fixture.ViewModel.CanChangeWindowsSpeechRate.Should().BeFalse();
        fixture.ViewModel.Invoking(vm => vm.SelectedWindowsSpeechRate = 11).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Protected_call_denies_rate_mutation_even_when_activated_input_remains_eligible()
    {
        var fixture = new Fixture(enableWindowsSpeechRate: true);
        await using var admission = fixture.OutputAdmission;
        fixture.Voice.Microphones = [new("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.SetVoiceConsentAsync(true);
        if (!fixture.ViewModel.IsVoiceEnabled) { await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
        fixture.CallState.SetState(CallState.Active);
        await fixture.Dispatcher.LastInvocation;
        await fixture.ViewModel.ExecuteWindowsSpeechRateCommandAsync(new(AppearanceCommandOperation.Set, "1"),
            SecurityAuditInitiator.VoiceCommand, TestContext.Current.CancellationToken);
        fixture.RatePreferences.Value.Should().BeNull();
        fixture.ViewModel.ResponseBody.Should().Contain("\"outcome\":\"denied\"");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changed_call_at_confirmed_notification_or_cleanup_never_publishes_late_rate_success(bool duringCleanup)
    {
        var fixture = new Fixture(enableWindowsSpeechRate: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        Task? speaking = null;
        if (duringCleanup)
        {
            fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            speaking = fixture.RunAsync("what power action is pending");
            await fixture.TextToSpeech.SpeakStarted.Task;
            fixture.TextToSpeech.BeforeStop = () => fixture.CallState.SetState(CallState.Active);
        }
        else
        {
            fixture.RateConfiguration!.Changed += (_, _) =>
            {
                if (fixture.RatePreferences.Value is not null) { fixture.CallState.SetState(CallState.Active); }
            };
        }
        var original = fixture.ViewModel.ResponseBody;
        await fixture.ViewModel.ExecuteWindowsSpeechRateCommandAsync(new(AppearanceCommandOperation.Set, "1"),
            SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        if (speaking is not null)
        {
            fixture.TextToSpeech.SpeakGate!.TrySetResult();
            await speaking;
        }
        fixture.RatePreferences.Value.Should().Be(new WindowsSpeechRate(1));
        fixture.ViewModel.ResponseBody.Should().Be(original);
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("owner")]
    [InlineData("topology")]
    [InlineData("native")]
    [InlineData("native-replaced")]
    [InlineData("name")]
    [InlineData("voice-generation")]
    public async Task Native_lifetime_original_name_privacy_and_input_generation_are_not_replaceable(string changed)
    {
        var fixture = new Fixture(enableWindowsSpeechRate: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        if (changed is "voice-generation")
        {
            fixture.Voice.Microphones = [new("0", "Headset")];
            fixture.Voice.DefaultMicrophoneId = "0";
            await fixture.ViewModel.SetVoiceConsentAsync(true);
            await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        }
        var visible = true;
        fixture.ViewModel.BindWindowsSpeechRateNativeLifetime(() => visible);
        fixture.ViewModel.CanInspectWindowsSpeechRateNative.Should().BeTrue();
        fixture.ViewModel.CanChangeWindowsSpeechRateNative.Should().BeTrue();
        fixture.Audit.BeforeWrite = item =>
        {
            if (item.Outcome != SecurityAuditOutcome.Requested || !string.Equals(item.ActionId, "configuration.windows-speech-rate", StringComparison.Ordinal)) { return; }
            if (changed is "locked") { fixture.Session.IsUnlocked = false; }
            if (changed is "owner") { fixture.ViewModel.BindCallOwnershipGate(static () => false); }
            if (changed is "topology") { fixture.PrivacyObservation.Current = fixture.PrivacyObservation.Current with { TopologyRevision = 100 }; }
            if (changed is "native") { visible = false; }
            if (changed is "native-replaced") { fixture.ViewModel.BindWindowsSpeechRateNativeLifetime(static () => true); }
            if (changed is "name") { fixture.ViewModel.SetAssistantNameAsync("Nova").GetAwaiter().GetResult(); }
            if (changed is "voice-generation") { fixture.Voice.AdvanceGeneration(); }
        };
        await fixture.ViewModel.ExecuteWindowsSpeechRateCommandAsync(new(AppearanceCommandOperation.Set, "1"),
            changed is "voice-generation" ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser, TestContext.Current.CancellationToken);
        fixture.RatePreferences.Value.Should().BeNull();
        if (changed is "voice-generation") { fixture.TextToSpeech.Rate.Should().BeNull(); }
        else { fixture.TextToSpeech.Rate.Should().Be(WindowsSpeechRate.Default); }
        fixture.ViewModel.BindWindowsSpeechRateNativeLifetime(static () => false);
        fixture.ViewModel.CanInspectWindowsSpeechRateNative.Should().BeFalse();
        fixture.ViewModel.CanChangeWindowsSpeechRateNative.Should().BeFalse();
        await fixture.ViewModel.RefreshWindowsSpeechRateCommand.ExecuteAsync();
        fixture.ViewModel.Transcript.Should().Contain("owning unlocked host");
    }
}
