using System.Text.Json;

using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("Kora")]
    [InlineData("Nova")]
    public async Task SpeechSuggestionListeningGrammarRegistersOnlyTheThreeExactCommandsAndCurrentNameVariants(string name)
    {
        var f = await Fixture.CreateInitializedAsync();
        if (!string.Equals(name, "Kora", StringComparison.Ordinal)) { await f.RunAsync("set assistant.name to " + name); }
        await f.RaiseActivatedTranscriptAsync(name + ", show spoken summary limits", 1);
        string[] expected =
        [
            "review speech limit suggestion", name + " review speech limit suggestion",
            "confirm speech limit change", name + " confirm speech limit change",
            "cancel speech limit review", name + " cancel speech limit review",
        ];
        f.Voice.StartedPhrases.Where(phrase => phrase.Contains("speech limit", StringComparison.OrdinalIgnoreCase))
            .Should().BeEquivalentTo(expected);
        SpeechSummarySuggestionCommand.FixedPhrases.Should()
            .Equal("review speech limit suggestion", "confirm speech limit change", "cancel speech limit review");
        f.Voice.StartedPhrases.Select(phrase => phrase.ToUpperInvariant()).Should().OnlyHaveUniqueItems();
        f.Preferences.SummaryLimits.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
        await f.ViewModel.DisposeAsync();
    }

    private static (SessionCommandStore Store, SpeechSuggestionModel Model) BindSpeechSuggestions(Fixture fixture, bool bindSpeech = true)
    {
        var store = new SessionCommandStore(fixture);
        var workspace = new SessionWorkspaceService(store, new(fixture.HostStore), store, NullLogger<SessionWorkspaceService>.Instance);
        var model = new SpeechSuggestionModel();
        var conversation = new SessionConversationService(store, store, workspace, store, model, TimeProvider.System,
            NullLogger<SessionConversationService>.Instance, execution: model);
        conversation.RunningConversationChanged += status =>
        {
            if (status.Phase == ConversationExecutionPhase.Terminal) { store.SpeechTerminalPublished = true; }
        };
        fixture.ViewModel.BindSessionCommands(workspace, new(workspace, conversation, store, TimeProvider.System));
        if (bindSpeech) { fixture.ViewModel.BindSpeechSummarySuggestions(conversation, TimeProvider.System); }
        return (store, model);
    }

    private static async Task SubmitSpeechSuggestion(Fixture fixture, SessionCommandStore store, bool voice, bool expectRetained = true)
    {
        var id = store.Session.Authority.SessionId.Value.ToString("D");
        var review = "session request review " + id + " 1 " + (store.Session.Metadata?.Revision.Value ?? 0)
            .ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " -- Suggest a word limit of forty for future ordinary spoken responses.";
        if (voice) { await fixture.RaiseActivatedTranscriptAsync("Kora, " + review, 1); }
        else { await fixture.RunAsync(review); }
        fixture.ViewModel.ResponseTitle.Should().Be("Session command review.");
        using var json = JsonDocument.Parse(fixture.ViewModel.ResponseBody);
        var confirmation = "session request confirm " + id + " " + json.RootElement.GetProperty("requestReview").GetProperty("id").GetString();
        await fixture.RunAsync(confirmation);
        fixture.ViewModel.ResponseTitle.Should().Be(expectRetained ? "Session command retained." : "Session command not confirmed.",
            fixture.ViewModel.ResponseBody);
        store.RequestMessage!.HasAssistantResponse.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public async Task SpeechSuggestionCanonicalTypedAndOriginalVoiceRoutesRequireReviewThenSeparateExactConfirm(
        bool sourceVoice, bool reviewVoice, bool confirmVoice)
    {
        var f = await Fixture.CreateInitializedAsync();
        var (store, model) = BindSpeechSuggestions(f);
        await SubmitSpeechSuggestion(f, store, sourceVoice);
        f.Preferences.SummaryLimits.Should().BeNull();
        var retained = store.RequestMessage;
        var before = f.Audit.Events.Count;
        if (reviewVoice) { await f.RaiseActivatedTranscriptAsync("Kora, review speech limit suggestion", 1); }
        else { await f.RunAsync("review speech limit suggestion"); }
        f.ViewModel.ResponseTitle.Should().Be("Review exact speech limit suggestion.");
        f.ViewModel.ResponseBody.Should().Contain("Current: 3 sentences, 80 words (default)")
            .And.Contain("Proposed: 3 sentences, 40 words").And.Contain("shared device-local");
        f.Preferences.SummaryLimits.Should().BeNull();
        f.Audit.Events.Count.Should().Be(before);
        if (confirmVoice) { await f.RaiseActivatedTranscriptAsync("Kora, confirm speech limit change", 1); }
        else { await f.RunAsync("confirm speech limit change"); }
        f.ViewModel.ResponseTitle.Should().Be("Speech limit saved.");
        f.ViewModel.ResponseBody.Should().Contain("saved limits");
        f.Preferences.SummaryLimits.Should().Be(new SpokenSummaryLimits(3, 40));
        f.ViewModel.SelectedSummaryWords.Should().Be(40);
        f.ViewModel.SelectedSummarySentences.Should().Be(3);
        f.Audit.Events.Skip(before).Should().HaveCount(2).And.OnlyContain(item => item.Initiator ==
            (sourceVoice || reviewVoice || confirmVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand));
        await f.RunAsync("confirm speech limit change");
        f.ViewModel.ResponseTitle.Should().Be("Speech limit source unavailable.");
        model.Calls.Should().Be(1);
        store.RequestMessage.Should().Be(retained);
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("confirm-first")]
    [InlineData("unprefixed")]
    [InlineData("call")]
    [InlineData("source")]
    [InlineData("settings")]
    [InlineData("owner")]
    [InlineData("privacy")]
    [InlineData("rebind")]
    public async Task SpeechSuggestionCommandRetiresExactSourceAndNeverReplaysOrRetargets(string loss)
    {
        var f = await Fixture.CreateInitializedAsync();
        var (store, model) = BindSpeechSuggestions(f);
        await SubmitSpeechSuggestion(f, store, voice: true);
        if (loss is not "confirm-first") { await f.RunAsync("review speech limit suggestion"); }
        if (loss is "cancel") { await f.RunAsync("cancel speech limit review"); }
        if (loss is "unprefixed") { await f.RaiseActivatedTranscriptAsync("confirm speech limit change", 1); }
        if (loss is "call") { f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; }
        if (loss is "source") { store.RequestMessage = store.RequestMessage! with { Body = "different retained source" }; }
        if (loss is "settings") { await f.RunAsync("set speech.summary-words to 30"); }
        if (loss is "owner") { f.ViewModel.BindCallOwnershipGate(() => false); }
        if (loss is "privacy") { f.PrivacyObservation.Current = f.PrivacyObservation.Current with { TopologyRevision = 2 }; }
        if (loss is "rebind") { BindSpeechSuggestions(f); }
        await f.RunAsync("confirm speech limit change");
        f.ViewModel.ResponseTitle.Should().NotBe("Speech limit saved.");
        f.Preferences.SummaryLimits.Should().Be(loss is "settings" ? new SpokenSummaryLimits(3, 30) : null);
        model.Calls.Should().Be(1);
        f.Reasoner.Requests.Should().BeEmpty();
        await f.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task SpeechSuggestionWithoutPinnedReceiptNeverSelectsAnAnswerOrConsumesPendingQuestion()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "Ready");
        f.Reasoner.Question = new("Which target?", ["One", "Two"]);
        await f.ViewModel.InitializeAsync();
        BindSpeechSuggestions(f);
        await f.RunAsync("review speech limit suggestion");
        f.ViewModel.ResponseTitle.Should().Be("Speech limit source unavailable.");
        await f.RunAsync("choose");
        await f.ViewModel.ActiveReasoningTask!;
        await f.RunAsync("confirm speech limit change");
        f.ViewModel.ResponseTitle.Should().Be("Speech limit review blocked.");
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        f.Preferences.SummaryLimits.Should().BeNull();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("store")]
    [InlineData("requested-audit")]
    [InlineData("terminal-audit")]
    [InlineData("late-lock")]
    public async Task SpeechSuggestionCommandSaveFailureIsExplicitVisualAndNotAutomaticallyRetried(string failure)
    {
        var f = await Fixture.CreateInitializedAsync();
        var (store, model) = BindSpeechSuggestions(f);
        await SubmitSpeechSuggestion(f, store, voice: false);
        await f.RunAsync("review speech limit suggestion");
        if (failure is "store") { f.Preferences.SaveException = new IOException("storage unavailable"); }
        f.Audit.BeforeWrite = item =>
        {
            if (!item.ActionId.StartsWith("configuration.speech-summary-", StringComparison.Ordinal)) { return; }
            if (failure is "requested-audit" && item.Outcome == SecurityAuditOutcome.Requested
                || failure is "terminal-audit" && item.Outcome == SecurityAuditOutcome.Succeeded)
            { throw new InvalidOperationException("audit unavailable"); }
            if (failure is "late-lock" && item.Outcome == SecurityAuditOutcome.Requested) { f.Session.IsUnlocked = false; }
        };
        await f.RunAsync("confirm speech limit change");
        f.ViewModel.ResponseTitle.Should().NotBe("Speech limit saved.");
        f.Preferences.SummaryLimits.Should().Be(failure is "terminal-audit" ? new SpokenSummaryLimits(3, 40) : null);
        model.Calls.Should().Be(1);
        f.Audit.BeforeWrite = null;
        f.Session.IsUnlocked = true;
        await f.RunAsync("confirm speech limit change");
        f.ViewModel.ResponseTitle.Should().Be("Speech limit source unavailable.");
        f.TextToSpeech.SpokenText.Should().BeNull();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("ownership")]
    [InlineData("binding")]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("source")]
    [InlineData("consent")]
    [InlineData("voice-off")]
    public async Task SpeechSuggestionNativeHostGatesRetainOriginalVoiceAndCapturedLifetime(string loss)
    {
        var f = await Fixture.CreateInitializedAsync();
        BindSpeechSuggestions(f);
        await f.RaiseActivatedTranscriptAsync("Kora, show spoken summary limits", 1);
        long ownership = 1;
        f.ViewModel.BindCallOwnershipGate(() => true, () => ownership);
        var message = AppearanceSuggestionMessage() with
        {
            Request = HostRequest.Create(RequestOrigin.ActivatedVoice),
        };
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), message.Request, ModelProviderSelection.OllamaCandidate);
        message = message with { Execution = provenance, AssistantResponse = new(provenance, "set speech.summary-words to 40") };
        SpeechSummarySuggestionReview review;
        var live = true;
        using (AppearanceSuggestionInput(message))
        {
            review = await f.ViewModel.ReviewSpeechSummarySuggestionAsync(message, () => live,
                _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        }
        if (loss is "ownership") { ownership++; }
        if (loss is "binding") { f.ViewModel.BindCallOwnershipGate(() => true, () => ownership); }
        if (loss is "privacy") { f.PrivacyObservation.Current = f.PrivacyObservation.Current with { TopologyRevision = 2 }; }
        if (loss is "call") { f.CallState.SetState(CallState.Unknown); await f.Dispatcher.LastInvocation; }
        if (loss is "source") { live = false; }
        if (loss is "consent") { await f.ViewModel.SetVoiceConsentAsync(false); }
        if (loss is "voice-off") { await f.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
        using (review)
        using (AppearanceSuggestionInput(message))
        {
            review.Available.Should().BeFalse();
            await f.ViewModel.Invoking(view => view.ConfirmSpeechSummarySuggestionAsync(review, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
            f.Preferences.SummaryLimits.Should().BeNull();
        }
        await f.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task SpeechSuggestionHostRequiresBindingAndJoinsPendingConfirmationOnDisposal()
    {
        var f = await Fixture.CreateInitializedAsync();
        var message = AppearanceSuggestionMessage();
        message = message with { AssistantResponse = new(message.Execution!, "set speech.summary-words to 40") };
        using (AppearanceSuggestionInput(message))
        {
            await FluentActions.Awaiting(() => f.ViewModel.ReviewSpeechSummarySuggestionAsync(message, () => true,
                _ => Task.CompletedTask, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        }
        BindSpeechSuggestions(f);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirming = false;
        async Task Validate(CancellationToken token)
        {
            if (!confirming) { return; }
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        }
        SpeechSummarySuggestionReview review;
        using (AppearanceSuggestionInput(message))
        {
            review = await f.ViewModel.ReviewSpeechSummarySuggestionAsync(message, () => true, Validate,
                TestContext.Current.CancellationToken);
        }
        using (review)
        using (AppearanceSuggestionInput(message))
        {
            confirming = true;
            var work = f.ViewModel.ConfirmSpeechSummarySuggestionAsync(review, TestContext.Current.CancellationToken);
            await entered.Task;
            await f.ViewModel.Invoking(view => view.ReviewSpeechSummarySuggestionAsync(message, () => true, Validate,
                TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
            await f.ViewModel.Invoking(view => view.ConfirmSpeechSummarySuggestionAsync(review,
                TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
            var disposal = f.ViewModel.DisposeAsync().AsTask();
            disposal.IsCompleted.Should().BeFalse();
            release.TrySetResult();
            await FluentActions.Awaiting(() => work).Should().ThrowAsync<InvalidOperationException>();
            await FluentActions.Awaiting(() => disposal).Should().ThrowAsync<InvalidOperationException>();
            f.Preferences.SummaryLimits.Should().BeNull();
        }
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("done")]
    [InlineData("metadata")]
    [InlineData("binding")]
    public async Task SpeechSuggestionReceiptRaceCannotPinChangedTargetOrReviveNewBinding(string fault)
    {
        var f = await Fixture.CreateInitializedAsync();
        var (store, model) = BindSpeechSuggestions(f);
        var injected = false;
        store.BeforeSpeechReceiptMetadataRead = () =>
        {
            injected = true;
            if (fault is "binding") { BindSpeechSuggestions(f); }
            else { store.ChangeSpeechTarget(fault); }
        };
        await SubmitSpeechSuggestion(f, store, voice: false, expectRetained: false);
        injected.Should().BeTrue("the race must occur after canonical terminal readbacks, during receipt pinning");
        f.ViewModel.ResponseBody.Should().Contain("exact successful submission receipt is no longer current");
        await f.RunAsync("review speech limit suggestion");
        f.ViewModel.ResponseTitle.Should().Be("Speech limit source unavailable.");
        f.Preferences.SummaryLimits.Should().BeNull();
        model.Calls.Should().Be(1);
        await f.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task SpeechSuggestionVoiceConsentLossDuringReceiptPinCannotAuthorizeReviewOrConfirmation()
    {
        var f = await Fixture.CreateInitializedAsync();
        var (store, model) = BindSpeechSuggestions(f);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        f.VoiceConsent.BeforeSave = consent =>
        {
            if (consent) { return; }
            entered.TrySetResult();
            release.Wait(token);
        };
        Task? withdrawal = null;
        store.BeforeSpeechReceiptMetadataRead = () =>
        {
            withdrawal = f.ViewModel.SetVoiceConsentAsync(false);
        };
        try
        {
            await SubmitSpeechSuggestion(f, store, voice: true);
            withdrawal.Should().NotBeNull("withdrawal must occur during receipt pinning, not before submission");
            await entered.Task.WaitAsync(token);
            f.ViewModel.HasVoiceConsent.Should().BeFalse();
            f.Voice.IsListening.Should().BeFalse();
            withdrawal!.IsCompleted.Should().BeFalse();
        }
        finally { release.Set(); }
        await withdrawal!;
        f.ViewModel.HasVoiceConsent.Should().BeFalse();
        f.VoiceConsent.Consent.Should().BeFalse();
        f.ViewModel.ResponseTitle.Should().Be("Voice consent declined or withdrawn.");
        store.RequestMessage!.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        await f.RunAsync("review speech limit suggestion");
        f.ViewModel.ResponseTitle.Should().Be("Speech limit change unconfirmed.");
        await f.RunAsync("confirm speech limit change");
        f.ViewModel.ResponseTitle.Should().Be("Speech limit source unavailable.");
        f.Preferences.SummaryLimits.Should().BeNull();
        model.Calls.Should().Be(1);
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("request")]
    [InlineData("state")]
    public async Task SpeechSuggestionCanonicalTerminalReadbackRejectsForgedReceiptBeforePinning(string fault)
    {
        var f = await Fixture.CreateInitializedAsync();
        var (store, model) = BindSpeechSuggestions(f);
        store.SpeechTerminalReceiptFault = fault;
        await SubmitSpeechSuggestion(f, store, voice: false, expectRetained: false);
        await f.RunAsync("review speech limit suggestion");
        f.ViewModel.ResponseTitle.Should().Be("Speech limit source unavailable.");
        f.Preferences.SummaryLimits.Should().BeNull();
        model.Calls.Should().Be(1);
        await f.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task SpeechSuggestionUnboundConsumerCannotPinACompletedCanonicalReceipt()
    {
        var f = await Fixture.CreateInitializedAsync();
        var (store, model) = BindSpeechSuggestions(f, bindSpeech: false);
        await SubmitSpeechSuggestion(f, store, voice: false);
        await f.RunAsync("review speech limit suggestion");
        f.ViewModel.ResponseTitle.Should().Be("Speech limit source unavailable.");
        model.Calls.Should().Be(1);
        f.Preferences.SummaryLimits.Should().BeNull();
        await f.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task SpeechSuggestionSavedSessionMetadataAndRepeatedReviewKeepOneExactCurrentTarget()
    {
        var f = await Fixture.CreateInitializedAsync();
        var (store, model) = BindSpeechSuggestions(f);
        store.ChangeSpeechTarget("metadata");
        await SubmitSpeechSuggestion(f, store, voice: false);
        await f.RunAsync("review speech limit suggestion");
        await f.RunAsync("review speech limit suggestion");
        f.ViewModel.ResponseTitle.Should().Be("Review exact speech limit suggestion.");
        f.Preferences.SummaryLimits.Should().BeNull();
        await f.RunAsync("confirm speech limit change");
        f.Preferences.SummaryLimits.Should().Be(new SpokenSummaryLimits(3, 40));
        model.Calls.Should().Be(1);
        await f.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task SpeechSuggestionCancelledSourceReadRetiresReviewWithoutWriteOrReplay()
    {
        var f = await Fixture.CreateInitializedAsync();
        var (store, model) = BindSpeechSuggestions(f);
        await SubmitSpeechSuggestion(f, store, voice: false);
        store.BeforeSpeechExactRead = () => throw new OperationCanceledException("source retired");
        await f.RunAsync("review speech limit suggestion");
        f.ViewModel.ResponseTitle.Should().Be("Speech limit change unconfirmed.");
        f.Preferences.SummaryLimits.Should().BeNull();
        store.BeforeSpeechExactRead = null;
        await f.RunAsync("confirm speech limit change");
        f.ViewModel.ResponseTitle.Should().Be("Speech limit source unavailable.");
        model.Calls.Should().Be(1);
        await f.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task SpeechSuggestionLateHostDisposalDuringWriteFailureDoesNotReopenPrivateFeedback()
    {
        var f = await Fixture.CreateInitializedAsync();
        var (store, _) = BindSpeechSuggestions(f);
        await SubmitSpeechSuggestion(f, store, voice: false);
        await f.RunAsync("review speech limit suggestion");
        f.Audit.BeforeWrite = item =>
        {
            if (!item.ActionId.StartsWith("configuration.speech-summary-", StringComparison.Ordinal)) { return; }
            if (item.Outcome == SecurityAuditOutcome.Requested) { f.ViewModel.Dispose(); throw new IOException("retired host"); }
        };
        await f.RunAsync("confirm speech limit change");
        f.Preferences.SummaryLimits.Should().BeNull();
        f.ViewModel.ResponseTitle.Should().NotBe("Speech limit saved.");
        f.Audit.BeforeWrite = null;
        await f.ViewModel.DisposeAsync();
    }

    private sealed partial class SessionCommandStore
    {
        internal Action? BeforeSpeechReceiptMetadataRead { get; set; }
        internal bool SpeechTerminalPublished { get; set; }
        internal Action? BeforeSpeechExactRead { get; set; }
        internal string? SpeechTerminalReceiptFault { get; set; }
        internal void ChangeSpeechTarget(string fault)
        {
            Session = fault switch
            {
                "generation" => Session with { Authority = Session.Authority with { Generation = new(2) } },
                "done" => Session with { Authority = Session.Authority with { IsActive = false } },
                "metadata" => Session with { Metadata = new(Session.Authority.SessionId, new(1), new("changed")) },
                _ => throw new ArgumentOutOfRangeException(nameof(fault)),
            };
        }
        public ValueTask<ConversationSubmissionReceipt> BeginConversationExecutionAsync(HostRequest request,
            ModelTurnProvenance provenance, long metadataRevision, Func<bool> admitted, CancellationToken token)
        {
            admitted().Should().BeTrue();
            RequestMessage = RequestMessage! with
            { Version = 2, Revision = 2, Execution = provenance, ExecutionMetadataRevision = metadataRevision };
            TaskResult = TaskResult! with { Task = TaskResult.Task.Next(HostTaskState.DispatchRecorded) };
            return ValueTask.FromResult(new ConversationSubmissionReceipt(RequestMessage, TaskResult.Task));
        }

        public ValueTask<ConversationSubmissionReceipt> CompleteConversationExecutionAsync(HostRequest request,
            ModelTurnResult result, Func<bool> admitted, CancellationToken token)
        {
            admitted().Should().BeTrue();
            RequestMessage = RequestMessage! with
            {
                Revision = 3, Outcome = result.Outcome, Reason = result.Reason,
                AssistantResponse = ConversationResponse.FromResult(result, RequestMessage.Execution!),
            };
            TaskResult = TaskResult! with { Task = TaskResult.Task.Next(RequestMessage.ExpectedTaskState) };
            var task = SpeechTerminalReceiptFault switch
            {
                "request" => new HostTaskRecord(HostRequest.Create(RequestOrigin.LocalUi), new(3), HostTaskState.Succeeded),
                "state" => new HostTaskRecord(request, new(3), HostTaskState.Denied),
                _ => TaskResult.Task,
            };
            return ValueTask.FromResult(new ConversationSubmissionReceipt(RequestMessage, task));
        }
    }

    private sealed class SpeechSuggestionModel : IModelTurnAdmission, IModelTurnExecution
    {
        internal string Answer { get; set; } = "set speech.summary-words to 40";
        internal int Calls { get; private set; }
        public bool IsQuiescent => true;
        public Task WaitForQuiescenceAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token)
        {
            var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), context.Request, ModelProviderSelection.OllamaCandidate);
            var turn = new ModelTurn(Guid.NewGuid(), HostActivity.RequireCurrent(), provenance, context, context.Serialize(),
                new(1), new(1), 1, new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.None, DateTimeOffset.MinValue, null));
            return Task.FromResult(new ModelTurnAdmission(new(ModelTurnOutcome.Succeeded, ModelTurnReason.None, provenance), turn));
        }

        public Task<ModelTurnResult> RunAsync(ModelTurn turn, Func<bool> admitted, CancellationToken token,
            Func<CancellationToken, Task>? validateSource = null)
        {
            Calls++;
            admitted().Should().BeTrue();
            var result = new ModelTurnResult(ModelTurnOutcome.Succeeded, ModelTurnReason.None, turn.Provenance,
                new(Answer, null));
            turn.TerminalResult = result;
            return Task.FromResult(result);
        }
    }
}
