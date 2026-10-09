using System.Text.Json;
using System.Reflection;
using System.Diagnostics;

using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.ViewModels;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Platform;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task Exact_discovery_get_native_selection_typed_reset_and_activated_choice_share_one_preference()
    {
        var f = CreateVoicePrivacyFixture();
        f.Voice.Microphones = [new("mic", "Headset"), new("second", "Headset")];
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("Kora, list input settings");
        using (var json = JsonDocument.Parse(f.ViewModel.ResponseBody))
        {
            json.RootElement.GetProperty("source").GetString().Should().Be("default");
            json.RootElement.GetProperty("desired").GetString().Should().Be("system-default");
            json.RootElement.GetProperty("effective").GetString().Should().Be("mic");
            json.RootElement.GetProperty("choices").GetArrayLength().Should().Be(3);
        }
        await f.RunAsync("set speech.input-device to Headset");
        f.AudioPreferences.MicrophoneId.Should().BeNull();
        f.ViewModel.ResponseTitle.Should().Contain("Exact");
        await f.RunAsync("set speech.input-device to MIC");
        f.AudioPreferences.MicrophoneId.Should().BeNull();
        await f.RunAsync("set speech.input-device to mic");
        f.AudioPreferences.MicrophoneId.Should().Be("mic");
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.ViewModel.IsListening.Should().BeFalse();
        using (var json = JsonDocument.Parse(f.ViewModel.ResponseBody))
        {
            json.RootElement.GetProperty("outcome").GetString().Should().Be("saved");
            json.RootElement.GetProperty("source").GetString().Should().Be("saved");
        }
        using var card = new MicrophoneRecoveryViewModel(f.ViewModel);
        await card.SaveAsync(card.Choices.Single(item => string.Equals(item.Device.Id, "second", StringComparison.Ordinal)));
        f.AudioPreferences.MicrophoneId.Should().Be("second");
        await f.RunAsync("get speech.input-device");
        f.ViewModel.ResponseBody.Should().Contain("second").And.Contain("input-preference-only");
        await f.RunAsync("reset speech.input-device");
        f.AudioPreferences.MicrophoneId.Should().BeNull();
        f.ViewModel.SelectedMicrophone.Should().Be(SystemAudioDevices.Microphone);
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.Voice.StartCalls.Should().Be(0);
        await card.EnableAsync(card.DisplayedSelection);
        await f.RaiseActivatedTranscriptAsync("Kora, set speech.input-device to second", 1);
        f.AudioPreferences.MicrophoneId.Should().Be("second");
        f.Audit.Events.Should().Contain(item => item.ActionId == InputDevicePreferenceService.AuditAction
            && item.Initiator == SecurityAuditInitiator.VoiceCommand && item.Outcome == SecurityAuditOutcome.Succeeded);
        f.VoiceConsent.Consent.Should().BeTrue();
        f.Reasoner.Requests.Should().BeEmpty();
        f.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Fact]
    public async Task Saved_missing_pin_and_System_default_changes_are_truthful_without_substitution_or_auto_arm()
    {
        var f = CreateVoicePrivacyFixture();
        f.AudioPreferences.MicrophoneId = "missing";
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("list input settings");
        using (var json = JsonDocument.Parse(f.ViewModel.ResponseBody))
        {
            json.RootElement.GetProperty("desired").GetString().Should().Be("missing");
            json.RootElement.GetProperty("effective").ValueKind.Should().Be(JsonValueKind.Null);
            json.RootElement.GetProperty("source").GetString().Should().Be("saved");
            json.RootElement.GetProperty("recovery").GetString().Should().Contain("unavailable");
        }
        await f.RunAsync("set speech.input-device to missing");
        f.ViewModel.ResponseTitle.Should().Contain("Exact");
        await f.RunAsync("reset speech.input-device");
        f.AudioPreferences.MicrophoneId.Should().BeNull();
        f.Voice.Microphones = [new("other", "Headset")];
        f.Voice.DefaultMicrophoneId = "other";
        f.PrivacyObservation.Current = f.PrivacyObservation.Current with
        { ActiveMicrophoneIds = ["other"], DefaultMicrophoneId = "other" };
        await f.RunAsync("list input settings");
        using (var json = JsonDocument.Parse(f.ViewModel.ResponseBody))
        {
            json.RootElement.GetProperty("desired").GetString().Should().Be("system-default");
            json.RootElement.GetProperty("effective").GetString().Should().Be("other");
        }
        f.Voice.Microphones = [];
        f.Voice.DefaultMicrophoneId = null;
        await f.RunAsync("list input settings");
        await f.RunAsync("reset speech.input-device");
        f.ViewModel.SelectedMicrophone.Should().Be(SystemAudioDevices.Microphone);
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.Voice.StartCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Suspected)]
    [InlineData(CallState.Unknown)]
    public async Task Original_voice_mutation_is_denied_for_protected_or_unknown_calls_but_local_reset_remains_separate(CallState state)
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        f.CallState.SetState(state);
        using (var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request))
        {
            await f.ViewModel.ExecuteInputDeviceCommandAsync(new(AppearanceCommandOperation.Set, "mic"), SecurityAuditInitiator.LocalUser, TestContext.Current.CancellationToken);
            await f.ViewModel.ExecuteInputDeviceCommandAsync(new(AppearanceCommandOperation.Reset), SecurityAuditInitiator.LocalUser, TestContext.Current.CancellationToken);
        }
        f.AudioPreferences.MicrophoneId.Should().BeNull();
        f.Audit.Events.Should().Contain(item => item.ActionId == InputDevicePreferenceService.AuditAction
            && item.Initiator == SecurityAuditInitiator.VoiceCommand && item.Outcome == SecurityAuditOutcome.Denied);
        await f.RunAsync("set speech.input-device to mic");
        f.AudioPreferences.MicrophoneId.Should().Be("mic");
        await f.RunAsync("reset speech.input-device");
        f.AudioPreferences.MicrophoneId.Should().BeNull();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Forged_equal_device_cross_host_and_stale_revisions_are_not_presented_authority()
    {
        var f = CreateVoicePrivacyFixture();
        var other = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        await other.ViewModel.InitializeAsync();
        var current = f.ViewModel.Microphones[1];
        await f.ViewModel.SelectMicrophoneAsync(current with { }, f.ViewModel.MicrophoneTopologyRevision);
        await f.ViewModel.SelectMicrophoneAsync(other.ViewModel.Microphones[1], f.ViewModel.MicrophoneTopologyRevision);
        await f.ViewModel.SelectMicrophoneAsync(current, f.ViewModel.MicrophoneTopologyRevision - 1);
        f.AudioPreferences.MicrophoneId.Should().BeNull();
        f.ViewModel.SelectedMicrophone.Should().Be(SystemAudioDevices.Microphone);
        await f.RunAsync("get speech.input-device extra");
        f.ViewModel.ResponseTitle.Should().Contain("Clarify");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("call")]
    [InlineData("owner")]
    [InlineData("privacy")]
    [InlineData("disable")]
    [InlineData("cancel")]
    [InlineData("dispose")]
    public async Task Deferred_exact_selection_rechecks_original_context_and_host_generations(string race)
    {
        var completion = new TaskCompletionSource<MicrophoneCatalogSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = new BoundedMicrophoneCatalog(() => completion.Task, TimeProvider.System, NullLogger.Instance);
        var f = CreateVoicePrivacyFixture(microphoneCatalog: catalog);
        await f.ViewModel.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        var operation = f.ViewModel.ExecuteInputDeviceCommandAsync(new(AppearanceCommandOperation.Set, "mic"),
            SecurityAuditInitiator.TypedCommand, cancellation.Token);
        switch (race)
        {
            case "call": f.CallState.SetState(CallState.Active); break;
            case "owner": f.ViewModel.BindCallOwnershipGate(static () => false); break;
            case "privacy": f.PrivacyObservation.Current = f.PrivacyObservation.Current with { MicrophoneAccess = MicrophoneAccessState.Unknown }; break;
            case "disable": await f.ViewModel.DisableListeningFromTrayAsync(); break;
            case "cancel": cancellation.Cancel(); break;
            case "dispose": f.ViewModel.Dispose(); break;
        }
        completion.SetResult(TraySnapshot());
        await operation;
        f.AudioPreferences.MicrophoneId.Should().BeNull();
        f.Voice.StartCalls.Should().Be(0);
        f.VoiceConsent.Consent.Should().BeTrue();
    }

    [Fact]
    public async Task Audit_time_call_and_privacy_changes_and_reentrant_native_selection_cannot_commit()
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        f.ViewModel.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(f.ViewModel.MicrophoneTopologyRevision), StringComparison.Ordinal))
            {
                f.ViewModel.SelectedMicrophone = f.ViewModel.Microphones[1];
                f.CallState.SetState(CallState.Active);
            }
        };
        await f.RunAsync("set speech.input-device to mic");
        f.AudioPreferences.MicrophoneId.Should().BeNull();
        f.ViewModel.ResponseBody.Should().Contain("not-confirmed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_and_audit_failure_are_explicit_no_success_live_change_or_automatic_retry(bool audit)
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        if (audit) { f.Audit.BeforeWrite = _ => throw new IOException("fixture evidence failure"); }
        else { f.AudioPreferences.MicrophoneSaveException = new UnauthorizedAccessException("fixture atomic failure"); }
        await f.RunAsync("set speech.input-device to mic");
        f.ViewModel.ResponseBody.Should().Contain("not-confirmed").And.Contain("evidence failed");
        f.ViewModel.SelectedMicrophone.Should().Be(SystemAudioDevices.Microphone);
        f.AudioPreferences.MicrophoneId.Should().BeNull();
        f.Voice.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task Input_commands_never_supersede_pending_questions_grants_or_approvals()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        f.Reasoner.Question = new("Which?", ["One", "Two"]);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("choose");
        await f.ViewModel.ActiveReasoningTask!;
        await f.RunAsync("list input settings");
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        await f.ViewModel.CancelModelQuestionAsync();
        f.ViewModel.PrepareGrantChange(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
        await f.RunAsync("reset speech.input-device");
        f.ViewModel.IsGrantChangePending.Should().BeTrue();
        await f.ViewModel.RejectPendingGrantChangeAsync();
        f.Reasoner.Question = null;
        f.Reasoner.Action = BuiltInAction.LockMachine;
        await f.RunAsync("please lock");
        await f.ViewModel.ActiveReasoningTask!;
        await f.RunAsync("get speech.input-device");
        f.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        f.ViewModel.ResponseTitle.Should().Be("Input configuration unavailable.");
        f.AudioPreferences.MicrophoneId.Should().BeNull();
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("dispose")]
    [InlineData("owner")]
    [InlineData("cancel")]
    public async Task Discovery_failure_cancellation_and_late_ineligible_results_do_not_present_live_choices(string mode)
    {
        var completion = new TaskCompletionSource<MicrophoneCatalogSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = new BoundedMicrophoneCatalog(() => completion.Task, TimeProvider.System, NullLogger.Instance);
        var f = CreateVoicePrivacyFixture(microphoneCatalog: catalog);
        await f.ViewModel.InitializeAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var operation = f.ViewModel.ExecuteInputDeviceCommandAsync(new(AppearanceCommandOperation.List),
            SecurityAuditInitiator.TypedCommand, cancellation.Token);
        if (string.Equals(mode, "dispose", StringComparison.Ordinal)) { f.ViewModel.Dispose(); }
        if (string.Equals(mode, "owner", StringComparison.Ordinal)) { f.ViewModel.BindCallOwnershipGate(static () => false); }
        if (string.Equals(mode, "cancel", StringComparison.Ordinal)) { cancellation.Cancel(); }
        if (string.Equals(mode, "failure", StringComparison.Ordinal)) { completion.SetException(new IOException("metadata unavailable")); }
        else { completion.SetResult(TraySnapshot()); }
        if (string.Equals(mode, "cancel", StringComparison.Ordinal))
        {
            var finish = () => operation;
            await finish.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            await operation;
            if (string.Equals(mode, "failure", StringComparison.Ordinal))
            {
                using var json = JsonDocument.Parse(f.ViewModel.ResponseBody);
                json.RootElement.GetProperty("metadataCurrent").GetBoolean().Should().BeFalse();
                json.RootElement.GetProperty("effective").ValueKind.Should().Be(JsonValueKind.Null);
                json.RootElement.GetProperty("recovery").GetString().Should().Contain("unavailable");
            }
            if (string.Equals(mode, "owner", StringComparison.Ordinal)) { f.ViewModel.ResponseTitle.Should().Be("Input configuration unavailable."); }
        }
        f.AudioPreferences.MicrophoneId.Should().BeNull();
        f.Voice.StartCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("observation")]
    [InlineData("null")]
    [InlineData("topology")]
    public async Task Status_never_infers_effective_input_from_unknown_permission_empty_selection_or_stale_topology(string kind)
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        if (string.Equals(kind, "permission", StringComparison.Ordinal)) { f.MicrophoneAccess.Status = new(MicrophoneAccessState.Unknown, "synthetic"); await f.ViewModel.RefreshMicrophonesAsync(); }
        if (string.Equals(kind, "observation", StringComparison.Ordinal)) { f.PrivacyObservation.Current = f.PrivacyObservation.Current with { MicrophoneAccess = MicrophoneAccessState.Unknown }; }
        if (string.Equals(kind, "null", StringComparison.Ordinal)) { f.ViewModel.SelectedMicrophone = null; f.ViewModel.SelectedMicrophone = null; }
        if (string.Equals(kind, "topology", StringComparison.Ordinal)) { f.PrivacyObservation.Current = f.PrivacyObservation.Current with { TopologyRevision = 10 }; }
        await f.RunAsync("get speech.input-device");
        using var json = JsonDocument.Parse(f.ViewModel.ResponseBody);
        json.RootElement.GetProperty("effective").ValueKind.Should().Be(JsonValueKind.Null);
        f.AudioPreferences.MicrophoneId.Should().BeNull();
    }

    [Fact]
    public async Task Direct_native_writes_preserve_live_host_lineage_and_cannot_reenter_committing_state()
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        f.ViewModel.SelectedMicrophone = f.ViewModel.Microphones[1];
        f.AudioPreferences.MicrophoneId.Should().Be("mic");
        var guard = typeof(MainViewModel).GetField("committingMicrophonePreference", BindingFlags.Instance | BindingFlags.NonPublic)!;
        guard.SetValue(f.ViewModel, true);
        var commit = typeof(MainViewModel).GetMethod("CommitMicrophonePreference", BindingFlags.Instance | BindingFlags.NonPublic)!;
        commit.Invoke(f.ViewModel, [SystemAudioDevices.Microphone, f.ViewModel.MicrophoneTopologyRevision,
            f.ViewModel.CallPolicyRevision, RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser, activity.Request,
            0L, TestContext.Current.CancellationToken]).Should().Be(false);
        guard.SetValue(f.ViewModel, false);
        await f.ViewModel.ExecuteInputDeviceCommandAsync(new(AppearanceCommandOperation.Reset),
            SecurityAuditInitiator.VoiceCommand, TestContext.Current.CancellationToken);
        f.AudioPreferences.MicrophoneId.Should().Be("mic");
        f.ViewModel.ResponseBody.Should().Contain("original voice channel");
    }

    [Fact]
    public async Task Read_request_on_unknown_owner_cannot_expose_endpoint_metadata()
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        f.ViewModel.BindCallOwnershipGate(static () => false);
        await f.ViewModel.ExecuteInputDeviceCommandAsync(new(AppearanceCommandOperation.Get),
            SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        f.ViewModel.ResponseTitle.Should().Be("Input configuration unavailable.");
        f.ViewModel.ResponseBody.Should().NotContain("Headset");
    }

    [Fact]
    public async Task Replaced_preference_with_failed_native_release_is_not_a_success_or_enablement()
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        f.Voice.StopException = new IOException("fixture release failed");
        var saved = await f.ViewModel.SelectMicrophoneAsync(f.ViewModel.Microphones[1], f.ViewModel.MicrophoneTopologyRevision);
        saved.Should().BeFalse();
        f.AudioPreferences.MicrophoneId.Should().Be("mic");
        f.ViewModel.SelectedMicrophone!.Id.Should().Be("mic");
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("missing", false)]
    [InlineData("missing", true)]
    [InlineData("system-default", false)]
    [InlineData("system-default", true)]
    public async Task Restart_retains_saved_desire_even_when_detection_fails_without_claiming_a_default_or_effective_route(string id, bool fail)
    {
        var f = CreateVoicePrivacyFixture();
        f.AudioPreferences.MicrophoneId = id;
        if (fail) { f.Voice.GetMicrophonesException = new IOException("metadata unavailable"); }
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("get speech.input-device");
        using var json = JsonDocument.Parse(f.ViewModel.ResponseBody);
        json.RootElement.GetProperty("desired").GetString().Should().Be(id);
        json.RootElement.GetProperty("source").GetString().Should().Be("saved");
        json.RootElement.GetProperty("metadataCurrent").GetBoolean().Should().Be(!fail);
        if (fail) { json.RootElement.GetProperty("effective").ValueKind.Should().Be(JsonValueKind.Null); }
        f.AudioPreferences.MicrophoneId.Should().Be(id);
        f.AudioPreferences.SavedMicrophoneId.Should().BeNull();
    }

    [Fact]
    public async Task Endpoint_content_and_incoming_trace_fields_are_not_authority_or_audit_activity_tags()
    {
        var f = CreateVoicePrivacyFixture();
        f.Voice.Microphones = [new("private-id", "Private friendly name")];
        f.Voice.DefaultMicrophoneId = "private-id";
        f.PrivacyObservation.Current = f.PrivacyObservation.Current with
        { ActiveMicrophoneIds = ["private-id"], DefaultMicrophoneId = "private-id" };
        await f.ViewModel.InitializeAsync();
        var requests = new List<HostRequest>();
        var traces = new List<ActivityTraceId>();
        f.Audit.BeforeWrite = item =>
        {
            if (!string.Equals(item.ActionId, InputDevicePreferenceService.AuditAction, StringComparison.Ordinal)) { return; }
            var current = HostActivity.RequireCurrent();
            requests.Add(current.Request);
            traces.Add(current.Activity!.TraceId);
            current.Activity.TagObjects.Should().NotContain(tag =>
                string.Equals(tag.Value!.ToString(), "private-id", StringComparison.Ordinal)
                || string.Equals(tag.Value.ToString(), "Private friendly name", StringComparison.Ordinal));
            current.Activity.Baggage.Should().BeEmpty();
        };
        using var incoming = new Activity("provider.untrusted").SetIdFormat(ActivityIdFormat.W3C).Start();
        incoming.AddBaggage("kora.session.id", "provider-selected-session");
        await f.RunAsync("set speech.input-device to private-id");
        requests.Should().HaveCount(2);
        requests[1].Should().BeSameAs(requests[0]);
        traces.Should().OnlyContain(trace => trace == traces[0]);
        traces[0].Should().NotBe(incoming.TraceId);
        f.AudioPreferences.MicrophoneId.Should().Be("private-id");
        f.VoiceConsent.Consent.Should().BeTrue();
    }

    [Fact]
    public async Task Terminal_invalid_audit_context_propagates_after_owned_input_release_without_live_selection()
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        f.Audit.BeforeWrite = item =>
        {
            if (item.Outcome == SecurityAuditOutcome.Succeeded) { throw new InvalidOperationException("audit context expired"); }
        };
        var select = () => f.ViewModel.SelectMicrophoneAsync(f.ViewModel.Microphones[1], f.ViewModel.MicrophoneTopologyRevision);
        await select.Should().ThrowAsync<InvalidOperationException>().WithMessage("audit context expired");
        f.AudioPreferences.MicrophoneId.Should().Be("mic");
        f.ViewModel.SelectedMicrophone.Should().Be(SystemAudioDevices.Microphone);
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.Voice.StopCalls.Should().BeGreaterThan(0);
        await f.RunAsync("get speech.input-device");
        using var json = JsonDocument.Parse(f.ViewModel.ResponseBody);
        json.RootElement.GetProperty("source").GetString().Should().Be("unavailable");
        json.RootElement.GetProperty("desired").ValueKind.Should().Be(JsonValueKind.Null);
        json.RootElement.GetProperty("effective").ValueKind.Should().Be(JsonValueKind.Null);
        f.ViewModel.ToggleListeningCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Missing_request_audit_context_propagates_without_changing_the_prior_preference()
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        f.Audit.BeforeWrite = _ => throw new InvalidOperationException("no admitted audit context");
        var select = () => f.ViewModel.SelectMicrophoneAsync(f.ViewModel.Microphones[1], f.ViewModel.MicrophoneTopologyRevision);
        await select.Should().ThrowAsync<InvalidOperationException>();
        f.AudioPreferences.MicrophoneId.Should().BeNull();
        f.ViewModel.SelectedMicrophone.Should().Be(SystemAudioDevices.Microphone);
    }

    [Fact]
    public async Task Terminal_IO_evidence_failure_holds_unknown_preference_until_a_new_explicit_successful_selection()
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        f.Audit.BeforeWrite = item =>
        {
            if (item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException("terminal evidence unavailable"); }
        };
        await f.RunAsync("set speech.input-device to mic");
        f.ViewModel.ResponseBody.Should().Contain("not-confirmed").And.Contain("unavailable");
        f.AudioPreferences.MicrophoneId.Should().Be("mic");
        f.ViewModel.ToggleListeningCommand.CanExecute(null).Should().BeFalse();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        using var card = new MicrophoneRecoveryViewModel(f.ViewModel);
        card.CanEnable.Should().BeFalse();
        f.Audit.BeforeWrite = null;
        await f.RunAsync("reset speech.input-device");
        f.AudioPreferences.MicrophoneId.Should().BeNull();
        f.ViewModel.ToggleListeningCommand.CanExecute(null).Should().BeTrue();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.Voice.StartCalls.Should().Be(0);
    }
}
