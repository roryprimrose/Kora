using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task Assistant_reset_does_not_change_session_metadata_grants_or_original_question_targets()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "Ready");
        fixture.Reasoner.Question = new("Which target?", ["One", "Two"]);
        await fixture.ViewModel.InitializeAsync();
        var store = BindSessions(fixture);
        await fixture.RunAsync("session create \"Durable label\"");
        var before = store.Session;
        await fixture.RunAsync("choose");
        await fixture.ViewModel.ActiveReasoningTask!;
        var choices = fixture.ViewModel.ModelQuestionChoices.ToArray();
        await fixture.RunAsync("call yourself Nova");
        await fixture.RunAsync("reset your name");
        fixture.ViewModel.IsModelQuestionPending.Should().BeTrue();
        fixture.ViewModel.ModelQuestionChoices.Should().Equal(choices);
        store.Session.Should().Be(before);
        await fixture.ViewModel.CancelModelQuestionAsync();
        await fixture.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add,
            BuiltInAction.EnableHostedModels, ModelApprovalScope.Always));
        await fixture.RunAsync("change your name to Nova");
        fixture.ViewModel.IsGrantChangePending.Should().BeTrue();
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
        await fixture.ViewModel.RejectPendingGrantChangeAsync();
        fixture.Reasoner.Question = null;
        fixture.Reasoner.Action = BuiltInAction.EnableHostedModels;
        await fixture.RunAsync("please lock");
        await fixture.ViewModel.ActiveReasoningTask!;
        await fixture.RunAsync("reset your name");
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        fixture.ViewModel.HostedModelsEnabled.Should().Be(false);
        fixture.Session.LockCalls.Should().Be(0);
        await fixture.ViewModel.RejectModelActionCommand.ExecuteAsync();
        fixture.ViewModel.LocalModelsEnabled = false;
        await fixture.RunAsync("set assistant name to Nova");
        await fixture.RunAsync("Nova, session status " + before.Authority.SessionId.Value.ToString("D"));
        fixture.ViewModel.ResponseBody.Should().Contain("Durable label");
        await fixture.RunAsync("Kora, session help");
        fixture.ViewModel.ResponseTitle.Should().Be("Model use is turned off.");
        fixture.Reasoner.Requests.Clear();
        await fixture.RunAsync("Kora, run lock");
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.ViewModel.LocalModelsEnabled = true;
        await fixture.RunAsync("Nova, run lock");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.Reasoner.Requests.Should().ContainSingle();
        store.Session.Should().Be(before);
    }

    [Fact]
    public async Task Assistant_UI_typed_and_activated_voice_share_discovery_set_get_reset_without_inference()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var notified = new HashSet<string?>(StringComparer.Ordinal);
        fixture.ViewModel.PropertyChanged += (_, args) => notified.Add(args.PropertyName);
        await fixture.RunAsync("Kora, list assistant settings");
        fixture.ViewModel.ResponseBody.Should().Contain("schema 1").And.Contain("assistant.name")
            .And.Contain("default Kora").And.Contain("1-3 words").And.Contain("1-32 UTF-16")
            .And.Contain("unsaved domain default").And.Contain("not authority or production wake");
        await fixture.RunAsync("set assistant.name to Nova");
        fixture.NamePreferences.SavedName.Should().Be("Nova");
        await fixture.RunAsync("Nova, get assistant name");
        fixture.ViewModel.ResponseBody.Should().Contain("= Nova").And.Contain("source: saved device-local");
        await fixture.RaiseActivatedTranscriptAsync("Nova, list assistant settings", 1);
        fixture.ViewModel.ResponseBody.Should().Contain("assistant.name");
        await fixture.RaiseActivatedTranscriptAsync("Nova, get assistant name", 1);
        fixture.ViewModel.ResponseBody.Should().Contain("= Nova");
        await fixture.RaiseActivatedTranscriptAsync("Nova, set assistant name to Atlas", 1);
        fixture.NamePreferences.SavedName.Should().Be("Atlas");
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        await fixture.RaiseActivatedTranscriptAsync("Atlas, reset assistant.name", 1);
        fixture.ViewModel.AssistantName.Should().Be("Kora");
        fixture.ViewModel.AssistantNameInput = "ノヴァ";
        await fixture.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
        fixture.ViewModel.AssistantNameConfigurationDescription.Should().Contain("ノヴァ");
        await fixture.ViewModel.ResetAssistantNameCommand.ExecuteAsync();
        fixture.ViewModel.AssistantName.Should().Be("Kora");
        notified.Should().Contain(nameof(fixture.ViewModel.AssistantNameConfigurationDescription));
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.Audit.Events.Should().Contain(item => item.ActionId == "configuration.assistant-name"
            && item.Initiator == SecurityAuditInitiator.VoiceCommand
            && item.Outcome == SecurityAuditOutcome.Succeeded);
        await fixture.RunAsync("set assistant.unknown to Nova");
        fixture.ViewModel.ResponseTitle.Should().Be("Clarify the assistant setting.");
        await fixture.RunAsync("set assistant name to Nova!");
        fixture.ViewModel.ResponseTitle.Should().Be("The assistant name is invalid.");
        fixture.ViewModel.AssistantName.Should().Be("Kora");
    }

    [Theory]
    [InlineData("set assistant name to Nova")]
    [InlineData("reset assistant.name")]
    [InlineData("call yourself Nova")]
    [InlineData("change your name to Nova")]
    [InlineData("reset your name")]
    public async Task Protected_and_unknown_calls_deny_original_voice_but_allow_new_native_or_typed_name_requests(string command)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.SetAssistantNameAsync("Atlas");
        fixture.CallState.SetState(CallState.Unknown);
        await fixture.Dispatcher.LastInvocation;
        await fixture.RaiseActivatedTranscriptAsync("Atlas, " + command, 1);
        fixture.ViewModel.AssistantName.Should().Be("Atlas");
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
        fixture.Audit.Events.Last().Initiator.Should().Be(SecurityAuditInitiator.VoiceCommand);
        await fixture.RunAsync(command);
        fixture.ViewModel.AssistantName.Should().Be(command.StartsWith("reset", StringComparison.Ordinal) ? "Kora" : "Nova");
    }

    [Theory]
    [InlineData("call yourself ", false)]
    [InlineData("change your name to ", false)]
    [InlineData("call yourself ", true)]
    [InlineData("change your name to ", true)]
    public async Task Natural_names_preserve_case_and_share_native_commit_and_current_prefix_only(
        string prefix, bool voice)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        async Task Run(string text)
        {
            if (voice) { await fixture.RaiseActivatedTranscriptAsync(text, 1); }
            else { await fixture.RunAsync(text); }
        }
        await Run("Kora, what is your name?");
        fixture.ViewModel.ResponseBody.Should().Contain("= Kora").And.Contain("call yourself <name>");
        await Run("Kora, " + prefix + "  ノヴァ\tPrImE  ");
        fixture.NamePreferences.SavedName.Should().Be("ノヴァ PrImE");
        fixture.ViewModel.AssistantName.Should().Be("ノヴァ PrImE");
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.Voice.StopCalls.Should().Be(voice ? 1 : 0);
        fixture.Voice.StartCalls.Should().Be(voice ? 2 : 0);
        fixture.Audit.Events.Where(item => string.Equals(item.ActionId, "configuration.assistant-name", StringComparison.Ordinal))
            .Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        fixture.Audit.Events.Last().Initiator.Should().Be(voice
            ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand);
        await Run("ノヴァ PrImE, what is your name");
        fixture.ViewModel.ResponseBody.Should().Contain("= ノヴァ PrImE");
        await Run("ノヴァ PrImE, " + prefix + "ノヴァ PrImE");
        fixture.Audit.Events.Last().ReasonCode.Should().Be("no-change");
        fixture.ViewModel.LocalModelsEnabled = false;
        var audits = fixture.Audit.Events.Count(item =>
            string.Equals(item.ActionId, "configuration.assistant-name", StringComparison.Ordinal));
        await Run("Kora, reset your name");
        fixture.ViewModel.AssistantName.Should().Be("ノヴァ PrImE");
        fixture.Audit.Events.Count(item =>
            string.Equals(item.ActionId, "configuration.assistant-name", StringComparison.Ordinal)).Should().Be(audits);
        await Run("ノヴァ PrImE, reset your name");
        fixture.ViewModel.AssistantName.Should().Be("Kora");
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        await fixture.RunAsync("Kora, what can you do?");
        fixture.ViewModel.ResponseBody.Should().Contain("call yourself <name>").And.Contain("reset your name");
        fixture.Reasoner.Requests.Should().BeEmpty();
        if (voice) { fixture.Voice.StartedPhrases.Should().Contain("ノヴァ PrImE what is your name"); }
    }

    [Theory]
    [InlineData("call yourself")]
    [InlineData("call yourself \t")]
    [InlineData("change your name Nova")]
    [InlineData("change your name to")]
    [InlineData("reset your name to Nova")]
    [InlineData("what is your name please")]
    public async Task Recognized_malformed_natural_names_do_not_answer_pending_questions_or_reach_inference(string text)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "Ready");
        fixture.Reasoner.Question = new("Which target?", ["One", "Two"]);
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("choose");
        await fixture.ViewModel.ActiveReasoningTask!;
        var requests = fixture.Reasoner.Requests.Count;
        await fixture.RunAsync(text);
        fixture.ViewModel.ResponseTitle.Should().Be("Clarify the assistant setting.");
        fixture.ViewModel.IsModelQuestionPending.Should().BeTrue();
        fixture.Reasoner.Requests.Count.Should().Be(requests);
        fixture.NamePreferences.SavedName.Should().BeNull();
        await fixture.ViewModel.CancelModelQuestionAsync();
        await fixture.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add,
            BuiltInAction.LockMachine, ModelApprovalScope.Always));
        await fixture.RunAsync(text);
        fixture.ViewModel.IsGrantChangePending.Should().BeTrue();
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Nova!")]
    [InlineData("No\u0001va")]
    [InlineData("e\u0301")]
    [InlineData("😀")]
    [InlineData("Supported Commands")]
    [InlineData("one two three four")]
    [InlineData("abcdefghijklmnopqrstuvwxyzABCDEFG")]
    public async Task Natural_invalid_names_use_authoritative_audited_validation_without_repair(string value)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.RunAsync("call yourself " + value);
        fixture.ViewModel.ResponseTitle.Should().Be("The assistant name is invalid.");
        fixture.ViewModel.AssistantName.Should().Be("Kora");
        fixture.NamePreferences.SavedName.Should().BeNull();
        fixture.Audit.Events.Last().ReasonCode.Should().Be("invalid-name");
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.Voice.StartCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Natural_name_failure_retains_old_prefix_without_reopening_capture(bool retirement)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        if (retirement) { fixture.Voice.PreventQuiescence = true; }
        else { fixture.NamePreferences.SaveException = new IOException("name storage unavailable"); }
        await fixture.RunAsync("Kora, change your name to Nova");
        fixture.ViewModel.AssistantName.Should().Be("Kora");
        fixture.NamePreferences.SavedName.Should().BeNull();
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
        fixture.Audit.Events.Last().ReasonCode.Should().Be(retirement ? "capture-stop-failed" : "io-error");
        fixture.Voice.StartCalls.Should().Be(0);
        await fixture.RunAsync("Kora, what is your name");
        fixture.ViewModel.ResponseBody.Should().Contain("= Kora");
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_saved_name_disables_prefix_and_capture_with_visible_unprefixed_recovery()
    {
        var fixture = new Fixture();
        fixture.NamePreferences.Name = "Supported Commands";
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.IsVoiceActivationAvailable.Should().BeFalse();
        fixture.ViewModel.AssistantNameConfigurationDescription.Should().Contain("unavailable");
        fixture.ViewModel.AssistantNameInput = "Nova";
        fixture.ViewModel.AssistantNameSettingStatus.Should().Contain("invalid or unreadable");
        await fixture.RunAsync("Kora, list assistant settings");
        fixture.ViewModel.ResponseTitle.Should().Be("Assistant prefix routing is unavailable.");
        await fixture.RunAsync("what is your name");
        fixture.ViewModel.ResponseBody.Should().Contain("unavailable");
        await fixture.RunAsync("session help");
        fixture.ViewModel.ResponseTitle.Should().Be("Assistant prefix routing is unavailable.");
        await fixture.RunAsync("stop speaking");
        await fixture.RunAsync("cancel task");
        var settingsOpened = false;
        fixture.ViewModel.SettingsRequested += (_, _) => settingsOpened = true;
        await fixture.RunAsync("open settings");
        settingsOpened.Should().BeTrue();
        await fixture.RunAsync("cancel shutdown");
        await fixture.RunAsync("open documentation");
        await fixture.RunAsync("reset your name");
        fixture.ViewModel.AssistantName.Should().Be("Kora");
        fixture.ViewModel.IsVoiceActivationAvailable.Should().BeTrue();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.NamePreferences.SavedName.Should().Be("Kora");
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Assistant_change_or_host_generation_loss_during_speech_stop_discards_already_admitted_input(int transition)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        await fixture.ViewModel.EndPushToTalkAsync();
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var preview = fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        await fixture.TextToSpeech.SpeakStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.TextToSpeech.StopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var transcript = fixture.Voice.RaiseTranscriptAsync("lock the machine", 1);
        await fixture.TextToSpeech.StopStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await preview;
        switch (transition)
        {
            case 0: await fixture.ViewModel.SetAssistantNameAsync("Nova"); break;
            case 1: fixture.Voice.AdvanceGeneration(); break;
            case 2: fixture.Session.IsUnlocked = false; break;
        }
        fixture.TextToSpeech.StopGate.SetResult();
        await transcript;
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Assistant_invalid_saved_name_still_allows_exact_exit_recovery()
    {
        var fixture = new Fixture();
        fixture.NamePreferences.Name = "Supported Commands";
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("exit the application");
        fixture.WindowActions.Should().Contain(Kora.Application.ViewModels.WindowAction.Close);
    }

    [Fact]
    public async Task Queued_capture_completion_and_transcript_are_retired_before_a_new_name_is_published()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        var old = fixture.Voice.Generation;
        fixture.Dispatcher.BeforeInvoke = () =>
        {
            fixture.ViewModel.AssistantNameInput = "Nova";
            fixture.ViewModel.ApplyAssistantNameCommand.ExecuteAsync().IsCompletedSuccessfully.Should().BeTrue();
        };
        await fixture.Voice.RaiseTranscriptAsync("Kora, lock the machine", 1);
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.AssistantName.Should().Be("Nova");
        var response = fixture.ViewModel.ResponseBody;
        fixture.Voice.PublishCompletion(new(old, VoiceRecognitionCompletionReason.EmptySpeechTimeout));
        fixture.Voice.PublishCaptureState(new(old, false));
        fixture.ViewModel.ResponseBody.Should().Be(response);
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.Voice.StartCalls.Should().Be(2);
        fixture.Voice.StartedPhrases.Should().Contain("Nova get assistant name")
            .And.Contain("Nova session help").And.NotContain("Kora session help");
    }

    [Fact]
    public async Task Failed_new_grammar_does_not_restore_old_prefix_or_reopen_input()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.SetAssistantNameAsync("Nova");
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.Voice.StartException = new InvalidOperationException("grammar unavailable");
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.ViewModel.AssistantName.Should().Be("Nova");
        fixture.NamePreferences.SavedName.Should().Be("Nova");
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.ResponseBody.Should().Contain("grammar unavailable");
        await fixture.RunAsync("Nova, get assistant.name");
        fixture.ViewModel.ResponseBody.Should().Contain("= Nova");
    }

    [Fact]
    public async Task Unconfirmed_capture_quiescence_prevents_persistence_and_disposed_notifications_do_not_reactivate()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Voice.PreventQuiescence = true;
        await fixture.ViewModel.SetAssistantNameAsync("Nova");
        fixture.NamePreferences.SavedName.Should().BeNull();
        fixture.ViewModel.ResponseBody.Should().Contain("shutdown is not confirmed");
        fixture.ViewModel.Dispose();
        await fixture.ViewModel.SetAssistantNameAsync("Nova");
        fixture.NamePreferences.SavedName.Should().BeNull();
    }
}
