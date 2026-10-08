using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task Native_typed_and_real_activated_manual_control_share_run_only_state_without_synthesis_or_capture_reopen()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.TextToSpeech.ClearSpokenResponse();
        await f.ViewModel.SetAssistantNameAsync("Nova", Kora.Core.Auditing.SecurityAuditInitiator.LocalUser);
        var starts = f.Voice.StartCalls;
        var tasks = f.ManualCallStore.Tasks.Count;
        foreach (var input in new[] { "list call settings", "Nova, get call.manual-active", "status call.manual-active" })
        {
            await f.RunAsync(input);
            using var json = JsonDocument.Parse(f.ViewModel.Transcript);
            json.RootElement.GetProperty("outcome").GetString().Should().Be("observed");
            json.RootElement.GetProperty("observation").GetProperty("manualActive").GetBoolean().Should().BeFalse();
            json.RootElement.GetProperty("automaticDetectorAvailable").GetBoolean().Should().BeFalse();
        }
        await f.ViewModel.GetManualCallStatusCommand.ExecuteAsync();
        f.ManualCallStore.Tasks.Should().HaveCount(tasks);
        await f.RaiseActivatedTranscriptAsync("Nova, set call.manual-active to on", 1);
        f.ViewModel.IsManualCallActive.Should().BeTrue();
        f.ManualCallStore.LastRequest!.Origin.Should().Be(Kora.Core.Hosting.RequestOrigin.ActivatedVoice);
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        await f.RaiseActivatedTranscriptAsync("Nova, reset call.manual-active", 1);
        f.ViewModel.IsManualCallActive.Should().BeTrue();
        await f.RunAsync("Nova, set call.manual-active to off");
        f.ViewModel.IsManualCallActive.Should().BeFalse();
        await f.ViewModel.EnableManualCallCommand.ExecuteAsync();
        await f.ViewModel.ResetManualCallCommand.ExecuteAsync();
        f.ViewModel.IsManualCallActive.Should().BeFalse();
        await f.RunAsync("set call.manual-active to on");
        await f.RunAsync("reset call.manual-active");
        f.ViewModel.IsManualCallActive.Should().BeFalse();
        await f.RaiseActivatedTranscriptAsync("Nova, get call.manual-active", 1);
        f.Voice.StartedPhrases.Should().Contain("Nova set call.manual-active to off").And.Contain("set call.manual-active to off");
        f.Voice.StartCalls.Should().Be(starts + 3);
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.CallPreferences.SavedSettings.Should().BeNull();
        f.AudioPreferences.SavedMicrophoneId.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reserved_exact_call_routes_preserve_complete_pending_question_and_security_approval(bool question)
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "ready");
        f.Reasoner.Action = question ? null : BuiltInAction.LockMachine;
        if (question) { f.Reasoner.Question = new("Exact choice?", ["one", "two"]); }
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("please do this work");
        await f.ViewModel.ActiveReasoningTask!;
        var preview = f.ViewModel.ResponseBody;
        var title = f.ViewModel.ResponseTitle;
        var choices = f.ViewModel.ModelQuestionChoices;
        var auditCount = f.Audit.Events.Count;
        var tasks = f.ManualCallStore.Tasks.Count;
        foreach (var input in new[] { "list call settings", "get call.manual-active", "status call.manual-active",
            "set call.manual-active to on", "set call.manual-active to off", "reset call.manual-active", "set call.manual-active to true" })
        {
            await f.RunAsync(input);
            f.ViewModel.ResponseBody.Should().Be(preview);
            f.ViewModel.ResponseTitle.Should().Be(title);
            f.ViewModel.ModelQuestionChoices.Should().BeSameAs(choices);
            f.ViewModel.IsResponseInteractionPending.Should().BeTrue();
            f.ViewModel.IsManualCallActive.Should().BeFalse();
        }
        await f.ViewModel.EnableManualCallCommand.ExecuteAsync();
        await f.ViewModel.ResetManualCallCommand.ExecuteAsync();
        await f.ViewModel.GetManualCallStatusCommand.ExecuteAsync();
        f.ViewModel.ResponseBody.Should().Be(preview);
        f.Audit.Events.Should().HaveCount(auditCount);
        f.ManualCallStore.Tasks.Should().HaveCount(tasks);
        f.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        f.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Unknown)]
    [InlineData(CallState.Unavailable)]
    public async Task Exact_off_and_reset_never_change_automatic_evidence_or_saved_call_flags(CallState automatic)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAllowVoiceActivationDuringCallsAsync(false);
        f.CallState.SetState(automatic);
        await f.RunAsync("set call.manual-active to on");
        await f.RunAsync("set call.manual-active to off");
        await f.RunAsync("set call.manual-active to on");
        await f.RunAsync("reset call.manual-active");
        f.ViewModel.AutomaticCallState.Should().Be(automatic);
        f.ViewModel.IsProtectedCall.Should().Be(automatic != CallState.Unavailable);
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.ViewModel.AllowVoiceActivationDuringCalls.Should().BeFalse();
        f.CallPreferences.SavedSettings.Should().Be(new CallAwareSettings(true, false));
        f.ViewModel.Transcript.Should().Contain("\"sourceRevision\":").And.Contain("\"scope\":\"current-process-run");
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("lock")]
    [InlineData("input")]
    [InlineData("dispose")]
    [InlineData("audit")]
    [InlineData("outcome")]
    [InlineData("native-close")]
    [InlineData("native-reopen")]
    public async Task Late_native_admission_and_required_audit_failures_are_not_success_or_replay(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ManualCallStore.BeforeOperation = () =>
        {
            switch (change)
            {
                case "owner": f.ViewModel.BindCallOwnershipGate(static () => false); break;
                case "lock": f.Session.IsUnlocked = false; break;
                case "input": f.ViewModel.CloseForObservedPrivacyEvent("changed input", hidePresentation: false); break;
                case "dispose": f.ViewModel.Dispose(); break;
                case "audit": f.ManualCallStore.FailRequestedAudit = true; break;
                case "outcome": f.ManualCallStore.FailOutcomeAudit = true; break;
                case "native-close": f.ViewModel.BindManualCallNativeLifetime(false); break;
                case "native-reopen":
                    f.ViewModel.BindManualCallNativeLifetime(false);
                    f.ViewModel.BindManualCallNativeLifetime(true);
                    break;
            }
        };
        await f.ViewModel.EnableManualCallCommand.ExecuteAsync();
        f.ViewModel.IsManualCallActive.Should().Be(change is "outcome");
        if (change is not "dispose") { f.ViewModel.Transcript.Should().Contain("not-confirmed"); }
        f.ManualCallStore.BeforeOperation = null;
        f.ManualCallStore.FailRequestedAudit = false;
        f.ManualCallStore.FailOutcomeAudit = false;
        f.ViewModel.BindCallOwnershipGate(static () => true);
        f.ViewModel.BindManualCallNativeLifetime(true);
        f.Session.IsUnlocked = true;
        await f.ViewModel.ClearManualCallCommand.ExecuteAsync();
        f.ViewModel.IsManualCallActive.Should().Be(change is "outcome");
        f.CallPreferences.SavedSettings.Should().BeNull();
    }

    [Fact]
    public async Task Manual_transition_retires_pending_synthesis_and_future_speech_from_an_old_local_request_without_hiding_full_result()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "ready");
        await f.ViewModel.InitializeAsync();
        f.TextToSpeech.ClearSpokenResponse();
        f.Reasoner.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await f.RunAsync("please answer this work");
        var starts = f.Voice.StartCalls;
        await f.RunAsync("set call.manual-active to on");
        await f.RunAsync("reset call.manual-active");
        f.Reasoner.Gate.SetResult("Complete old response must remain visual.");
        await f.ViewModel.ActiveReasoningTask!;
        f.ViewModel.ResponseBody.Should().Contain("Complete old response must remain visual.");
        f.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Voice.StartCalls.Should().Be(starts);
        await f.RunAsync("what power action is pending");
        f.TextToSpeech.SpokenText.Should().NotBeNull();
    }

    [Fact]
    public async Task Missing_control_and_unavailable_live_prefix_are_explicit_without_unprefixed_fallback_aliases()
    {
        var missing = new Fixture(enableManualCallControl: false);
        await missing.ViewModel.InitializeAsync();
        missing.ViewModel.ManualCallConfigurationStatus.Should().Contain("admission is unavailable");
        await missing.ViewModel.GetManualCallStatusCommand.ExecuteAsync();
        await missing.RunAsync("set call.manual-active to on");
        missing.ViewModel.Transcript.Should().Contain("admission is unavailable");
        missing.ViewModel.IsManualCallActive.Should().BeFalse();
        var f = new Fixture();
        f.NamePreferences.Name = "Supported Commands";
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("Kora, set call.manual-active to on");
        f.ViewModel.Transcript.Should().Contain("prefix routing is unavailable");
        f.ViewModel.IsManualCallActive.Should().BeFalse();
        await f.RunAsync("set call.manual-active to on");
        f.ViewModel.IsManualCallActive.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Post_effect_owner_or_disposal_loss_never_publishes_a_success_receipt_into_a_foreign_lifetime(bool dispose)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ManualCallStore.AfterOperation = () =>
        {
            if (dispose) { f.ViewModel.Dispose(); }
            else { f.ViewModel.BindCallOwnershipGate(static () => false); }
        };
        await f.ViewModel.EnableManualCallCommand.ExecuteAsync();
        f.ViewModel.IsManualCallActive.Should().BeTrue();
        f.ViewModel.Transcript.Should().NotContain("\"outcome\":\"applied\"");
    }

    [Fact]
    public async Task Question_arriving_during_admitted_native_control_keeps_its_complete_preview_and_failed_automatic_stop_keeps_it_visible()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "ready");
        f.Reasoner.Question = new("Keep all options?", ["one", "two"]);
        await f.ViewModel.InitializeAsync();
        f.ManualCallStore.AfterOperation = () =>
            f.RunAsync("please do this work").IsCompletedSuccessfully.Should().BeTrue();
        await f.ViewModel.EnableManualCallCommand.ExecuteAsync();
        await f.ViewModel.ActiveReasoningTask!;
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        var preview = f.ViewModel.ResponseBody;
        f.TextToSpeech.StopException = new IOException("owned closure failure");
        f.CallState.SetState(CallState.Unknown);
        var release = () => f.ViewModel.CallClosureTask;
        await release.Should().ThrowAsync<IOException>();
        f.ViewModel.ResponseBody.Should().Be(preview);
        f.ViewModel.Transcript.Should().Contain("complete pending interaction remains visual");
        f.ViewModel.IsVisualResponseVisible.Should().BeTrue();
    }

    [Fact]
    public async Task Closing_native_lifetime_during_retirement_denies_the_transition_but_not_a_new_original_typed_request()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.TextToSpeech.BeforeStop = () => f.ViewModel.BindManualCallNativeLifetime(false);
        await f.ViewModel.EnableManualCallCommand.ExecuteAsync();
        f.ViewModel.IsManualCallActive.Should().BeFalse();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.ManualCallStore.Tasks.Last().State.Should().Be(Kora.Core.Hosting.HostTaskState.Denied);
        f.TextToSpeech.BeforeStop = null;
        await f.RunAsync("set call.manual-active to on");
        f.ViewModel.IsManualCallActive.Should().BeTrue();
    }
}
