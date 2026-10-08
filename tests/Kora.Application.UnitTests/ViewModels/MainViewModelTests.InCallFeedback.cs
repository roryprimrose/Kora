using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private sealed class FakeInCallFeedbackPreferences : IInCallFeedbackPreferences
    {
        public InCallFeedbackMode? Value { get; set; }
        public Exception? ReadFailure { get; set; }
        public Exception? WriteFailure { get; set; }
        public Action? AfterWrite { get; set; }
        public bool Pending { get; set; }
        public InCallFeedbackMode? Load() => Pending ? throw new InvalidDataException("Unconfirmed feedback") : ReadBack();
        public InCallFeedbackMode? ReadBack() { if (ReadFailure is { } failure) { throw failure; } return Value; }
        public void BeginWrite() => Pending = true;
        public void ConfirmWrite() => Pending = false;
        public void Save(InCallFeedbackMode mode) { if (WriteFailure is { } failure) { throw failure; } Value = mode; AfterWrite?.Invoke(); }
        public void Reset() { Value = null; AfterWrite?.Invoke(); }
    }

    [Fact]
    public async Task Native_exact_and_current_activation_name_routes_share_authority_without_implicit_capture_consent_or_grants()
    {
        var fixture = new Fixture(enableInCallFeedback: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.ViewModel.CanInspectInCallFeedbackNative.Should().BeTrue();
        fixture.ViewModel.CanChangeInCallFeedbackNative.Should().BeTrue();
        var starts = fixture.Voice.StartCalls;
        var grants = fixture.ApprovalPreferences.Preferences;
        var consent = fixture.VoiceConsent.Consent;
        await fixture.RunAsync("Kora, list call feedback settings");
        fixture.ViewModel.ResponseBody.Should().Contain("\"default\":\"UI\"").And.Contain("\"source\":\"default\"")
            .And.Contain("\"saved\":null").And.Contain("\"applied\":false");
        await fixture.RunAsync("set calls.feedback-mode to Both");
        fixture.FeedbackPreferences.Value.Should().Be(InCallFeedbackMode.Both);
        await fixture.RunAsync("status calls.feedback-mode");
        fixture.ViewModel.ResponseBody.Should().Contain("\"source\":\"saved\"").And.Contain("\"desired\":\"Both\"");
        await fixture.ViewModel.RefreshInCallFeedbackCommand.ExecuteAsync();
        fixture.ViewModel.SelectedInCallFeedbackChoice = fixture.ViewModel.InCallFeedbackChoices.Single(choice => choice.Mode == InCallFeedbackMode.Voice);
        await fixture.ViewModel.SaveInCallFeedbackCommand.ExecuteAsync();
        fixture.FeedbackPreferences.Value.Should().Be(InCallFeedbackMode.Voice);
        await fixture.ViewModel.ResetInCallFeedbackCommand.ExecuteAsync();
        fixture.FeedbackPreferences.Value.Should().BeNull();
        fixture.ViewModel.InCallFeedbackStatus.Should().Contain("\"source\":\"default\"");
        fixture.Voice.StartCalls.Should().Be(starts);
        await fixture.RunAsync("set assistant.name to Nova");
        fixture.TextToSpeech.ClearSpokenResponse();
        await fixture.RaiseActivatedTranscriptAsync("Nova, set calls.feedback-mode to Inherit", 1);
        fixture.FeedbackPreferences.Value.Should().Be(InCallFeedbackMode.Inherit);
        fixture.Voice.StartedPhrases.Should().Contain("Nova set calls.feedback-mode to Inherit");
        await fixture.RaiseActivatedTranscriptAsync("Nova, reset calls.feedback-mode", 1);
        fixture.FeedbackPreferences.Value.Should().BeNull();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.AudioPreferences.SavedMicrophoneId.Should().BeNull();
        fixture.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
        fixture.OutputPreferences.SavedMode.Should().BeNull();
        fixture.CallPreferences.SavedSettings.Should().BeNull();
        fixture.Preferences.SavedVoiceId.Should().BeNull();
        fixture.ApprovalPreferences.Preferences.Should().BeSameAs(grants);
        fixture.VoiceConsent.Consent.Should().Be(consent);
        fixture.ViewModel.Dispose();
        fixture.FeedbackConfiguration!.HoldUnavailable();
    }

    [Theory]
    [InlineData(CallState.Active, InCallFeedbackMode.Voice, ResponseOutputMode.VoiceOnly, true)]
    [InlineData(CallState.Suspected, InCallFeedbackMode.Both, ResponseOutputMode.Hybrid, true)]
    [InlineData(CallState.Active, InCallFeedbackMode.UI, ResponseOutputMode.VisualOnly, true)]
    [InlineData(CallState.Active, InCallFeedbackMode.Inherit, ResponseOutputMode.Hybrid, false)]
    [InlineData(CallState.Clear, InCallFeedbackMode.UI, ResponseOutputMode.Hybrid, false)]
    [InlineData(CallState.Unavailable, InCallFeedbackMode.UI, ResponseOutputMode.Hybrid, false)]
    [InlineData(CallState.Unknown, InCallFeedbackMode.Voice, ResponseOutputMode.Hybrid, false)]
    public async Task Configured_precedence_is_distinct_from_mandatory_call_speech_and_visual_policy(
        CallState state, InCallFeedbackMode mode, ResponseOutputMode expected, bool applied)
    {
        var fixture = new Fixture(enableInCallFeedback: true);
        await using var admission = fixture.OutputAdmission;
        fixture.FeedbackPreferences.Value = mode;
        fixture.FeedbackConfiguration!.Observe();
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.ViewModel.QueueResponseMode = ResponseOutputMode.VisualOnly;
        fixture.ViewModel.TaskResponseMode = ResponseOutputMode.Hybrid;
        fixture.CallState.SetState(state);
        await fixture.ViewModel.CallClosureTask;
        fixture.ViewModel.EffectiveResponseMode.Should().Be(expected);
        fixture.ViewModel.IsInCallFeedbackOverrideApplied.Should().Be(applied);
        if (state is CallState.Active or CallState.Suspected or CallState.Unknown)
        {
            fixture.ViewModel.IsSpeechResponseEnabled.Should().BeFalse();
            fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        }
        await fixture.RunAsync("get calls.feedback-mode");
        fixture.ViewModel.ResponseBody.Should().Contain("\"effective\":\"" + expected + "\"");
        fixture.ViewModel.QueueResponseMode.Should().Be(ResponseOutputMode.VisualOnly);
        fixture.ViewModel.TaskResponseMode.Should().Be(ResponseOutputMode.Hybrid);
        fixture.ViewModel.AllowVoiceActivationDuringCalls.Should().BeTrue();
        fixture.CallPreferences.SavedSettings.Should().BeNull();
    }

    [Theory]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Suspected)]
    [InlineData(CallState.Unknown)]
    public async Task Original_voice_cannot_be_relabelled_as_ui_and_manual_active_has_same_override(CallState state)
    {
        var fixture = new Fixture(enableInCallFeedback: true);
        await using var admission = fixture.OutputAdmission;
        fixture.Voice.Microphones = [new MicrophoneDevice("fixture", "Synthetic microphone")];
        fixture.Voice.DefaultMicrophoneId = "fixture";
        await fixture.ViewModel.InitializeAsync();
        fixture.CallState.SetState(state);
        await fixture.ViewModel.CallClosureTask;
        using (var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice), HostActivityLayer.Application, HostOperation.Request))
        {
            await fixture.ViewModel.ExecuteInCallFeedbackCommandAsync(new(AppearanceCommandOperation.Set, InCallFeedbackMode.Both),
                SecurityAuditInitiator.LocalUser, cancellationToken: TestContext.Current.CancellationToken);
        }
        fixture.FeedbackPreferences.Value.Should().BeNull();
        fixture.ViewModel.ResponseBody.Should().Contain("\"outcome\":\"denied\"");
        fixture.Audit.Events.Should().Contain(item => item.ActionId == "configuration.in-call-feedback"
            && item.Initiator == SecurityAuditInitiator.VoiceCommand && item.Outcome == SecurityAuditOutcome.Denied);
        await fixture.RunAsync("set calls.feedback-mode to Voice");
        fixture.FeedbackPreferences.Value.Should().Be(InCallFeedbackMode.Voice);
        fixture.ViewModel.AreReusableGrantsIgnored.Should().BeTrue();
        fixture.CallState.SetState(CallState.Unavailable);
        await fixture.ViewModel.CallClosureTask;
        await fixture.ViewModel.EnableManualCallCommand.ExecuteAsync();
        fixture.ViewModel.IsManualCallActive.Should().BeTrue();
        fixture.ViewModel.EffectiveResponseMode.Should().Be(ResponseOutputMode.VoiceOnly);
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeFalse();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
    }

    [Fact]
    public async Task Legacy_relaxed_speech_does_not_relax_ui_feedback_or_unknown_state_and_clearing_never_replays()
    {
        var fixture = new Fixture(enableInCallFeedback: true);
        await using var admission = fixture.OutputAdmission;
        fixture.CallPreferences.Settings = new(false, true);
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var speaking = fixture.RunAsync("what power action is pending");
        await fixture.TextToSpeech.SpeakStarted.Task;
        var original = fixture.ViewModel.ResponseBody;
        fixture.CallState.SetState(CallState.Suspected);
        await fixture.ViewModel.CallClosureTask;
        fixture.TextToSpeech.SpeakGate.TrySetResult();
        await speaking;
        fixture.ViewModel.EffectiveResponseMode.Should().Be(ResponseOutputMode.VisualOnly);
        fixture.ViewModel.IsSpeaking.Should().BeFalse();
        fixture.ViewModel.ResponseBody.Should().Be(original);
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        await fixture.RunAsync("set calls.feedback-mode to Voice");
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeTrue();
        fixture.CallState.SetState(CallState.Unknown);
        await fixture.ViewModel.CallClosureTask;
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeFalse();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.CallState.SetState(CallState.Clear);
        await fixture.ViewModel.CallClosureTask;
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Voice.StartCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("native")]
    [InlineData("name")]
    [InlineData("call")]
    [InlineData("session")]
    [InlineData("generation")]
    public async Task Native_choice_from_old_lifetime_name_call_or_session_is_not_revived(string stage)
    {
        var fixture = new Fixture(enableInCallFeedback: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.RefreshInCallFeedbackCommand.ExecuteAsync();
        var choice = fixture.ViewModel.InCallFeedbackChoices[0];
        if (stage is "native") { fixture.ViewModel.BindInCallFeedbackNativeLifetime(static () => true); }
        if (stage is "name") { await fixture.RunAsync("set assistant.name to Nova"); }
        if (stage is "call") { fixture.CallState.SetState(CallState.Active); await fixture.ViewModel.CallClosureTask; }
        if (stage is "session") { fixture.ManualCallStore.Authority = fixture.ManualCallStore.Authority! with { SessionId = new(Guid.NewGuid()) }; }
        if (stage is "generation") { fixture.ManualCallStore.Authority = fixture.ManualCallStore.Authority! with { Generation = new(2) }; }
        fixture.ViewModel.SelectedInCallFeedbackChoice = choice;
        await fixture.ViewModel.SaveInCallFeedbackCommand.ExecuteAsync();
        fixture.FeedbackPreferences.Value.Should().BeNull();
        fixture.ViewModel.InCallFeedbackStatus.Should().Contain("\"available\":false");
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("save")]
    [InlineData("write-failure")]
    public async Task Inflight_configuration_keeps_full_original_response_and_cannot_replay(string outcome)
    {
        var fixture = new Fixture(enableInCallFeedback: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var speaking = fixture.RunAsync("what power action is pending");
        await fixture.TextToSpeech.SpeakStarted.Task;
        var original = fixture.ViewModel.ResponseBody;
        if (outcome is "write-failure") { fixture.FeedbackPreferences.WriteFailure = new IOException(); }
        await fixture.ViewModel.ExecuteInCallFeedbackCommandAsync(new(AppearanceCommandOperation.Set, InCallFeedbackMode.Voice),
            SecurityAuditInitiator.TypedCommand, cancellationToken: TestContext.Current.CancellationToken);
        fixture.TextToSpeech.SpeakGate.TrySetResult();
        await speaking;
        fixture.ViewModel.ResponseBody.Should().Be(original);
        fixture.ViewModel.Transcript.Should().Contain(outcome is "save" ? "\"outcome\":\"saved\"" : "not confirmed");
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.Voice.StartCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_exact_question_or_approval_cannot_be_answered_or_hidden_by_feedback(bool question)
    {
        var fixture = new Fixture(enableInCallFeedback: true);
        await using var admission = fixture.OutputAdmission;
        fixture.Probe.Status = new("local.inference", "Local model inference (Ollama)", Kora.Core.Dependencies.DependencyReadiness.Ready, "ready");
        fixture.Reasoner.Action = Kora.Core.Commands.BuiltInAction.LockMachine;
        if (question) { fixture.Reasoner.Question = new("Exact choice?", ["one", "two"]); }
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("please do this work");
        await fixture.ViewModel.ActiveReasoningTask!;
        var preview = fixture.ViewModel.ResponseBody;
        await fixture.RunAsync("set calls.feedback-mode to Voice");
        await fixture.ViewModel.ResetInCallFeedbackCommand.ExecuteAsync();
        fixture.ViewModel.ResponseBody.Should().Be(preview);
        fixture.FeedbackPreferences.Value.Should().BeNull();
        fixture.ViewModel.IsResponseInteractionPending.Should().BeTrue();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Missing_corrupt_cancellation_invalid_hidden_and_disposed_controls_fail_explicitly()
    {
        var missing = new Fixture();
        missing.ViewModel.InCallFeedbackChoices.Should().BeEmpty();
        missing.ViewModel.InCallFeedbackStatus.Should().Contain("unavailable");
        missing.ViewModel.CanInspectInCallFeedback.Should().BeFalse();
        await missing.RunAsync("reset calls.feedback-mode");
        var fixture = new Fixture(enableInCallFeedback: true, feedbackReadFailure: new InvalidDataException("corrupt"));
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();
        fixture.ViewModel.CanChangeInCallFeedbackNative.Should().BeFalse();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        await fixture.RunAsync("get calls.feedback-mode");
        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.FeedbackPreferences.ReadFailure = null;
        await fixture.RunAsync("set calls.feedback-mode to 0");
        fixture.ViewModel.ResponseTitle.Should().Be("Clarify the call feedback setting.");
        await fixture.ViewModel.RefreshInCallFeedbackCommand.ExecuteAsync();
        fixture.ViewModel.BindInCallFeedbackNativeLifetime(static () => false);
        fixture.ViewModel.CanInspectInCallFeedbackNative.Should().BeFalse();
        await fixture.ViewModel.ResetInCallFeedbackCommand.ExecuteAsync();
        fixture.FeedbackPreferences.Value.Should().BeNull();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await fixture.ViewModel.ExecuteInCallFeedbackCommandAsync(new(AppearanceCommandOperation.Reset),
            SecurityAuditInitiator.TypedCommand, cancellationToken: cancelled.Token);
        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.InCallFeedbackStatus.Should().Contain("\"available\":false");
        fixture.ViewModel.Dispose();
        await fixture.ViewModel.ResetInCallFeedbackCommand.ExecuteAsync();
        fixture.ViewModel.CanInspectInCallFeedback.Should().BeFalse();
        fixture.ViewModel.CanInspectInCallFeedbackNative.Should().BeFalse();
        fixture.ViewModel.CanChangeInCallFeedbackNative.Should().BeFalse();
    }

    [Fact]
    public async Task Invalid_name_refuses_prefixed_feedback_but_exact_unprefixed_inspection_remains_available()
    {
        var fixture = new Fixture(enableInCallFeedback: true);
        await using var admission = fixture.OutputAdmission;
        fixture.NamePreferences.Name = "Supported Commands";
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("Kora, get calls.feedback-mode");
        fixture.ViewModel.Transcript.Should().Contain("prefix routing is unavailable");
        await fixture.RunAsync("get calls.feedback-mode");
        fixture.ViewModel.ResponseBody.Should().Contain("\"desired\":\"UI\"");
        fixture.FeedbackPreferences.Value.Should().BeNull();
    }

    [Theory]
    [InlineData("discovery")]
    [InlineData("stop")]
    [InlineData("disposed")]
    [InlineData("presentation")]
    [InlineData("no-subscriber")]
    public async Task Late_feedback_callbacks_revalidate_presentation_and_do_not_replay(string stage)
    {
        var fixture = new Fixture(subscribeToWindowActions: stage is not "no-subscriber", enableInCallFeedback: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        Task? speaking = null;
        if (stage is "stop")
        {
            fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            speaking = fixture.RunAsync("what power action is pending");
            await fixture.TextToSpeech.SpeakStarted.Task;
            fixture.TextToSpeech.BeforeStop = () => fixture.CallState.SetState(CallState.Active);
        }
        if (stage is "discovery")
        {
            fixture.FeedbackConfiguration!.Changed += (_, _) => fixture.ViewModel.BindCallOwnershipGate(static () => false);
        }
        if (stage is "disposed")
        {
            fixture.Audit.BeforeWrite = item =>
            {
                if (item.Outcome == SecurityAuditOutcome.Succeeded && string.Equals(item.ActionId, "configuration.in-call-feedback", StringComparison.Ordinal))
                {
                    fixture.ViewModel.Dispose();
                }
            };
        }
        if (stage is "presentation")
        {
            fixture.ViewModel.PropertyChanged += (_, args) =>
            {
                if (string.Equals(args.PropertyName, nameof(fixture.ViewModel.ResponseBody), StringComparison.Ordinal)) { fixture.Session.IsUnlocked = false; }
            };
        }
        await fixture.ViewModel.ExecuteInCallFeedbackCommandAsync(
            new(stage is "discovery" or "presentation" or "no-subscriber" ? AppearanceCommandOperation.Get : AppearanceCommandOperation.Set,
                InCallFeedbackMode.Voice), SecurityAuditInitiator.TypedCommand, cancellationToken: TestContext.Current.CancellationToken);
        if (speaking is not null)
        {
            fixture.TextToSpeech.SpeakGate!.TrySetResult();
            await speaking;
        }
        if (stage is "discovery" or "stop" or "disposed") { fixture.ViewModel.InCallFeedbackStatus.Should().Contain("\"available\":false"); }
        if (stage is "disposed") { fixture.FeedbackPreferences.Pending.Should().BeTrue(); }
        fixture.Voice.StartCalls.Should().Be(0);
    }
}
