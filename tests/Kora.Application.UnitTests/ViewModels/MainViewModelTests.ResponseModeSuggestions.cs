using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Configuration.RegisteredSettings;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false, false, false, ResponseOutputMode.Hybrid)]
    [InlineData(false, true, false, ResponseOutputMode.VoiceOnly)]
    [InlineData(false, false, true, ResponseOutputMode.VisualOnly)]
    [InlineData(true, false, false, ResponseOutputMode.VoiceOnly)]
    [InlineData(true, true, true, ResponseOutputMode.Hybrid)]
    public async Task ResponseModeSuggestionTypedAndActivatedRoutesCompleteExactAuditedPreferenceOnly(
        bool sourceVoice, bool reviewVoice, bool confirmVoice, ResponseOutputMode mode)
    {
        var f = new Fixture(enableResponseModeConfiguration: true);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        var (store, model) = BindSpeechSuggestions(f);
        model.Answer = $"set responses.default-mode to {mode}";
        await SubmitSpeechSuggestion(f, store, sourceVoice);
        var retained = store.RequestMessage;
        f.TextToSpeech.ClearSpokenResponse();
        var before = f.Audit.Events.Count;
        var captures = f.Voice.StartCalls;
        if (reviewVoice) { await f.RaiseActivatedTranscriptAsync("Kora, review response mode suggestion", 1); }
        else { await f.RunAsync("review response mode suggestion"); }
        f.ViewModel.ResponseTitle.Should().Be("Review exact response-mode suggestion.", f.ViewModel.ResponseBody);
        f.ViewModel.ResponseBody.Should().Contain("Current saved: unsaved (default)")
            .And.Contain($"Proposed: {mode}").And.Contain("shared device-local").And.Contain("Mandatory full visual");
        f.OutputPreferences.SavedMode.Should().BeNull();
        f.Audit.Events.Count.Should().Be(before);
        f.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        if (confirmVoice) { await f.RaiseActivatedTranscriptAsync("Kora, confirm response mode change", 1); }
        else { await f.RunAsync("confirm response mode change"); }
        f.ViewModel.ResponseTitle.Should().Be("Response mode saved.", f.ViewModel.ResponseBody);
        f.OutputPreferences.SavedMode.Should().Be(mode);
        f.ViewModel.DefaultResponseMode.Should().Be(mode);
        f.ViewModel.IsVisualResponseVisible.Should().BeTrue("the complete review/readback remains mandatory visual even for VoiceOnly");
        f.Audit.Events.Skip(before).Should().HaveCount(2).And.OnlyContain(item =>
            item.ActionId == "configuration.response-output" && item.Initiator ==
            (sourceVoice || reviewVoice || confirmVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand));
        await f.RunAsync("confirm response mode change");
        f.ViewModel.ResponseTitle.Should().Be("Response-mode source unavailable.");
        model.Calls.Should().Be(1);
        store.RequestMessage.Should().Be(retained);
        f.Reasoner.Requests.Should().BeEmpty();
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.TextToSpeech.InstallProviderCalls.Should().Be(0);
        f.TextToSpeech.RemoveProviderCalls.Should().Be(0);
        f.Voice.StartCalls.Should().Be(captures + (reviewVoice ? 1 : 0) + (confirmVoice ? 1 : 0),
            "only the two explicit activated test inputs restart their pre-existing listening lifecycle");
        f.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
        f.OutputPreferences.SavedMutedOutputVisualFallback.Should().BeNull();
        f.Preferences.SavedVoiceId.Should().BeNull();
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
    [InlineData("consent")]
    [InlineData("dispose")]
    public async Task ResponseModeSuggestionInvalidationCannotRetargetOrLaunderOriginalVoice(string loss)
    {
        var f = new Fixture(enableResponseModeConfiguration: true);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        var (store, model) = BindSpeechSuggestions(f);
        model.Answer = "set responses.default-mode to VoiceOnly";
        await SubmitSpeechSuggestion(f, store, voice: true);
        f.TextToSpeech.ClearSpokenResponse();
        if (loss is not "confirm-first") { await f.RunAsync("review response mode suggestion"); }
        if (loss is "cancel") { await f.RunAsync("cancel response mode review"); }
        if (loss is "unprefixed") { await f.RaiseActivatedTranscriptAsync("confirm response mode change", 1); }
        if (loss is "call") { f.CallState.SetState(CallState.Unknown); await f.Dispatcher.LastInvocation; }
        if (loss is "source") { store.RequestMessage = store.RequestMessage! with { Body = "changed exact retained source" }; }
        if (loss is "settings") { await f.RunAsync("set responses.default-mode to VisualOnly"); }
        if (loss is "owner") { f.ViewModel.BindCallOwnershipGate(() => false); }
        if (loss is "privacy") { f.PrivacyObservation.Current = f.PrivacyObservation.Current with { TopologyRevision = 2 }; }
        if (loss is "rebind") { BindSpeechSuggestions(f); }
        if (loss is "consent") { await f.ViewModel.SetVoiceConsentAsync(false); }
        if (loss is "dispose") { f.ViewModel.Dispose(); }
        await f.RunAsync("confirm response mode change");
        f.ViewModel.ResponseTitle.Should().NotBe("Response mode saved.");
        f.OutputPreferences.SavedMode.Should().Be(loss is "settings" ? ResponseOutputMode.VisualOnly : null);
        f.Audit.Events.Should().NotContain(item => item.ActionId == "configuration.response-output"
            && item.Outcome == SecurityAuditOutcome.Succeeded && !string.Equals(loss, "settings", StringComparison.Ordinal));
        model.Calls.Should().Be(1);
        f.Reasoner.Requests.Should().BeEmpty();
        f.TextToSpeech.SpokenText.Should().BeNull();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseModeSuggestionRequiresBothBindingsAndExactWriterOwnership(bool serviceMissing)
    {
        var f = new Fixture(enableResponseModeConfiguration: !serviceMissing);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        if (serviceMissing) { BindSpeechSuggestions(f); }
        var message = AppearanceSuggestionMessage();
        message = message with { AssistantResponse = new(message.Execution!, "set responses.default-mode to Hybrid") };
        using (AppearanceSuggestionInput(message))
        {
            await f.ViewModel.Invoking(view => view.ReviewResponseModeSuggestionAsync(message, () => true,
                _ => Task.CompletedTask, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        }
        await f.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task ResponseModeSuggestionReservesExactGrammarBeforeModelFallback()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RaiseActivatedTranscriptAsync("Kora, review response mode suggestion", 1);
        f.ViewModel.ResponseTitle.Should().Be("Response-mode source unavailable.");
        f.Voice.StartedPhrases.Should().Contain(RegisteredSettingSuggestionCommand.FixedPhrases)
            .And.Contain(["Kora review response mode suggestion", "Kora confirm response mode change", "Kora cancel response mode review"]);
        f.Reasoner.Requests.Should().BeEmpty();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("noncanonical")]
    [InlineData("foreign-setting")]
    [InlineData("write")]
    [InlineData("audit")]
    [InlineData("audit-denied")]
    [InlineData("source-cancelled")]
    [InlineData("late-dispose")]
    public async Task ResponseModeSuggestionReportsErrorsAndAuditUncertaintyWithoutRetry(string failure)
    {
        var f = new Fixture(enableResponseModeConfiguration: true);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        var (store, model) = BindSpeechSuggestions(f);
        model.Answer = failure switch
        {
            "noncanonical" => "set responses.default-mode to voiceonly",
            "foreign-setting" => "set speech.summary-words to 40",
            _ => "set responses.default-mode to VoiceOnly",
        };
        await SubmitSpeechSuggestion(f, store, voice: false);
        await f.RunAsync("review response mode suggestion");
        if (failure is "write") { f.OutputPreferences.SaveException = new UnauthorizedAccessException("Synthetic denied write"); }
        if (failure is "audit")
        {
            f.Audit.BeforeWrite = item =>
            {
                if (string.Equals(item.ActionId, "configuration.response-output", StringComparison.Ordinal)
                    && item.Outcome == SecurityAuditOutcome.Succeeded)
                { throw new IOException("Synthetic terminal audit failure"); }
            };
        }
        if (failure is "source-cancelled")
        { store.BeforeSpeechExactRead = () => throw new OperationCanceledException("Exact retained source revoked"); }
        if (failure is "audit-denied" or "late-dispose")
        {
            f.Audit.BeforeWrite = item =>
            {
                if (!string.Equals(item.ActionId, "configuration.response-output", StringComparison.Ordinal)
                    || item.Outcome != SecurityAuditOutcome.Requested) { return; }
                if (failure is "audit-denied") { f.CallState.SetState(CallState.Unknown); }
                else { f.ViewModel.Dispose(); throw new IOException("Host retired before audit"); }
            };
        }
        if (failure is not ("noncanonical" or "foreign-setting")) { await f.RunAsync("confirm response mode change"); }
        if (failure is not "late-dispose")
        {
            f.ViewModel.ResponseTitle.Should().Be(failure is "audit-denied" ? "Response mode not confirmed." : "Response-mode change unconfirmed.");
            f.ViewModel.ResponseBody.Should().Contain("a write may have persisted").And.Contain("No automatic retry or rollback");
        }
        f.OutputPreferences.SavedMode.Should().Be(failure is "audit" ? ResponseOutputMode.VoiceOnly : null);
        var count = f.Audit.Events.Count;
        await f.RunAsync("confirm response mode change");
        f.Audit.Events.Count.Should().Be(count);
        model.Calls.Should().Be(1);
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Audit.BeforeWrite = null;
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseModeSuggestionJoinsLateReviewAndConfirmationWithoutRevivingDisposedAuthority(bool confirmation)
    {
        var f = new Fixture(enableResponseModeConfiguration: true);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        BindSpeechSuggestions(f);
        var message = AppearanceSuggestionMessage();
        message = message with { AssistantResponse = new(message.Execution!, "set responses.default-mode to VoiceOnly") };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocking = false;
        Task Validate(CancellationToken _)
        {
            if (!blocking) { return Task.CompletedTask; }
            entered.TrySetResult();
            return release.Task;
        }
        using var input = AppearanceSuggestionInput(message);
        using var review = await f.ViewModel.ReviewResponseModeSuggestionAsync(message, () => true, Validate,
            TestContext.Current.CancellationToken);
        using var confirmInput = AppearanceSuggestionInput(message);
        blocking = true;
        Task work = confirmation
            ? f.ViewModel.ConfirmResponseModeSuggestionAsync(review, TestContext.Current.CancellationToken)
            : f.ViewModel.ReviewResponseModeSuggestionAsync(message, () => true, Validate, TestContext.Current.CancellationToken);
        await entered.Task;
        await f.ViewModel.Invoking(view => view.ReviewResponseModeSuggestionAsync(message, () => true, Validate,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        await f.ViewModel.Invoking(view => view.ConfirmResponseModeSuggestionAsync(review,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        f.ViewModel.Dispose();
        var disposal = f.ViewModel.DisposeAsync().AsTask();
        disposal.IsCompleted.Should().BeFalse();
        release.TrySetResult();
        await FluentActions.Awaiting(() => work).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => disposal).Should().ThrowAsync<InvalidOperationException>();
        f.OutputPreferences.SavedMode.Should().BeNull();
        await f.ViewModel.DisposeAsync();
    }
}
