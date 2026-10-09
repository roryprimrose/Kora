using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_voice_response_setting_without_ambient_context_preserves_voice_origin_and_call_refusal(bool protectedCall)
    {
        var fixture = new Fixture(enableResponseModeConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        fixture.Voice.Microphones = [new MicrophoneDevice("voice-fixture", "Synthetic microphone")];
        fixture.Voice.DefaultMicrophoneId = "voice-fixture";
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        if (protectedCall)
        {
            fixture.CallState.SetState(CallState.Unknown);
            await fixture.Dispatcher.LastInvocation;
        }
        HostActivity.Current.Should().BeNull();

        await fixture.ViewModel.ExecuteResponseModeCommandAsync(new(AppearanceCommandOperation.Reset),
            SecurityAuditInitiator.VoiceCommand, cancellationToken: TestContext.Current.CancellationToken);

        if (protectedCall)
        {
            fixture.OutputPreferences.SavedMode.Should().BeNull();
            fixture.ViewModel.ResponseBody.Should().Contain("\"outcome\":\"denied\"");
            fixture.Audit.Events.Should().Contain(item => item.Initiator == SecurityAuditInitiator.VoiceCommand
                && item.Outcome == SecurityAuditOutcome.Denied);
        }
        else
        {
            fixture.OutputPreferences.SavedMode.Should().Be(ResponseOutputMode.Hybrid);
            fixture.ViewModel.ResponseBody.Should().Contain("\"outcome\":\"saved\"");
        }
        fixture.ManualCallStore.Tasks.Should().NotBeEmpty().And.OnlyContain(
            record => record.Request.Origin == RequestOrigin.ActivatedVoice);
        fixture.Voice.StartCalls.Should().Be(0);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
        HostActivity.Current.Should().BeNull();
    }

    [Fact]
    public async Task Device_response_native_exact_and_activated_routes_share_save_readback_without_autoplay_or_microphone_changes()
    {
        var fixture = new Fixture(enableResponseModeConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        var starts = fixture.Voice.StartCalls;
        await fixture.RunAsync("Kora, list response settings");
        fixture.ViewModel.ResponseBody.Should().Contain("\"source\":\"default\"").And.Contain("\"saved\":null");
        await fixture.RunAsync("set responses.default-mode to VisualOnly");
        fixture.ViewModel.DefaultResponseMode.Should().Be(ResponseOutputMode.VisualOnly);
        fixture.OutputPreferences.SavedMode.Should().Be(ResponseOutputMode.VisualOnly);
        await fixture.RunAsync("status responses.default-mode");
        fixture.ViewModel.ResponseBody.Should().Contain("\"source\":\"saved\"").And.Contain("\"effective\":\"VisualOnly\"");
        await fixture.ViewModel.RefreshResponseModeCommand.ExecuteAsync();
        fixture.ViewModel.SelectedResponseModeChoice = fixture.ViewModel.ResponseModeChoices.Single(choice => choice.Mode == ResponseOutputMode.VoiceOnly);
        await fixture.ViewModel.SaveResponseModeCommand.ExecuteAsync();
        fixture.ViewModel.DefaultResponseMode.Should().Be(ResponseOutputMode.VoiceOnly);
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        await fixture.ViewModel.ResetResponseModeCommand.ExecuteAsync();
        fixture.ViewModel.DefaultResponseMode.Should().Be(ResponseOutputMode.Hybrid);
        fixture.Voice.StartCalls.Should().Be(starts);
        await fixture.RaiseActivatedTranscriptAsync("Kora, set responses.default-mode to VisualOnly", 1);
        fixture.OutputPreferences.SavedMode.Should().Be(ResponseOutputMode.VisualOnly);
        await fixture.RaiseActivatedTranscriptAsync("Kora, reset responses.default-mode", 1);
        fixture.OutputPreferences.SavedMode.Should().Be(ResponseOutputMode.Hybrid);
        fixture.Voice.StartCalls.Should().Be(starts + 2);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
        fixture.OutputPreferences.SavedMutedOutputVisualFallback.Should().BeNull();
        fixture.Preferences.SavedVoiceId.Should().BeNull();
        fixture.Voice.StartedPhrases.Should().Contain("set responses.default-mode to VisualOnly");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_response_configuration_never_answers_or_hides_pending_question_or_approval(bool question)
    {
        var fixture = new Fixture(enableResponseModeConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        fixture.Probe.Status = new("local.inference", "Local model inference (Ollama)", Kora.Core.Dependencies.DependencyReadiness.Ready, "ready");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        if (question) { fixture.Reasoner.Question = new("Exact choice?", ["one", "two"]); }
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("please do this work");
        await fixture.ViewModel.ActiveReasoningTask!;
        var preview = fixture.ViewModel.ResponseBody;
        fixture.ViewModel.CanChangeResponseMode.Should().BeFalse();
        await fixture.RunAsync("set responses.default-mode to VoiceOnly");
        await fixture.ViewModel.ResetResponseModeCommand.ExecuteAsync();
        fixture.ViewModel.ResponseBody.Should().Be(preview);
        fixture.OutputPreferences.SavedMode.Should().BeNull();
        fixture.ViewModel.IsResponseInteractionPending.Should().BeTrue();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Original_voice_call_refusal_and_narrower_output_precedence_are_preserved()
    {
        var fixture = new Fixture(enableResponseModeConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.QueueResponseMode = ResponseOutputMode.VisualOnly;
        fixture.ViewModel.TaskResponseMode = ResponseOutputMode.Hybrid;
        await fixture.RunAsync("set responses.default-mode to VoiceOnly");
        fixture.ViewModel.ResponseBody.Should().Contain("\"desired\":\"VoiceOnly\"").And.Contain("\"effective\":\"Hybrid\"");
        fixture.ViewModel.QueueResponseMode.Should().Be(ResponseOutputMode.VisualOnly);
        fixture.ViewModel.TaskResponseMode.Should().Be(ResponseOutputMode.Hybrid);
        await fixture.RaiseActivatedTranscriptAsync("get responses.default-mode", 1);
        fixture.CallState.SetState(CallState.Unknown);
        await fixture.Dispatcher.LastInvocation;
        using (var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice), HostActivityLayer.Application, HostOperation.Request))
        {
            await fixture.ViewModel.ExecuteResponseModeCommandAsync(new(AppearanceCommandOperation.Reset), SecurityAuditInitiator.LocalUser,
                cancellationToken: TestContext.Current.CancellationToken);
        }
        fixture.OutputPreferences.SavedMode.Should().Be(ResponseOutputMode.VoiceOnly);
        fixture.ViewModel.ResponseBody.Should().Contain("\"outcome\":\"denied\"");
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeFalse();
        await fixture.RunAsync("reset responses.default-mode");
        fixture.OutputPreferences.SavedMode.Should().Be(ResponseOutputMode.Hybrid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Device_mode_change_retires_output_and_preserves_complete_visual_response_even_on_save_failure(bool fail)
    {
        var fixture = new Fixture(enableResponseModeConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.RefreshResponseModeCommand.ExecuteAsync();
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var speaking = fixture.RunAsync("what power action is pending");
        await fixture.TextToSpeech.SpeakStarted.Task;
        var body = fixture.ViewModel.ResponseBody;
        if (fail) { fixture.OutputPreferences.SaveException = new IOException("owned atomic failure"); }
        fixture.ViewModel.SelectedResponseModeChoice = fixture.ViewModel.ResponseModeChoices.Single(choice => choice.Mode == ResponseOutputMode.VoiceOnly);
        await fixture.ViewModel.SaveResponseModeCommand.ExecuteAsync();
        fixture.TextToSpeech.SpeakGate.TrySetResult();
        await speaking;
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.OutputPreferences.SavedMode.Should().Be(fail ? null : ResponseOutputMode.VoiceOnly);
        if (fail) { fixture.ViewModel.Transcript.Should().Contain("not confirmed"); }
    }

    [Fact]
    public async Task Missing_disposed_invalid_and_cancelled_response_controls_fail_explicitly_without_inference()
    {
        var missing = new Fixture();
        missing.ViewModel.ResponseModeChoices.Should().BeEmpty();
        missing.ViewModel.CanChangeResponseMode.Should().BeFalse();
        missing.ViewModel.ResponseModeConfigurationStatus.Should().Contain("unavailable");
        await missing.RunAsync("reset responses.default-mode");
        missing.OutputPreferences.SavedMode.Should().BeNull();
        var fixture = new Fixture(enableResponseModeConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("set responses.default-mode to 0");
        fixture.ViewModel.ResponseTitle.Should().Be("Clarify the response setting.");
        fixture.Reasoner.Requests.Should().BeEmpty();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await fixture.ViewModel.ExecuteResponseModeCommandAsync(new(AppearanceCommandOperation.Reset),
            SecurityAuditInitiator.TypedCommand, cancellationToken: cancelled.Token);
        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ResponseModeConfigurationStatus.Should().Contain("\"available\":false");
        fixture.ViewModel.Dispose();
        await fixture.ViewModel.ResetResponseModeCommand.ExecuteAsync();
        fixture.OutputPreferences.SavedMode.Should().BeNull();
        fixture.ViewModel.CanChangeResponseMode.Should().BeFalse();
    }

    [Theory]
    [InlineData("receipt")]
    [InlineData("disposed")]
    [InlineData("presentation")]
    [InlineData("no-subscriber")]
    public async Task Late_ownership_disposal_and_presentation_privacy_are_rechecked_without_replay(string stage)
    {
        var fixture = new Fixture(subscribeToWindowActions: stage is not "no-subscriber", enableResponseModeConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(fixture.ViewModel.ResponseModeChoices), StringComparison.Ordinal))
            {
                if (stage is "receipt") { fixture.Session.IsUnlocked = false; }
                if (stage is "disposed") { fixture.ViewModel.Dispose(); }
            }
            if (stage is "presentation" && string.Equals(args.PropertyName, nameof(fixture.ViewModel.ResponseBody), StringComparison.Ordinal))
            {
                fixture.Session.IsUnlocked = false;
            }
        };
        await fixture.RunAsync("get responses.default-mode");
        fixture.OutputPreferences.SavedMode.Should().BeNull();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        if (stage is "receipt") { fixture.ViewModel.ResponseModeConfigurationStatus.Should().Contain("\"available\":false"); }
        if (stage is "disposed") { fixture.ViewModel.CanChangeResponseMode.Should().BeFalse(); }
    }

    [Fact]
    public async Task Response_mode_preserves_merged_zero_volume_and_unity_reset_without_autoplay_or_microphone_changes()
    {
        var fixture = new Fixture(enablePlaybackVolume: true, enableResponseModeConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        var starts = fixture.Voice.StartCalls;
        await fixture.RunAsync("set speech.playback-volume to 0");
        await fixture.RunAsync("set responses.default-mode to VoiceOnly");
        fixture.VolumePreferences.Value.Should().Be(new Kora.Core.Configuration.PlaybackVolume(0));
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.ResponseBody.Should().Contain("\"speechEligible\":false").And.Contain("\"mandatoryVisual\":true");
        await fixture.ViewModel.ResetResponseModeCommand.ExecuteAsync();
        fixture.VolumePreferences.Value.Should().Be(new Kora.Core.Configuration.PlaybackVolume(0));
        fixture.OutputPreferences.SavedMode.Should().Be(ResponseOutputMode.Hybrid);
        await fixture.RunAsync("reset speech.playback-volume");
        fixture.VolumePreferences.Value.Should().BeNull();
        fixture.ViewModel.SelectedPlaybackVolume.Should().Be(100);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Voice.StartCalls.Should().Be(starts);
    }
}
