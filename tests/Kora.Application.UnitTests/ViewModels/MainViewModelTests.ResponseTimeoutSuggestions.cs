using AwesomeAssertions;

using Kora.Application.Configuration.RegisteredSettings;
using Kora.Core.Commands;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false, false, false, 1)]
    [InlineData(false, true, false, 60)]
    [InlineData(false, false, true, 15)]
    [InlineData(true, false, false, 15)]
    [InlineData(true, true, true, 60)]
    public async Task ResponseTimeoutSuggestionChannelsCompleteOnlyTheExactAuditedPreference(
        bool sourceVoice, bool reviewVoice, bool confirmVoice, int seconds)
    {
        var f = await Fixture.CreateInitializedAsync();
        try
        {
            var (store, model) = BindSpeechSuggestions(f);
            model.Answer = $"set appearance.response-timeout to {seconds}";
            await SubmitSpeechSuggestion(f, store, sourceVoice);
            var retained = store.RequestMessage;
            f.TextToSpeech.ClearSpokenResponse();
            var before = f.Audit.Events.Count;
            if (reviewVoice) { await f.RaiseActivatedTranscriptAsync("Kora, review response timeout suggestion", 1); }
            else { await f.RunAsync("review response timeout suggestion"); }
            f.ViewModel.ResponseTitle.Should().Be("Review exact response-timeout suggestion.", f.ViewModel.ResponseBody);
            f.ViewModel.ResponseBody.Should().Contain("current 5 (default)")
                .And.Contain($"proposed {seconds} seconds").And.Contain("shared device-local")
                .And.Contain("not a provider deadline").And.Contain("Always show");
            f.AppearancePreferences.LoadResponseTimeoutSeconds().Should().BeNull();
            f.Audit.Events.Should().HaveCount(before);
            if (confirmVoice) { await f.RaiseActivatedTranscriptAsync("Kora, confirm response timeout change", 1); }
            else { await f.RunAsync("confirm response timeout change"); }
            f.ViewModel.ResponseTitle.Should().Be("Response timeout saved.", f.ViewModel.ResponseBody);
            f.AppearancePreferences.LoadResponseTimeoutSeconds().Should().Be(seconds);
            f.ViewModel.ResponseTimeoutSeconds.Should().Be(seconds);
            f.AppearancePreferences.LoadPresenceTimeoutSeconds().Should().BeNull();
            f.Audit.Events.Skip(before).Select(item => item.Outcome).Should()
                .Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
            f.Audit.Events.Skip(before).Should().OnlyContain(item => item.ActionId == "configuration.response-timeout"
                && item.Initiator == (sourceVoice || reviewVoice || confirmVoice
                    ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand));
            await f.RunAsync("confirm response timeout change");
            f.ViewModel.ResponseTitle.Should().Be("Response-timeout source unavailable.");
            model.Calls.Should().Be(1);
            store.RequestMessage.Should().Be(retained);
            f.Reasoner.Requests.Should().BeEmpty();
            f.TextToSpeech.SpokenText.Should().BeNull();
            await f.RunAsync("undo the last settings change");
            f.ViewModel.CanConfirmSettingsUndo.Should().BeTrue();
            f.ViewModel.SettingsUndoDescription.Should().Contain($"current {seconds} (saved); restore 5 (default)");
            await f.RunAsync("confirm settings undo");
            f.AppearancePreferences.LoadResponseTimeoutSeconds().Should().BeNull();
            f.ViewModel.ResponseTimeoutSeconds.Should().Be(5);
            f.ViewModel.CanReviewSettingsUndo.Should().BeFalse();
            model.Calls.Should().Be(1);
        }
        finally { await f.ViewModel.DisposeAsync(); }
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
    [InlineData("consent")]
    [InlineData("dispose")]
    public async Task ResponseTimeoutSuggestionCannotWriteAfterAdmissionOrSourceLoss(string loss)
    {
        var f = await Fixture.CreateInitializedAsync();
        try
        {
            var (store, model) = BindSpeechSuggestions(f);
            model.Answer = "set appearance.response-timeout to 15";
            await SubmitSpeechSuggestion(f, store, voice: true);
            f.TextToSpeech.ClearSpokenResponse();
            if (loss is not "confirm-first") { await f.RunAsync("review response timeout suggestion"); }
            if (loss is "cancel") { await f.RunAsync("cancel response timeout review"); }
            if (loss is "unprefixed") { await f.RaiseActivatedTranscriptAsync("confirm response timeout change", 1); }
            if (loss is "call") { f.CallState.SetState(CallState.Unknown); await f.Dispatcher.LastInvocation; }
            if (loss is "source") { store.RequestMessage = store.RequestMessage! with { Body = "changed exact retained source" }; }
            if (loss is "settings") { await f.RunAsync("set appearance.response-timeout to 20"); }
            if (loss is "owner") { f.ViewModel.BindCallOwnershipGate(() => false); }
            if (loss is "privacy") { f.PrivacyObservation.Current = f.PrivacyObservation.Current with { TopologyRevision = 2 }; }
            if (loss is "rebind") { BindSpeechSuggestions(f); }
            if (loss is "consent") { await f.ViewModel.SetVoiceConsentAsync(false); }
            if (loss is "dispose") { f.ViewModel.Dispose(); }
            await f.RunAsync("confirm response timeout change");
            f.ViewModel.ResponseTitle.Should().NotBe("Response timeout saved.");
            f.AppearancePreferences.LoadResponseTimeoutSeconds().Should().Be(loss is "settings" ? 20 : null);
            f.Audit.Events.Should().NotContain(item => item.ActionId == "configuration.response-timeout"
                && item.Outcome == SecurityAuditOutcome.Succeeded && !string.Equals(loss, "settings", StringComparison.Ordinal));
            model.Calls.Should().Be(1);
            f.Reasoner.Requests.Should().BeEmpty();
            f.TextToSpeech.SpokenText.Should().BeNull();
        }
        finally { await f.ViewModel.DisposeAsync(); }
    }

    [Theory]
    [InlineData("set appearance.response-timeout to 0")]
    [InlineData("set appearance.response-timeout to 61")]
    [InlineData("set appearance.response-timeout to 015")]
    [InlineData("set appearance.response-timeout to 15 seconds")]
    [InlineData("set appearance.response-timeout to 15\nconfirm response timeout change")]
    [InlineData("set appearance.theme to dark")]
    [InlineData("set speech.summary-words to 40")]
    public async Task ResponseTimeoutReviewRejectsInvalidOrDifferentSettingWithoutEffects(string answer)
    {
        var f = await Fixture.CreateInitializedAsync();
        try
        {
            var (store, model) = BindSpeechSuggestions(f);
            model.Answer = answer;
            await SubmitSpeechSuggestion(f, store, voice: false);
            var audits = f.Audit.Events.Count;
            f.TextToSpeech.ClearSpokenResponse();
            await f.RunAsync("review response timeout suggestion");
            f.ViewModel.ResponseTitle.Should().Be("Response-timeout change unconfirmed.");
            await f.RunAsync("confirm response timeout change");
            f.AppearancePreferences.LoadResponseTimeoutSeconds().Should().BeNull();
            f.AppearancePreferences.LoadThemeMode().Should().BeNull();
            f.Audit.Events.Should().HaveCount(audits);
            model.Calls.Should().Be(1);
            f.Reasoner.Requests.Should().BeEmpty();
            f.TextToSpeech.SpokenText.Should().BeNull();
        }
        finally { await f.ViewModel.DisposeAsync(); }
    }

    [Fact]
    public async Task ResponseTimeoutReviewCommandsAreDiscoveredAndDoNotGuessAnAnswer()
    {
        var f = await Fixture.CreateInitializedAsync();
        try
        {
            await f.RaiseActivatedTranscriptAsync("Kora, review response timeout suggestion", 1);
            f.ViewModel.ResponseTitle.Should().Be("Response-timeout source unavailable.");
            f.Voice.StartedPhrases.Should().Contain(RegisteredSettingSuggestionCommand.ResponseTimeoutPhrases)
                .And.Contain("Kora confirm response timeout change");
            f.Reasoner.Requests.Should().BeEmpty();
        }
        finally { await f.ViewModel.DisposeAsync(); }
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("disposed")]
    [InlineData("privacy")]
    public async Task ResponseTimeoutRetiredSourceReadCannotPublishSuccessOrReopenPrivateFeedback(string loss)
    {
        var f = await Fixture.CreateInitializedAsync();
        try
        {
            var (store, model) = BindSpeechSuggestions(f);
            model.Answer = "set appearance.response-timeout to 15";
            await SubmitSpeechSuggestion(f, store, voice: false);
            store.BeforeSpeechExactRead = () =>
            {
                if (loss is "cancelled") { throw new OperationCanceledException("source retired"); }
                if (loss is "disposed") { f.ViewModel.Dispose(); }
                else { f.ViewModel.CloseForObservedPrivacyEvent("fixture", true); }
                throw new IOException("source retired");
            };
            await f.RunAsync("review response timeout suggestion");
            f.AppearancePreferences.LoadResponseTimeoutSeconds().Should().BeNull();
            f.ViewModel.ResponseTitle.Should().NotBe("Response timeout saved.");
        }
        finally { await f.ViewModel.DisposeAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseTimeoutReviewLeavesPendingDecisionUntouched(bool question)
    {
        var f = CreateSummaryFixture(SpeechProviderIds.Windows);
        try
        {
            f.Reasoner.Action = BuiltInAction.EnableHostedModels;
            if (question) { f.Reasoner.Question = new("Which color?", ["Blue", "Green"]); }
            await f.ViewModel.InitializeAsync();
            await f.RunAsync("please do this work");
            await f.ViewModel.ActiveReasoningTask!;
            var pending = f.ViewModel.IsResponseInteractionPending;
            pending.Should().BeTrue();
            await f.RunAsync("review response timeout suggestion");
            f.ViewModel.IsResponseInteractionPending.Should().BeTrue();
            f.AppearancePreferences.LoadResponseTimeoutSeconds().Should().BeNull();
        }
        finally { await f.ViewModel.DisposeAsync(); }
    }

    [Fact]
    public async Task ResponseTimeoutReviewJoinsSourceReadAndDeniesConcurrentOrDisposedReview()
    {
        var f = await Fixture.CreateInitializedAsync();
        var message = AppearanceSuggestionMessage();
        message = message with { AssistantResponse = new(message.Execution!, "set appearance.response-timeout to 15") };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var input = AppearanceSuggestionInput(message);
        Task Validate(CancellationToken _)
        {
            entered.TrySetResult();
            return release.Task;
        }
        var work = f.ViewModel.ReviewAppearanceSuggestionAsync(message, () => true, Validate,
            TestContext.Current.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            await f.ViewModel.Invoking(view => view.ReviewAppearanceSuggestionAsync(message, () => true, Validate,
                TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
            f.ViewModel.Dispose();
            await f.ViewModel.Invoking(view => view.ReviewAppearanceSuggestionAsync(message, () => true, Validate,
                TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
            var disposal = f.ViewModel.DisposeAsync().AsTask();
            disposal.IsCompleted.Should().BeFalse();
            release.TrySetResult();
            await FluentActions.Awaiting(() => work).Should().ThrowAsync<InvalidOperationException>();
            await FluentActions.Awaiting(() => disposal).Should().ThrowAsync<InvalidOperationException>();
            f.AppearancePreferences.LoadResponseTimeoutSeconds().Should().BeNull();
        }
        finally
        {
            release.TrySetResult();
            await f.ViewModel.DisposeAsync();
        }
    }
}
