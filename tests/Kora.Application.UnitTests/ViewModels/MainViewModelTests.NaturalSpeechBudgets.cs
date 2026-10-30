using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task NaturalVoiceBudgetsRequireTheCurrentExactNameAndRemainLocal()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.RunAsync("set assistant.name to Nova");
        fixture.ViewModel.AssistantName.Should().Be("Nova");
        await fixture.RaiseActivatedTranscriptAsync("set spoken summary word limit to 40", 1);
        fixture.ViewModel.Transcript.Should().Contain("current assistant-name prefix");
        fixture.Preferences.SummaryLimits.Should().BeNull();
        await fixture.RaiseActivatedTranscriptAsync("Kora, set spoken summary word limit to 40", 1);
        fixture.Preferences.SummaryLimits.Should().BeNull();
        await fixture.RaiseActivatedTranscriptAsync("Novax, set spoken summary word limit to 40", 1);
        fixture.Preferences.SummaryLimits.Should().BeNull();
        await fixture.RaiseActivatedTranscriptAsync("Nova, set spoken summary word limit to 40", 1);
        fixture.Preferences.SummaryLimits.Should().Be(new SpokenSummaryLimits(3, 40));
        fixture.Voice.StartedPhrases.Should().Contain("Nova show spoken summary limits")
            .And.NotContain("Kora show spoken summary limits");
        await fixture.RunAsync("what can you do");
        fixture.ViewModel.ResponseBody.Should().Contain("show spoken summary limits");
        fixture.TextToSpeech.ClearSpokenResponse();
        await fixture.RunAsync("show spoken summary limits");
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NaturalBudgetsRejectLateOwnershipOrLockLoss(bool ownerLoss)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Dispatcher.BeforeInvoke = () =>
        {
            if (ownerLoss) { fixture.ViewModel.BindCallOwnershipGate(static () => false); }
            else { fixture.Session.IsUnlocked = false; }
        };
        await fixture.RunAsync("set spoken summary word limit to 40");
        fixture.Preferences.SummaryLimits.Should().BeNull();
        fixture.Audit.Events.Should().Contain(item => item.Outcome == SecurityAuditOutcome.Denied
            && string.Equals(item.ActionId, "configuration.speech-summary-words", StringComparison.Ordinal));
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task NaturalBudgetsShareNativeStateDiscoveryAuditAndIndependentResetWithoutSpeech()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.TextToSpeech.ClearSpokenResponse();
        await fixture.RunAsync("show spoken summary limits");
        fixture.ViewModel.ResponseBody.Should().Contain("speech.summary-sentences = 3").And.Contain("speech.summary-words = 80")
            .And.Contain("unsaved limit defaults").And.Contain("revision");
        fixture.Preferences.SummaryLimits.Should().BeNull();
        await fixture.RunAsync("set spoken summary word limit to 40");
        await fixture.RaiseActivatedTranscriptAsync("Kora, set spoken summary sentence limit to 1", 1);
        fixture.Preferences.SummaryLimits.Should().Be(new SpokenSummaryLimits(1, 40));
        fixture.ViewModel.SelectedSummaryWords.Should().Be(40);
        fixture.ViewModel.SelectedSummarySentences.Should().Be(1);
        await fixture.RunAsync("reset spoken summary word limit");
        fixture.Preferences.SummaryLimits.Should().Be(new SpokenSummaryLimits(1, 80));
        await fixture.RaiseActivatedTranscriptAsync("Kora, reset spoken summary sentence limit", 1);
        fixture.Preferences.SummaryLimits.Should().Be(SpokenSummaryLimits.Default);
        await fixture.RunAsync("list speech settings");
        fixture.ViewModel.ResponseBody.Should().Contain("show spoken summary limits").And.Contain("future ordinary-speech budgets")
            .And.Contain("saved limits");
        fixture.Voice.StartedPhrases.Should().Contain(SpeechCommand.FixedPhrases)
            .And.Contain("Kora show spoken summary limits");
        var writes = fixture.Audit.Events.Where(item => item.ActionId.StartsWith("configuration.speech-summary-", StringComparison.Ordinal)).ToArray();
        writes.Should().HaveCount(8);
        writes.Chunk(2).Should().OnlyContain(pair => pair[0].Outcome == SecurityAuditOutcome.Requested
            && pair[1].Outcome == SecurityAuditOutcome.Succeeded && pair[0].CorrelationId == pair[1].CorrelationId);
        writes.Where(item => string.Equals(item.ActionId, "configuration.speech-summary-sentences", StringComparison.Ordinal)).Should()
            .OnlyContain(item => item.Initiator == SecurityAuditInitiator.VoiceCommand);
        writes.Where(item => string.Equals(item.ActionId, "configuration.speech-summary-words", StringComparison.Ordinal)).Should()
            .OnlyContain(item => item.Initiator == SecurityAuditInitiator.TypedCommand);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Preferences.SavedProviderId.Should().BeNull();
        fixture.Preferences.SavedVoiceId.Should().BeNull();
        fixture.ViewModel.SelectedOutputDevice.Should().Be(SystemAudioDevices.Output);
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Unknown)]
    public async Task NaturalVoiceWritesRemainDeniedDuringProtectedCallsButNewTypedWritesAreEligible(CallState state)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.RunAsync("set spoken summary word limit to 40");
        fixture.CallState.SetState(state);
        await fixture.Dispatcher.LastInvocation;
        await fixture.RaiseActivatedTranscriptAsync("Kora, set spoken summary sentence limit to 1", 1);
        await fixture.RaiseActivatedTranscriptAsync("Kora, reset spoken summary word limit", 1);
        fixture.Preferences.SummaryLimits.Should().Be(new SpokenSummaryLimits(3, 40));
        fixture.Audit.Events.Where(item => item.Outcome == SecurityAuditOutcome.Denied).Should().HaveCount(2)
            .And.OnlyContain(item => item.Initiator == SecurityAuditInitiator.VoiceCommand);
        await fixture.RunAsync("set spoken summary sentence limit to 1");
        await fixture.RunAsync("reset spoken summary word limit");
        fixture.Preferences.SummaryLimits.Should().Be(new SpokenSummaryLimits(1, 80));
        fixture.ViewModel.IsProtectedCall.Should().BeTrue();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("set spoken summary word limit to 20")]
    [InlineData("reset spoken summary sentence limit")]
    [InlineData("reset spoken summary word limit")]
    public async Task NaturalBudgetsNeverDefaultAnInvalidPersistedCompanion(string text)
    {
        var fixture = new Fixture();
        fixture.Preferences.SummaryLimits = new(4, 40);
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync(text);
        fixture.Preferences.SummaryLimits.Should().Be(new SpokenSummaryLimits(4, 40));
        fixture.ViewModel.ResponseBody.Should().Contain("unknown companion limits cannot be defaulted");
        fixture.ViewModel.SelectedSummaryWords.Should().BeNull();
        fixture.ViewModel.SelectedSummarySentences.Should().BeNull();
        fixture.Audit.Events.Should().NotContain(item => item.ActionId.StartsWith("configuration.speech-summary-", StringComparison.Ordinal));
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NaturalBudgetCommandsLeavePendingQuestionsAndApprovalsUntouched(bool question)
    {
        var fixture = CreateSummaryFixture(SpeechProviderIds.Windows);
        fixture.Reasoner.Action = BuiltInAction.EnableHostedModels;
        if (question) { fixture.Reasoner.Question = new("Which color?", ["Blue", "Green"]); }
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("please do this work");
        await fixture.ViewModel.ActiveReasoningTask!;
        var title = fixture.ViewModel.ResponseTitle;
        var body = fixture.ViewModel.ResponseBody;
        var spoken = fixture.TextToSpeech.SpokenText;
        foreach (var command in new[] { "show spoken summary limits", "set spoken summary word limit to 40",
            "reset spoken summary sentence limit", "set spoken summary word limit to 1.5" })
        {
            await fixture.RunAsync(command);
            fixture.ViewModel.ResponseTitle.Should().Be(title);
            fixture.ViewModel.ResponseBody.Should().Be(body);
            fixture.ViewModel.Transcript.Should().Contain("pending question or approval");
            fixture.ViewModel.IsResponseInteractionPending.Should().BeTrue();
        }
        fixture.Preferences.SummaryLimits.Should().BeNull();
        fixture.Reasoner.Requests.Should().ContainSingle();
        fixture.ViewModel.HostedModelsEnabled.Should().Be(false);
        fixture.Session.LockCalls.Should().Be(0);
        fixture.TextToSpeech.SpokenText.Should().Be(spoken);
    }

    [Theory]
    [InlineData("set spoken summary word limit to 1.5")]
    [InlineData("show spoken summary limits and read the answer")]
    [InlineData("reset spoken summary word limit please")]
    public async Task ReservedMalformedBudgetsClarifyBeforeAnyModelFallback(string text)
    {
        var fixture = CreateSummaryFixture(SpeechProviderIds.Windows);
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync(text);
        fixture.ViewModel.ResponseTitle.Should().Be("Clarify the speech setting.");
        fixture.Preferences.SummaryLimits.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Fact]
    public async Task NaturalBudgetsPreserveFullOversizedVisualResultsAndNeverRegenerateOrReplay()
    {
        var fixture = CreateSummaryFixture(SpeechProviderIds.Windows);
        var answer = string.Join(' ', Enumerable.Repeat("original", 90)) + ". Keep this last sentence.";
        fixture.Reasoner.Response = new(answer, null);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        await fixture.RunAsync("set spoken summary sentence limit to 1");
        await fixture.RunAsync("set spoken summary word limit to 1");
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        await fixture.RunAsync("explain this");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.ViewModel.ResponseBody.Should().Be($"Generated locally; verify important details.\n\n{answer}");
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.ResponseOutputStatus.Should().Contain("Speech withheld").And.Contain("No text was shortened");
        fixture.Reasoner.Requests.Should().ContainSingle();
        await fixture.RunAsync("reset spoken summary word limit");
        fixture.Preferences.SummaryLimits.Should().Be(new SpokenSummaryLimits(1, 80));
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task NaturalBudgetsRecheckDispatchAdmissionAndSurfacePersistenceFailure()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Dispatcher.BeforeInvoke = () => fixture.Session.IsUnlocked = false;
        await fixture.RunAsync("set spoken summary word limit to 40");
        fixture.Preferences.SummaryLimits.Should().BeNull();
        fixture.Dispatcher.BeforeInvoke = null;
        fixture.Session.IsUnlocked = true;
        fixture.Preferences.SaveException = new IOException("atomic budget failure");
        await fixture.RunAsync("set spoken summary word limit to 40");
        fixture.ViewModel.ResponseTitle.Should().Be("The speech setting was not changed.");
        fixture.ViewModel.SpeechSettingStatus.Should().Contain("atomic budget failure");
        fixture.Preferences.SummaryLimits.Should().BeNull();
        fixture.Preferences.SaveException = null;
        fixture.Dispatcher.BeforeInvoke = () => fixture.CallState.SetState(CallState.Active);
        await fixture.RunAsync("reset spoken summary sentence limit");
        fixture.Preferences.SummaryLimits.Should().BeNull();
        fixture.ViewModel.SpeechSettingStatus.Should().Contain("policy changed");
        fixture.Dispatcher.BeforeInvoke = fixture.ViewModel.Dispose;
        await fixture.RunAsync("reset spoken summary word limit");
        fixture.Preferences.SummaryLimits.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
    }
}
