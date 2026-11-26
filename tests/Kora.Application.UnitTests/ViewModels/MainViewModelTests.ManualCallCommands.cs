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
    public async Task Question_arriving_during_admitted_native_control_keeps_its_complete_preview_and_failed_automatic_stop_keeps_it_visible()
    {
        var f = new Fixture();
        await using var pendingDetails = new PendingDetailsQuestion(f);
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "ready");
        await f.ViewModel.InitializeAsync();
        Task? question = null;
        f.ManualCallStore.AfterOperation = () => question = pendingDetails.PresentAsync();
        await f.ViewModel.EnableManualCallCommand.ExecuteAsync();
        await question!;
        f.ViewModel.HasDetailsQuestion.Should().BeTrue();
        var preview = f.ViewModel.ResponseBody;
        f.TextToSpeech.StopException = new IOException("owned closure failure");
        f.CallState.SetState(CallState.Unknown);
        var release = () => f.ViewModel.CallClosureTask;
        await release.Should().ThrowAsync<IOException>();
        f.ViewModel.ResponseBody.Should().Be(preview);
        f.ViewModel.Transcript.Should().Contain("complete pending interaction remains visual");
        f.ViewModel.IsVisualResponseVisible.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_typed_and_real_activated_manual_control_share_run_only_state_without_synthesis_or_capture_reopen(bool natural)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.TextToSpeech.ClearSpokenResponse();
        await f.ViewModel.SetAssistantNameAsync("Nova", Kora.Core.Auditing.SecurityAuditInitiator.LocalUser);
        var starts = f.Voice.StartCalls;
        var tasks = f.ManualCallStore.Tasks.Count;
        foreach (var input in new[] { "list call settings", "Nova, get call.manual-active", "status call.manual-active",
            "show manual call status", "Nova, show manual call status" })
        {
            await f.RunAsync(input);
            using var json = JsonDocument.Parse(f.ViewModel.Transcript);
            json.RootElement.GetProperty("outcome").GetString().Should().Be("observed");
            json.RootElement.GetProperty("observation").GetProperty("manualActive").GetBoolean().Should().BeFalse();
            json.RootElement.GetProperty("automaticDetectorAvailable").GetBoolean().Should().BeFalse();
        }
        await f.ViewModel.GetManualCallStatusCommand.ExecuteAsync();
        f.ManualCallStore.Tasks.Should().HaveCount(tasks);
        await f.RaiseActivatedTranscriptAsync(natural ? "Nova, turn on manual call protection" : "Nova, set call.manual-active to on", 1);
        f.ViewModel.IsManualCallActive.Should().BeTrue();
        f.ManualCallStore.LastRequest!.Origin.Should().Be(Kora.Core.Hosting.RequestOrigin.ActivatedVoice);
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        await f.RaiseActivatedTranscriptAsync(natural ? "Nova, turn off manual call protection" : "Nova, reset call.manual-active", 1);
        f.ViewModel.IsManualCallActive.Should().BeTrue();
        await f.RunAsync(natural ? "Nova, turn off manual call protection" : "Nova, set call.manual-active to off");
        f.ViewModel.IsManualCallActive.Should().BeFalse();
        await f.ViewModel.EnableManualCallCommand.ExecuteAsync();
        await f.ViewModel.ResetManualCallCommand.ExecuteAsync();
        f.ViewModel.IsManualCallActive.Should().BeFalse();
        await f.RunAsync("set call.manual-active to on");
        await f.RunAsync("reset call.manual-active");
        f.ViewModel.IsManualCallActive.Should().BeFalse();
        await f.RaiseActivatedTranscriptAsync("Nova, get call.manual-active", 1);
        f.Voice.StartedPhrases.Should().Contain("Nova set call.manual-active to off").And.Contain("set call.manual-active to off");
        foreach (var phrase in new[] { "turn on manual call protection", "turn off manual call protection", "show manual call status" })
        {
            f.Voice.StartedPhrases.Should().Contain(phrase).And.Contain("Nova " + phrase);
        }
        f.Voice.StartCalls.Should().Be(starts + 3);
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.CallPreferences.SavedSettings.Should().BeNull();
        f.AudioPreferences.SavedMicrophoneId.Should().BeNull();

    }

    [Fact]
    public async Task NaturalCommandsRemainAvailableDuringBusyBootstrap()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.Probe.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = f.ViewModel.DetectMicrophonesAsync();
        try
        {
            f.ViewModel.IsBusy.Should().BeTrue();
            foreach (var input in new[] { "turn on manual call protection", "turn off manual call protection",
                "show manual call status", "turn on manual call protection please", "show manual call" })
            {
                f.ViewModel.CommandText = input;
                f.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue();
            }
            await f.RunAsync("show manual call status");
            f.ViewModel.IsBusy.Should().BeTrue();

            f.ManualCallStore.Tasks.Should().BeEmpty();
        }
        finally
        {
            f.Probe.Gate.SetResult();
            await busy;
        }
    }

    [Theory]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Unknown)]
    public async Task NaturalVoiceDoesNotAcquireAProtectedCallException(CallState automatic)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("turn on manual call protection");
        f.CallState.SetState(automatic);
        foreach (var phrase in new[] { "turn off manual call protection", "turn on manual call protection" })
        {
            await f.RaiseActivatedTranscriptAsync("Kora, " + phrase, 1);
            f.ViewModel.IsManualCallActive.Should().BeTrue();
            f.ManualCallStore.LastRequest!.Origin.Should().Be(Kora.Core.Hosting.RequestOrigin.ActivatedVoice);
            f.ManualCallStore.Tasks.Last().State.Should().Be(Kora.Core.Hosting.HostTaskState.Denied);
        }
        await f.RunAsync("turn off manual call protection");
        await f.RunAsync("show manual call status");
        using var status = JsonDocument.Parse(f.ViewModel.Transcript);
        var observation = status.RootElement.GetProperty("observation");
        observation.GetProperty("manualActive").GetBoolean().Should().BeFalse();
        observation.GetProperty("automaticState").GetString().Should().Be(automatic.ToString());
        observation.GetProperty("isProtected").GetBoolean().Should().BeTrue();

    }

    [Theory]
    [InlineData("owner")]
    [InlineData("lock")]
    [InlineData("privacy")]
    [InlineData("voice-generation")]
    [InlineData("call-revision")]
    [InlineData("session-generation")]
    [InlineData("receipt")]
    public async Task NaturalVoiceRechecksOriginalHostSourceAndGenerationBeforeCommit(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ManualCallStore.BeforeOperation = () =>
        {
            switch (change)
            {
                case "owner": f.ViewModel.BindCallOwnershipGate(static () => false); break;
                case "lock": f.Session.IsUnlocked = false; break;
                case "privacy":
                    f.PrivacyObservation.Current = f.PrivacyObservation.Current with { TopologyRevision = 99 };
                    break;
                case "voice-generation": f.Voice.AdvanceGeneration(); break;
                case "call-revision": f.CallState.SetState(CallState.Unknown); break;
                case "session-generation": f.ManualCallStore.Authority = f.ManualCallStore.Authority! with { Generation = new(2) }; break;
                case "receipt": f.ManualCallStore.FailTerminal = true; break;
            }
        };
        await f.RaiseActivatedTranscriptAsync("Kora, turn on manual call protection", 1);
        f.ViewModel.IsManualCallActive.Should().Be(change is "receipt");
        f.ManualCallStore.LastRequest!.Origin.Should().Be(Kora.Core.Hosting.RequestOrigin.ActivatedVoice);
        f.ManualCallStore.BeforeOperation = null;
        f.ManualCallStore.FailTerminal = false;
        if (change is "receipt")
        {
            f.ViewModel.CallObservation.ManualControlEvidenceUnavailable.Should().BeTrue();
            await f.RunAsync("turn off manual call protection");
            f.ViewModel.IsManualCallActive.Should().BeTrue();
        }
        f.CallPreferences.SavedSettings.Should().BeNull();

    }

    [Fact]
    public async Task NaturalMutationAwaitsRetirementAndLateCallbacksNeverClearOrReopenIt()
    {
        var f = await Fixture.CreateInitializedAsync();
        var starts = f.Voice.StartCalls;
        var retirement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Voice.StopGate = retirement;
        var mutation = f.RunAsync("turn on manual call protection");
        mutation.IsCompleted.Should().BeFalse();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.ViewModel.Transcript.Should().NotContain("\"outcome\":\"applied\"");
        retirement.SetResult();
        await mutation;
        f.ViewModel.IsManualCallActive.Should().BeTrue();
        await f.Voice.RaiseTranscriptAsync("Kora, turn off manual call protection", 1);
        f.ViewModel.IsManualCallActive.Should().BeTrue();
        await f.RunAsync("turn off manual call protection");
        f.ViewModel.IsManualCallActive.Should().BeFalse();
        f.Voice.StartCalls.Should().Be(starts);

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
        await f.RunAsync("turn on manual call protection");
        await f.RunAsync("turn off manual call protection");
        await f.RunAsync("turn on manual call protection");
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
    public async Task Missing_control_and_unavailable_live_prefix_are_explicit_without_unprefixed_fallback_aliases()
    {
        var missing = new Fixture(enableManualCallControl: false);
        await missing.ViewModel.InitializeAsync();
        missing.ViewModel.ManualCallConfigurationStatus.Should().Contain("controls are unavailable");
        await missing.ViewModel.GetManualCallStatusCommand.ExecuteAsync();
        await missing.RunAsync("turn on manual call protection");
        missing.ViewModel.Transcript.Should().Contain("admission is unavailable");
        missing.ViewModel.IsManualCallActive.Should().BeFalse();
        var f = new Fixture();
        f.NamePreferences.Name = "Supported Commands";
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("Kora, turn on manual call protection");
        f.ViewModel.Transcript.Should().Contain("prefix routing is unavailable");
        f.ViewModel.IsManualCallActive.Should().BeFalse();
        await f.RunAsync("turn on manual call protection");
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
