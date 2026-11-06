using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private static void AddInstalledSuggestionVoice(Fixture f)
    {
        f.TextToSpeech.Providers = f.TextToSpeech.Providers.Append(
            new(SpeechProviderIds.Kokoro, "Kokoro", "Installed", true, false, 1, null)).ToArray();
        f.TextToSpeech.Voices = f.TextToSpeech.Voices.Append(
            new SpeechVoice("female", "Installed Kokoro voice", "en-US", SpeechVoiceGender.Female)
            { ProviderId = SpeechProviderIds.Kokoro }).ToArray();
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public async Task InstalledVoiceTypedAndActivatedVoiceConfirmExactPairAndOnlyNextIndependentSpeechUsesIt(
        bool sourceVoice, bool reviewVoice, bool confirmVoice)
    {
        var f = new Fixture();
        AddInstalledSuggestionVoice(f);
        await f.ViewModel.InitializeAsync();
        var (store, model) = BindSpeechSuggestions(f);
        model.Answer = "set speech.voice to kokoro / female";
        await SubmitSpeechSuggestion(f, store, sourceVoice);
        var retained = store.RequestMessage;
        var before = f.Audit.Events.Count;
        var spoken = f.TextToSpeech.SpeakCalls;
        f.TextToSpeech.ClearSpokenResponse();
        if (reviewVoice) { await f.RaiseActivatedTranscriptAsync("Kora, review installed voice suggestion", 1); }
        else { await f.RunAsync("review installed voice suggestion"); }
        f.ViewModel.ResponseTitle.Should().Be("Review exact installed voice suggestion.", f.ViewModel.ResponseBody);
        f.ViewModel.ResponseBody.Should().Contain("Current provider: windows-sapi; voice: provider default (default)")
            .And.Contain("Proposed provider: kokoro; voice: female").And.Contain("shared device-local")
            .And.Contain("exact successful retained answer").And.Contain("NEXT separately requested");
        f.Preferences.LoadSelection().Should().BeNull();
        f.Audit.Events.Count.Should().Be(before);
        if (confirmVoice) { await f.RaiseActivatedTranscriptAsync("Kora, confirm installed voice change", 1); }
        else { await f.RunAsync("confirm installed voice change"); }
        f.ViewModel.ResponseTitle.Should().Be("Installed voice saved.", f.ViewModel.ResponseBody);
        var expected = new SpeechSelection(SpeechProviderIds.Kokoro, "female");
        f.Preferences.LoadSelection().Should().Be(expected);
        f.SpeechConfiguration.Get().Selection.Should().Be(expected);
        f.ViewModel.SelectedVoice!.ConfigurationId.Should().Be("kokoro / female");
        f.Audit.Events.Skip(before).Should().HaveCount(2).And.OnlyContain(item =>
            item.ActionId == SpeechOptionRegistry.Get(SpeechOption.Voice).AuditAction && item.Initiator ==
            (sourceVoice || reviewVoice || confirmVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand));
        f.TextToSpeech.SpeakCalls.Should().Be(spoken);
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.TextToSpeech.InstallProviderCalls.Should().Be(0);
        f.TextToSpeech.RemoveProviderCalls.Should().Be(0);
        f.Preferences.SummaryLimits.Should().BeNull();
        f.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
        f.ViewModel.CanReviewSettingsUndo.Should().BeFalse();
        await f.RunAsync("confirm installed voice change");
        f.ViewModel.ResponseTitle.Should().Be("Installed voice source unavailable.");
        await f.RunAsync("what power action is pending");
        f.TextToSpeech.SpeakCalls.Should().Be(spoken + 1);
        f.TextToSpeech.SpokenVoice!.ConfigurationId.Should().Be("kokoro / female");
        model.Calls.Should().Be(1);
        store.RequestMessage.Should().Be(retained);
        f.Reasoner.Requests.Should().BeEmpty();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("confirm-first")]
    [InlineData("unprefixed")]
    [InlineData("call")]
    [InlineData("source")]
    [InlineData("deleted-source")]
    [InlineData("done")]
    [InlineData("generation")]
    [InlineData("settings")]
    [InlineData("catalogue")]
    [InlineData("owner")]
    [InlineData("privacy")]
    [InlineData("rebind")]
    [InlineData("consent")]
    [InlineData("dispose")]
    [InlineData("foreign")]
    [InlineData("write")]
    [InlineData("source-cancelled")]
    [InlineData("late-owner")]
    [InlineData("late-lock")]
    public async Task InstalledVoiceCommandsRejectRetiredOriginalSourcesAndNeverFallbackOrReplay(string loss)
    {
        var f = new Fixture();
        AddInstalledSuggestionVoice(f);
        await f.ViewModel.InitializeAsync();
        var (store, model) = BindSpeechSuggestions(f);
        model.Answer = string.Equals(loss, "foreign", StringComparison.Ordinal) ? "set speech.provider to kokoro" : "set speech.voice to kokoro / female";
        await SubmitSpeechSuggestion(f, store, voice: true);
        f.TextToSpeech.ClearSpokenResponse();
        var spoken = f.TextToSpeech.SpeakCalls;
        if (!string.Equals(loss, "confirm-first", StringComparison.Ordinal)) { await f.RunAsync("review installed voice suggestion"); }
        if (string.Equals(loss, "cancel", StringComparison.Ordinal)) { await f.RunAsync("cancel installed voice review"); }
        if (string.Equals(loss, "unprefixed", StringComparison.Ordinal)) { await f.RaiseActivatedTranscriptAsync("confirm installed voice change", 1); }
        if (string.Equals(loss, "call", StringComparison.Ordinal)) { f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; }
        if (string.Equals(loss, "source", StringComparison.Ordinal)) { store.RequestMessage = store.RequestMessage! with { Body = "changed exact source" }; }
        if (string.Equals(loss, "deleted-source", StringComparison.Ordinal)) { store.RequestMessage = null; }
        if (loss is "done" or "generation") { store.ChangeSpeechTarget(loss); }
        if (string.Equals(loss, "settings", StringComparison.Ordinal)) { f.Preferences.ProviderId = SpeechProviderIds.Windows; f.Preferences.VoiceId = "female"; }
        if (string.Equals(loss, "catalogue", StringComparison.Ordinal)) { f.TextToSpeech.Voices = f.TextToSpeech.Voices.Take(1).ToArray(); }
        if (string.Equals(loss, "owner", StringComparison.Ordinal)) { f.ViewModel.BindCallOwnershipGate(() => false); }
        if (string.Equals(loss, "privacy", StringComparison.Ordinal)) { f.PrivacyObservation.Current = f.PrivacyObservation.Current with { TopologyRevision = 2 }; }
        if (string.Equals(loss, "rebind", StringComparison.Ordinal)) { BindSpeechSuggestions(f); }
        if (string.Equals(loss, "consent", StringComparison.Ordinal)) { await f.ViewModel.SetVoiceConsentAsync(false); }
        if (string.Equals(loss, "dispose", StringComparison.Ordinal)) { f.ViewModel.Dispose(); }
        if (string.Equals(loss, "write", StringComparison.Ordinal)) { f.Preferences.SaveException = new IOException("disk"); }
        if (string.Equals(loss, "source-cancelled", StringComparison.Ordinal)) { store.BeforeSpeechExactRead = () => throw new OperationCanceledException("source retired"); }
        if (loss is "late-owner" or "late-lock")
        {
            f.Audit.BeforeWrite = item =>
            {
                if (string.Equals(item.ActionId, SpeechOptionRegistry.Get(SpeechOption.Voice).AuditAction, StringComparison.Ordinal) && item.Outcome == SecurityAuditOutcome.Requested)
                {
                    if (loss is "late-lock") { f.Session.IsUnlocked = false; }
                    else { f.ViewModel.BindCallOwnershipGate(() => false); }
                }
            };
        }
        await f.RunAsync("confirm installed voice change");
        f.ViewModel.ResponseTitle.Should().NotBe("Installed voice saved.");
        f.Preferences.SavedProviderId.Should().BeNull();
        f.Preferences.SavedVoiceId.Should().BeNull();
        f.Audit.Events.Should().NotContain(item => item.ActionId == SpeechOptionRegistry.Get(SpeechOption.Voice).AuditAction
            && item.Outcome == SecurityAuditOutcome.Succeeded);
        model.Calls.Should().Be(1);
        f.Reasoner.Requests.Should().BeEmpty();
        f.TextToSpeech.SpeakCalls.Should().Be(spoken);
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.TextToSpeech.InstallProviderCalls.Should().Be(0);
        f.TextToSpeech.RemoveProviderCalls.Should().Be(0);
        f.Audit.BeforeWrite = null;
        await f.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task InstalledVoiceReviewGrammarIsReservedWithoutModelFallback()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RaiseActivatedTranscriptAsync("Kora, review installed voice suggestion", 1);
        f.ViewModel.ResponseTitle.Should().Be("Installed voice source unavailable.");
        f.Voice.StartedPhrases.Should().Contain(InstalledSpeechVoiceSuggestionCommand.FixedPhrases)
            .And.Contain(["Kora review installed voice suggestion", "Kora confirm installed voice change", "Kora cancel installed voice review"]);
        f.Reasoner.Requests.Should().BeEmpty();
        await f.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task InstalledVoiceReviewNeverAnswersOrReplacesAnExistingPendingQuestion()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        f.Reasoner.Question = new("Which target?", ["One", "Two"]);
        await f.ViewModel.InitializeAsync();
        BindSpeechSuggestions(f);
        await f.RunAsync("choose");
        await f.ViewModel.ActiveReasoningTask!;
        await f.RunAsync("review installed voice suggestion");
        f.ViewModel.ResponseTitle.Should().Be("Installed voice review denied.");
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        f.Preferences.SavedProviderId.Should().BeNull();
        f.TextToSpeech.InstallProviderCalls.Should().Be(0);
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstalledVoiceReviewAndConfirmationJoinDelayedSourceWorkAndRefuseOverlappingOperations(bool confirming)
    {
        var f = new Fixture();
        AddInstalledSuggestionVoice(f);
        await f.ViewModel.InitializeAsync();
        var (store, model) = BindSpeechSuggestions(f, bindSpeech: false);
        model.Answer = "set speech.voice to kokoro / female";
        await SubmitSpeechSuggestion(f, store, voice: false);
        var message = store.RequestMessage!;
        using var input = HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        await f.ViewModel.Invoking(vm => vm.ReviewInstalledSpeechVoiceSuggestionAsync(message, () => true,
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
        using var review = await f.ViewModel.ReviewInstalledSpeechVoiceSuggestionAsync(message, () => true, Validate,
            TestContext.Current.CancellationToken);
        using var confirmInput = HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        waiting = true;
        Task work = confirming ? f.ViewModel.ConfirmInstalledSpeechVoiceSuggestionAsync(review, TestContext.Current.CancellationToken)
            : f.ViewModel.ReviewInstalledSpeechVoiceSuggestionAsync(message, () => true, Validate, TestContext.Current.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            await f.ViewModel.Invoking(vm => vm.ReviewInstalledSpeechVoiceSuggestionAsync(message, () => true, Validate,
                TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
            await f.ViewModel.Invoking(vm => vm.ConfirmInstalledSpeechVoiceSuggestionAsync(review,
                TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
            f.ViewModel.Dispose();
            var disposal = f.ViewModel.DisposeAsync().AsTask();
            work.IsCompleted.Should().BeFalse();
            disposal.IsCompleted.Should().BeFalse();
            release.TrySetResult();
            await FluentActions.Awaiting(() => work).Should().ThrowAsync<InvalidOperationException>();
            await FluentActions.Awaiting(() => disposal).Should().ThrowAsync<InvalidOperationException>();
            await f.ViewModel.Invoking(vm => vm.ConfirmInstalledSpeechVoiceSuggestionAsync(review,
                TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
            f.Preferences.SavedVoiceId.Should().BeNull();
        }
        finally { release.TrySetResult(); }
    }
}
