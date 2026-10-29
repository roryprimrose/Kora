using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("use visual responses", ResponseOutputMode.VisualOnly)]
    [InlineData("use spoken responses", ResponseOutputMode.VoiceOnly)]
    [InlineData("use spoken and visual responses", ResponseOutputMode.Hybrid)]
    public async Task Natural_native_and_enum_modes_share_exact_audited_save_without_other_audio_changes(string phrase, ResponseOutputMode mode)
    {
        foreach (var route in new[] { "alias", "enum", "native", "activated" })
        {
            var fixture = new Fixture(enableResponseModeConfiguration: true, enablePlaybackVolume: true, enableWindowsSpeechRate: true);
            await using var admission = fixture.OutputAdmission;
            fixture.NamePreferences.Name = "Nova";
            await fixture.ViewModel.InitializeAsync();
            fixture.TextToSpeech.ClearSpokenResponse();
            var starts = fixture.Voice.StartCalls;
            var stops = fixture.Voice.StopCalls;
            var volume = fixture.TextToSpeech.Volume;
            var rate = fixture.TextToSpeech.Rate;
            if (route is "native")
            {
                await fixture.ViewModel.RefreshResponseModeCommand.ExecuteAsync();
                fixture.ViewModel.SelectedResponseModeChoice = fixture.ViewModel.ResponseModeChoices.Single(choice => choice.Mode == mode);
                await fixture.ViewModel.SaveResponseModeCommand.ExecuteAsync();
            }
            else if (route is "activated") { await fixture.RaiseActivatedTranscriptAsync("Nova, " + phrase, 1); }
            else { await fixture.RunAsync(route is "enum" ? "set responses.default-mode to " + mode : "Nova, " + phrase); }

            fixture.OutputPreferences.SavedMode.Should().Be(mode, route);
            fixture.ViewModel.DefaultResponseMode.Should().Be(mode);
            fixture.ViewModel.ResponseBody.Should().Contain("\"outcome\":\"saved\"")
                .And.Contain("\"desired\":\"" + mode + "\"").And.Contain("\"effective\":\"" + mode + "\"")
                .And.Contain("next eligible response only");
            fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
            var writes = fixture.Audit.Events.Where(item => string.Equals(item.ActionId, "configuration.response-output", StringComparison.Ordinal)).ToArray();
            writes.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
            writes[0].CorrelationId.Should().Be(writes[1].CorrelationId);
            var initiator = route is "activated" ? SecurityAuditInitiator.VoiceCommand
                : route is "native" ? SecurityAuditInitiator.LocalUser : SecurityAuditInitiator.TypedCommand;
            writes.Should().OnlyContain(item => item.Initiator == initiator);
            if (route is not "activated")
            {
                fixture.Voice.StartCalls.Should().Be(starts);
                fixture.Voice.StopCalls.Should().Be(stops);
            }
            fixture.TextToSpeech.SpokenText.Should().BeNull();
            fixture.TextToSpeech.Volume.Should().Be(volume);
            fixture.TextToSpeech.Rate.Should().Be(rate);
            fixture.VolumePreferences.Value.Should().BeNull();
            fixture.RatePreferences.Value.Should().BeNull();
            fixture.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
            fixture.OutputPreferences.SavedMutedOutputVisualFallback.Should().BeNull();
            fixture.Preferences.SavedVoiceId.Should().BeNull();
            fixture.Reasoner.Requests.Should().BeEmpty();

            await fixture.RunAsync("Nova, check response mode");
            fixture.ViewModel.ResponseBody.Should().Contain("\"outcome\":\"observed\"").And.Contain("\"saved\":\"" + mode + "\"");
            fixture.Audit.Events.Count(item => string.Equals(item.ActionId, "configuration.response-output", StringComparison.Ordinal)).Should().Be(2);
            if (route is "activated")
            {
                fixture.Voice.StartedPhrases.Should().Contain(phrase).And.Contain("check response mode");
            }
        }
    }

    [Theory]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Suspected)]
    [InlineData(CallState.Unknown)]
    public async Task Spoken_alias_retains_original_voice_refusal_and_required_visual_under_call_policy(CallState state)
    {
        var fixture = new Fixture(enableResponseModeConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        fixture.Voice.Microphones = [new MicrophoneDevice("voice-fixture", "Synthetic microphone")];
        fixture.Voice.DefaultMicrophoneId = "voice-fixture";
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.CallState.SetState(state);
        await fixture.Dispatcher.LastInvocation;
        var starts = fixture.Voice.StartCalls;

        await fixture.ViewModel.ExecuteResponseModeCommandAsync(ResponseModeCommand.Parse("use spoken responses", "Kora")!,
            SecurityAuditInitiator.VoiceCommand, cancellationToken: TestContext.Current.CancellationToken);

        fixture.OutputPreferences.SavedMode.Should().BeNull();
        fixture.ViewModel.ResponseBody.Should().Contain("\"outcome\":\"denied\"").And.Contain("\"desired\":\"Hybrid\"");
        fixture.Audit.Events.Should().Contain(item => string.Equals(item.ActionId, "configuration.response-output", StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Denied && item.Initiator == SecurityAuditInitiator.VoiceCommand);
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeFalse();
        fixture.Voice.StartCalls.Should().Be(starts);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Stale_native_choice_refuses_then_fresh_alias_uses_the_current_revision()
    {
        var fixture = new Fixture(enableResponseModeConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.RefreshResponseModeCommand.ExecuteAsync();
        var stale = fixture.ViewModel.ResponseModeChoices.Single(choice => choice.Mode == ResponseOutputMode.VoiceOnly);
        await fixture.ViewModel.RefreshResponseModeCommand.ExecuteAsync();
        fixture.ViewModel.SelectedResponseModeChoice = stale;
        await fixture.ViewModel.SaveResponseModeCommand.ExecuteAsync();
        fixture.OutputPreferences.SavedMode.Should().BeNull();
        fixture.ViewModel.ResponseBody.Should().Contain("Inspect and choose again");
        fixture.ViewModel.ResponseModeConfigurationStatus.Should().StartWith("Setting unavailable.");

        await fixture.RunAsync("use spoken responses");
        fixture.OutputPreferences.SavedMode.Should().Be(ResponseOutputMode.VoiceOnly);
        fixture.ViewModel.ResponseBody.Should().Contain("\"outcome\":\"saved\"");
    }

    [Fact]
    public async Task Spoken_alias_preserves_zero_volume_and_narrower_modes_without_synthesis_or_capture()
    {
        var fixture = new Fixture(enableResponseModeConfiguration: true, enablePlaybackVolume: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        var starts = fixture.Voice.StartCalls;
        await fixture.RunAsync("set speech.playback-volume to 0");
        await fixture.RunAsync("use spoken responses");
        fixture.ViewModel.ResponseBody.Should().Contain("\"desired\":\"VoiceOnly\"").And.Contain("\"effective\":\"VoiceOnly\"")
            .And.Contain("\"speechEligible\":false").And.Contain("\"mandatoryVisual\":true");
        fixture.ViewModel.QueueResponseMode = ResponseOutputMode.VisualOnly;
        await fixture.RunAsync("use spoken responses");
        fixture.ViewModel.ResponseBody.Should().Contain("\"desired\":\"VoiceOnly\"").And.Contain("\"effective\":\"VisualOnly\"");
        fixture.ViewModel.TaskResponseMode = ResponseOutputMode.Hybrid;
        await fixture.RunAsync("use spoken responses");
        fixture.ViewModel.ResponseBody.Should().Contain("\"desired\":\"VoiceOnly\"").And.Contain("\"effective\":\"Hybrid\"");
        fixture.VolumePreferences.Value.Should().Be(new Kora.Core.Configuration.PlaybackVolume(0));
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Voice.StartCalls.Should().Be(starts);
    }

    [Theory]
    [InlineData("use visual responses")]
    [InlineData("use spoken responses")]
    [InlineData("use spoken and visual responses")]
    public async Task Natural_mode_during_playback_preserves_the_complete_response_without_replay(string phrase)
    {
        var fixture = new Fixture(enableResponseModeConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var speaking = fixture.RunAsync("what power action is pending");
        await fixture.TextToSpeech.SpeakStarted.Task;
        var body = fixture.ViewModel.ResponseBody;
        var spoken = fixture.TextToSpeech.SpokenText;
        var starts = fixture.Voice.StartCalls;
        await fixture.ViewModel.ExecuteResponseModeCommandAsync(ResponseModeCommand.Parse(phrase, "Kora")!,
            SecurityAuditInitiator.TypedCommand, cancellationToken: TestContext.Current.CancellationToken);
        fixture.TextToSpeech.SpeakGate.TrySetResult();
        await speaking;
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.Transcript.Should().Contain("\"outcome\":\"saved\"");
        fixture.TextToSpeech.SpokenText.Should().Be(spoken);
        fixture.Voice.StartCalls.Should().Be(starts);
    }

    [Theory]
    [InlineData("use spoken responses please")]
    [InlineData("use visual response")]
    [InlineData("check response mode please")]
    public async Task Malformed_reserved_alias_is_local_clarification_without_pending_work(string text)
    {
        var fixture = new Fixture(enableResponseModeConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync(text);
        fixture.ViewModel.ResponseTitle.Should().Be("Clarify the response setting.");
        fixture.OutputPreferences.SavedMode.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("held")]
    [InlineData("save")]
    [InlineData("cancel")]
    public async Task Natural_mode_does_not_convert_corrupt_held_failed_or_cancelled_state_into_success(string stage)
    {
        var fixture = new Fixture(enableResponseModeConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        if (stage is "corrupt") { fixture.OutputPreferences.Mode = (ResponseOutputMode)100; }
        if (stage is "held") { fixture.OutputPreferences.BeginDefaultModeWrite(); }
        fixture.TextToSpeech.ClearSpokenResponse();
        if (stage is "save") { fixture.OutputPreferences.SaveException = new IOException("atomic failure"); }
        using var cancellation = new CancellationTokenSource();
        if (stage is "cancel") { await cancellation.CancelAsync(); }
        var starts = fixture.Voice.StartCalls;

        await fixture.ViewModel.ExecuteResponseModeCommandAsync(ResponseModeCommand.Parse("use spoken responses", "Kora")!,
            SecurityAuditInitiator.TypedCommand, cancellationToken: cancellation.Token);

        fixture.OutputPreferences.SavedMode.Should().BeNull();
        fixture.ViewModel.ResponseModeConfigurationStatus.Should().StartWith("Setting unavailable.");
        fixture.ViewModel.ResponseTitle.Should().Be("Response preference not confirmed.");
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Voice.StartCalls.Should().Be(starts);
        fixture.Reasoner.Requests.Should().BeEmpty();
        if (stage is "save")
        {
            fixture.OutputPreferences.SaveException = null;
            await fixture.RunAsync("use spoken and visual responses");
            fixture.OutputPreferences.SavedMode.Should().BeNull();
            fixture.ViewModel.ResponseModeConfigurationStatus.Should().StartWith("Setting unavailable.");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Malformed_reserved_aliases_cannot_answer_pending_decisions_or_reach_the_model(bool question)
    {
        var fixture = new Fixture(enableResponseModeConfiguration: true);
        await using var admission = fixture.OutputAdmission;
        fixture.Probe.Status = new("local.inference", "Local model inference (Ollama)", Kora.Core.Dependencies.DependencyReadiness.Ready, "ready");
        fixture.Reasoner.Action = Kora.Core.Commands.BuiltInAction.EnableHostedModels;
        if (question) { fixture.Reasoner.Question = new("Exact choice?", ["one", "two"]); }
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("please do this work");
        await fixture.ViewModel.ActiveReasoningTask!;
        var preview = fixture.ViewModel.ResponseBody;
        var requests = fixture.Reasoner.Requests.Count;
        foreach (var text in new[] { "use spoken responses", "use spoken responses and approve once", "use visual response",
            "use  spoken responses", "use\tvisual responses", "check response mode please", "check  response mode", "check response\tmode" })
        {
            await fixture.RunAsync("Kora, " + text);
            fixture.ViewModel.ResponseBody.Should().Be(preview);
            fixture.ViewModel.IsResponseInteractionPending.Should().BeTrue();
            fixture.Reasoner.Requests.Should().HaveCount(requests);
            fixture.ViewModel.HostedModelsEnabled.Should().Be(false);
        fixture.Session.LockCalls.Should().Be(0);
            fixture.OutputPreferences.SavedMode.Should().BeNull();
        }
    }
}
