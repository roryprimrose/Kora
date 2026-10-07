using AwesomeAssertions;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task Native_UI_and_exact_typed_activated_commands_share_discovery_revision_reset_and_policy()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.SummarySentenceChoices.Should().Equal(1, 2, 3);
        fixture.ViewModel.SummaryWordChoices.Should().HaveCount(80);
        var notified = new HashSet<string?>(StringComparer.Ordinal);
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            notified.Add(args.PropertyName);
            if (string.Equals(args.PropertyName, nameof(fixture.ViewModel.SelectedSummaryWords), StringComparison.Ordinal))
            {
                fixture.ViewModel.SelectedSummaryWords = fixture.ViewModel.SelectedSummaryWords;
            }
            if (string.Equals(args.PropertyName, nameof(fixture.ViewModel.SelectedSummarySentences), StringComparison.Ordinal))
            {
                fixture.ViewModel.SelectedSummarySentences = fixture.ViewModel.SelectedSummarySentences;
            }
        };
        await fixture.RunAsync("list speech settings");
        fixture.ViewModel.ResponseBody.Should().Contain("speech.summary-sentences").And.Contain("speech.summary-words")
            .And.Contain("positive-integer").And.Contain("unsaved limit defaults");
        fixture.ViewModel.SelectedSummaryWords = 40;
        fixture.ViewModel.SelectedSummarySentences = 1;
        fixture.Preferences.SummaryLimits.Should().Be(new SpokenSummaryLimits(1, 40));
        await fixture.RunAsync("get speech.summary-words");
        fixture.ViewModel.ResponseBody.Should().Contain("saved limits").And.Contain("revision");
        await fixture.ViewModel.ResetSummarySentencesCommand.ExecuteAsync();
        fixture.Preferences.SummaryLimits.Should().Be(new SpokenSummaryLimits(3, 40));
        await fixture.RaiseActivatedTranscriptAsync("Kora, set speech summary sentences to 2", 1);
        fixture.Preferences.SummaryLimits.Should().Be(new SpokenSummaryLimits(2, 40));
        fixture.CallState.SetState(CallState.Active);
        await fixture.Dispatcher.LastInvocation;
        await fixture.RaiseActivatedTranscriptAsync("Kora, reset speech.summary-words", 1);
        fixture.Preferences.SummaryLimits.Should().Be(new SpokenSummaryLimits(2, 40));
        fixture.Audit.Events.Should().Contain(item => item.ActionId == "configuration.speech-summary-words"
            && item.Initiator == SecurityAuditInitiator.VoiceCommand && item.Outcome == SecurityAuditOutcome.Denied);
        await fixture.RunAsync("reset speech.summary-words");
        fixture.Preferences.SummaryLimits.Should().Be(new SpokenSummaryLimits(2, 80));
        await fixture.ViewModel.ResetSummaryWordsCommand.ExecuteAsync();
        fixture.ViewModel.SelectedSummaryWords = null;
        fixture.ViewModel.SelectedSummarySentences = null;
        await fixture.RunAsync("set speech.summary-words to forty");
        fixture.ViewModel.ResponseTitle.Should().Contain("Choose");
        fixture.Reasoner.Requests.Should().BeEmpty();
        notified.Should().Contain(nameof(fixture.ViewModel.SelectedSummaryWords)).And.Contain(nameof(fixture.ViewModel.SelectedSummarySentences));
        fixture.ViewModel.SelectedOutputDevice.Should().Be(SystemAudioDevices.Output);
        fixture.Preferences.SavedProviderId.Should().BeNull();
        fixture.ViewModel.SelectedSummarySentences.Should().Be(2);
    }

    [Theory]
    [InlineData(SpeechProviderIds.Windows, 72, false, true)]
    [InlineData(SpeechProviderIds.Kokoro, 72, false, true)]
    [InlineData(SpeechProviderIds.Windows, 73, false, false)]
    [InlineData(SpeechProviderIds.Kokoro, 73, false, false)]
    [InlineData(SpeechProviderIds.Windows, 1, true, false)]
    [InlineData(SpeechProviderIds.Kokoro, 1, true, false)]
    public async Task Normal_local_results_are_measured_at_exact_both_caps_without_changing_visual_or_warning(
        string provider, int words, bool extraSentence, bool allowed)
    {
        var fixture = CreateSummaryFixture(provider);
        var answer = string.Join(' ', Enumerable.Repeat("word", words)) + "." + (extraSentence ? " Another." : "");
        fixture.Reasoner.Response = new(answer, null);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        await fixture.RunAsync("explain this");
        await fixture.ViewModel.ActiveReasoningTask!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.ViewModel.ResponseTitle.Should().Be("Local model response");
        fixture.ViewModel.ResponseBody.Should().Be($"Generated locally; verify important details.\n\n{answer}");
        var full = $"{fixture.ViewModel.ResponseTitle}. {fixture.ViewModel.ResponseBody}";
        if (allowed)
        {
            SpokenSummaryMeasure.Count(full).Should().Be(new SpokenSummaryMeasure(3, 80));
            fixture.TextToSpeech.SpokenText.Should().Be(full);
            fixture.TextToSpeech.SpokenVoice!.ProviderId.Should().Be(provider);
            fixture.ViewModel.IsVisualResponseVisible.Should().BeFalse();
        }
        else
        {
            fixture.TextToSpeech.SpokenText.Should().BeNull();
            fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
            fixture.ViewModel.ResponseOutputStatus.Should().Contain("Speech withheld").And.Contain("No text was shortened");
        }
        fixture.Reasoner.Requests.Should().ContainSingle();
        fixture.TextToSpeech.InstallProviderCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(SpeechProviderIds.Windows)]
    [InlineData(SpeechProviderIds.Kokoro)]
    public async Task Native_result_respects_lowered_either_cap_and_does_not_replay_after_reset(string provider)
    {
        var fixture = CreateSummaryFixture(provider);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        await fixture.RunAsync("what power action is pending");
        var full = $"{fixture.ViewModel.ResponseTitle}. {fixture.ViewModel.ResponseBody}";
        fixture.TextToSpeech.SpokenText.Should().Be(full);
        var measured = SpokenSummaryMeasure.Count(full);
        measured.Sentences.Should().Be(2);
        fixture.ViewModel.SelectedSummaryWords = measured.Words;
        fixture.ViewModel.SelectedSummarySentences = 2;
        await fixture.RunAsync("what power action is pending");
        fixture.TextToSpeech.SpokenText.Should().Be(full);
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.ViewModel.SelectedSummaryWords = measured.Words - 1;
        await fixture.RunAsync("what power action is pending");
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.ResponseBody.Should().Contain("intentionally disabled");
        await fixture.ViewModel.ResetSummaryWordsCommand.ExecuteAsync();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.SelectedSummarySentences = 1;
        await fixture.RunAsync("what power action is pending");
        fixture.ViewModel.ResponseOutputStatus.Should().Contain("2 sentences");
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mandatory_question_or_approval_readback_is_exact_despite_one_word_one_sentence_caps(bool question)
    {
        var fixture = CreateSummaryFixture(SpeechProviderIds.Windows);
        fixture.Preferences.SummaryLimits = new(1, 1);
        if (question) { fixture.Reasoner.Question = new("Which color?", ["Blue", "Green"]); }
        else { fixture.Reasoner.Action = Kora.Core.Commands.BuiltInAction.LockMachine; }
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("choose");
        await fixture.ViewModel.ActiveReasoningTask!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.TextToSpeech.SpokenText.Should().Contain(question ? "Choose an option by number" : "grant permission once");
        fixture.TextToSpeech.SpokenText.Should().Contain("Begin your answer by addressing me by name");
        fixture.ViewModel.IsResponseInteractionPending.Should().BeTrue();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.ResponseOutputStatus.Should().NotContain("Speech withheld");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changing_caps_or_disposal_invalidates_pending_output_before_dispatch_and_never_replays(bool dispose)
    {
        var fixture = CreateSummaryFixture(SpeechProviderIds.Windows);
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = fixture.RunAsync("what power action is pending");
        await fixture.TextToSpeech.SpeakStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.Events.Clear();
        if (dispose) { fixture.ViewModel.Dispose(); }
        else { fixture.ViewModel.SelectedSummaryWords = 1; }
        fixture.Events.Should().Contain("speech.invalidate");
        fixture.TextToSpeech.SpeakGate.SetResult();
        await response.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.ViewModel.IsSpeaking.Should().BeFalse();
        fixture.TextToSpeech.IsSpeaking.Should().BeFalse();
        var spoken = fixture.TextToSpeech.SpokenText;
        if (!dispose) { await fixture.ViewModel.ResetSummaryWordsCommand.ExecuteAsync(); }
        fixture.TextToSpeech.SpokenText.Should().Be(spoken);
    }

    private static Fixture CreateSummaryFixture(string provider)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Verified fake.");
        fixture.TextToSpeech.Providers = [CreateWindowsProvider(), CreateKokoroProvider(true)];
        fixture.TextToSpeech.Voices = [new("female", "Windows", "en-US", SpeechVoiceGender.Female), CreateKokoroVoice("af_heart", "Heart")];
        fixture.Preferences.ProviderId = provider;
        return fixture;
    }

    [Fact]
    public async Task Corrupt_limit_state_is_visible_in_native_getters_and_status_without_disabling_exact_readback()
    {
        var fixture = CreateSummaryFixture(SpeechProviderIds.Windows);
        fixture.Preferences.SummaryLimitsLoadFailure = new InvalidDataException("unknown format");
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedSummaryWords.Should().BeNull();
        fixture.ViewModel.SelectedSummarySentences.Should().BeNull();
        fixture.ViewModel.SpeechSettingStatus.Should().Contain("speech.summary-words = invalid")
            .And.Contain("speech.summary-sentences = invalid").And.Contain("saved limits").And.Contain("repair");
        await fixture.RunAsync("reset speech.summary-words");
        fixture.ViewModel.ResponseTitle.Should().Contain("Choose");
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        await fixture.RunAsync("what power action is pending");
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.ResponseOutputStatus.Should().Contain("repair");
        fixture.ViewModel.ResponseBody.Should().Contain("intentionally disabled");
        fixture.Reasoner.Question = new("Continue?", ["Yes", "No"]);
        await fixture.RunAsync("choose");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.TextToSpeech.SpokenText.Should().Contain("Choose an option by number");
    }

    [Fact]
    public async Task Reentrant_cap_change_at_provider_enqueue_retires_the_original_generation_without_playback()
    {
        var fixture = CreateSummaryFixture(SpeechProviderIds.Windows);
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.BeforeSpeak = () => fixture.ViewModel.SelectedSummaryWords = 1;
        await fixture.RunAsync("what power action is pending");
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Preferences.SummaryLimits.Should().Be(new SpokenSummaryLimits(3, 1));
        fixture.ViewModel.ResponseBody.Should().Contain("intentionally disabled");
        fixture.ViewModel.IsSpeaking.Should().BeFalse();
        fixture.TextToSpeech.BeforeSpeak = null;
        await fixture.ViewModel.ResetSummaryWordsCommand.ExecuteAsync();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }
}
