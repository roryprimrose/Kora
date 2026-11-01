using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false, false, false, -1)]
    [InlineData(true, false, false, -10)]
    [InlineData(false, true, false, 0)]
    [InlineData(false, false, true, 10)]
    [InlineData(true, true, true, -1)]
    public async Task RateSuggestionTypedAndVoiceReviewThenSeparateConfirmationUseTheNativeWriterOnly(
        bool sourceVoice, bool reviewVoice, bool confirmVoice, int rate)
    {
        var f = new Fixture(enableWindowsSpeechRate: true);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        var (store, model) = BindSpeechSuggestions(f);
        model.Answer = "set speech.windows-rate to " + rate.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await SubmitSpeechSuggestion(f, store, sourceVoice);
        var retained = store.RequestMessage;
        f.TextToSpeech.ClearSpokenResponse();
        var before = f.Audit.Events.Count;
        if (reviewVoice) { await f.RaiseActivatedTranscriptAsync("Kora, review rate suggestion", 1); }
        else { await f.RunAsync("review rate suggestion"); }
        f.ViewModel.ResponseTitle.Should().Be("Review exact Windows rate suggestion.", f.ViewModel.ResponseBody);
        f.ViewModel.ResponseBody.Should().Contain("Current desired: 0; effective: 0 (default)")
            .And.Contain($"Proposed: {rate}").And.Contain("shared device-local").And.Contain("Kokoro unchanged");
        f.RatePreferences.Value.Should().BeNull();
        f.Audit.Events.Count.Should().Be(before);
        if (confirmVoice) { await f.RaiseActivatedTranscriptAsync("Kora, confirm rate change", 1); }
        else { await f.RunAsync("confirm rate change"); }
        f.ViewModel.ResponseTitle.Should().Be("Windows rate saved.", f.ViewModel.ResponseBody);
        f.RatePreferences.Value.Should().Be(new WindowsSpeechRate(rate));
        f.ViewModel.SavedWindowsSpeechRate.Should().Be(rate);
        f.RatePreferences.Pending.Should().BeFalse();
        f.ViewModel.CanReviewSettingsUndo.Should().BeFalse("Windows rate is not in the existing undo writer subset");
        f.Audit.Events.Skip(before).Should().HaveCount(2).And.OnlyContain(item =>
            item.ActionId == "configuration.windows-speech-rate" && item.Initiator ==
            (sourceVoice || reviewVoice || confirmVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand));
        await f.RunAsync("confirm rate change");
        f.ViewModel.ResponseTitle.Should().Be("Rate source unavailable.");
        model.Calls.Should().Be(1);
        store.RequestMessage.Should().Be(retained);
        f.Reasoner.Requests.Should().BeEmpty();
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.TextToSpeech.InstallProviderCalls.Should().Be(0);
        f.TextToSpeech.RemoveProviderCalls.Should().Be(0);
        f.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
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
    [InlineData("foreign")]
    [InlineData("write")]
    [InlineData("readback")]
    [InlineData("audit-denied")]
    [InlineData("source-cancelled")]
    [InlineData("late-dispose")]
    public async Task RateSuggestionRefusesStaleSourcesAndReportsFailuresWithoutRetargetOrVoiceLaundering(string loss)
    {
        var f = new Fixture(enableWindowsSpeechRate: true);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        var (store, model) = BindSpeechSuggestions(f);
        model.Answer = loss is "foreign" ? "set speech.playback-volume to 30" : "set speech.windows-rate to -1";
        await SubmitSpeechSuggestion(f, store, voice: true);
        f.TextToSpeech.ClearSpokenResponse();
        if (loss is not "confirm-first") { await f.RunAsync("review rate suggestion"); }
        if (loss is "cancel") { await f.RunAsync("cancel rate review"); }
        if (loss is "unprefixed") { await f.RaiseActivatedTranscriptAsync("confirm rate change", 1); }
        if (loss is "call") { f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; }
        if (loss is "source") { store.RequestMessage = store.RequestMessage! with { Body = "changed exact source" }; }
        if (loss is "settings") { await f.RunAsync("set speech.windows-rate to 5"); }
        if (loss is "owner") { f.ViewModel.BindCallOwnershipGate(() => false); }
        if (loss is "privacy") { f.PrivacyObservation.Current = f.PrivacyObservation.Current with { TopologyRevision = 2 }; }
        if (loss is "rebind") { BindSpeechSuggestions(f); }
        if (loss is "consent") { await f.ViewModel.SetVoiceConsentAsync(false); }
        if (loss is "dispose") { f.ViewModel.Dispose(); }
        if (loss is "write") { f.RatePreferences.WriteFailure = new IOException("write failed"); }
        if (loss is "readback") { f.RatePreferences.AfterWrite = () => f.RatePreferences.Value = new(5); }
        if (loss is "source-cancelled") { store.BeforeSpeechExactRead = () => throw new OperationCanceledException("source retired"); }
        if (loss is "audit-denied" or "late-dispose")
        {
            f.Audit.BeforeWrite = item =>
            {
                if (!string.Equals(item.ActionId, "configuration.windows-speech-rate", StringComparison.Ordinal)
                    || item.Outcome != SecurityAuditOutcome.Requested) { return; }
                if (loss is "audit-denied") { f.ViewModel.BindCallOwnershipGate(() => false); }
                else { f.ViewModel.Dispose(); throw new IOException("retired host"); }
            };
        }
        await f.RunAsync("confirm rate change");
        f.ViewModel.ResponseTitle.Should().NotBe("Windows rate saved.");
        f.RatePreferences.Value.Should().Be(loss is "settings" or "readback" ? new WindowsSpeechRate(5) : null);
        f.Audit.Events.Should().NotContain(item => item.ActionId == "configuration.windows-speech-rate"
            && item.Outcome == SecurityAuditOutcome.Succeeded && loss != "settings");
        model.Calls.Should().Be(1);
        f.Reasoner.Requests.Should().BeEmpty();
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Audit.BeforeWrite = null;
        await f.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task RateSuggestionGrammarIsReservedBeforeModelFallback()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RaiseActivatedTranscriptAsync("Kora, review rate suggestion", 1);
        f.ViewModel.ResponseTitle.Should().Be("Rate source unavailable.");
        f.Voice.StartedPhrases.Should().Contain(WindowsSpeechRateSuggestionCommand.FixedPhrases)
            .And.Contain(["Kora review rate suggestion", "Kora confirm rate change", "Kora cancel rate review"]);
        f.Reasoner.Requests.Should().BeEmpty();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RateReviewAndSaveJoinOwnedWorkAndRefuseOverlappingOrUnboundOperations(bool confirming)
    {
        var f = new Fixture(enableWindowsSpeechRate: true);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        var (store, model) = BindSpeechSuggestions(f, bindSpeech: false);
        model.Answer = "set speech.windows-rate to -1";
        await SubmitSpeechSuggestion(f, store, voice: false);
        var message = store.RequestMessage!;
        using var input = HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        await f.ViewModel.Invoking(vm => vm.ReviewWindowsSpeechRateSuggestionAsync(message, () => true,
            _ => Task.CompletedTask, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        var (nextStore, nextModel) = BindSpeechSuggestions(f);
        nextModel.Answer = model.Answer;
        await SubmitSpeechSuggestion(f, nextStore, voice: false);
        message = nextStore.RequestMessage!;
        using var reviewInput = HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var waiting = false;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Validate(CancellationToken _)
        {
            if (!waiting) { return; }
            entered.TrySetResult();
            await release.Task;
        }
        using var review = await f.ViewModel.ReviewWindowsSpeechRateSuggestionAsync(message, () => true, Validate,
            TestContext.Current.CancellationToken);
        using var confirmInput = HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        waiting = true;
        Task work = confirming ? f.ViewModel.ConfirmWindowsSpeechRateSuggestionAsync(review, TestContext.Current.CancellationToken)
            : f.ViewModel.ReviewWindowsSpeechRateSuggestionAsync(message, () => true, Validate, TestContext.Current.CancellationToken);
        await entered.Task;
        await f.ViewModel.Invoking(vm => vm.ReviewWindowsSpeechRateSuggestionAsync(message, () => true, Validate,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        await f.ViewModel.Invoking(vm => vm.ConfirmWindowsSpeechRateSuggestionAsync(review,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        f.ViewModel.Dispose();
        var disposal = f.ViewModel.DisposeAsync().AsTask();
        work.IsCompleted.Should().BeFalse();
        disposal.IsCompleted.Should().BeFalse();
        release.SetResult();
        await FluentActions.Awaiting(() => work).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => disposal).Should().ThrowAsync<InvalidOperationException>();
        await f.ViewModel.Invoking(vm => vm.ConfirmWindowsSpeechRateSuggestionAsync(review,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        f.RatePreferences.Value.Should().BeNull();
    }
}
