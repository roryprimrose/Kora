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
    [InlineData(false, false, false, 0)]
    [InlineData(false, true, false, 1)]
    [InlineData(false, false, true, 100)]
    [InlineData(true, false, false, 30)]
    [InlineData(true, true, true, 40)]
    public async Task VolumeSuggestionTypedAndVoiceReviewThenFreshConfirmationUseExistingWriterOnly(
        bool sourceVoice, bool reviewVoice, bool confirmVoice, int percent)
    {
        var f = new Fixture(enablePlaybackVolume: true);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        var (store, model) = BindSpeechSuggestions(f);
        model.Answer = "set speech.playback-volume to " + percent.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await SubmitSpeechSuggestion(f, store, sourceVoice);
        var retained = store.RequestMessage;
        f.TextToSpeech.ClearSpokenResponse();
        var before = f.Audit.Events.Count;
        if (reviewVoice) { await f.RaiseActivatedTranscriptAsync("Kora, review volume suggestion", 1); }
        else { await f.RunAsync("review volume suggestion"); }
        f.ViewModel.ResponseTitle.Should().Be("Review exact volume suggestion.", f.ViewModel.ResponseBody);
        f.ViewModel.ResponseBody.Should().Contain("Current desired: 100%; effective: 100% (default)")
            .And.Contain($"Proposed: {percent}%").And.Contain("shared device-local").And.Contain("never synthesized or replayed");
        f.VolumePreferences.Value.Should().BeNull();
        f.Audit.Events.Count.Should().Be(before);
        if (confirmVoice) { await f.RaiseActivatedTranscriptAsync("Kora, confirm volume change", 1); }
        else { await f.RunAsync("confirm volume change"); }
        f.ViewModel.ResponseTitle.Should().Be("Kora volume saved.", f.ViewModel.ResponseBody);
        f.VolumePreferences.Value.Should().Be(new PlaybackVolume(percent));
        f.ViewModel.SavedPlaybackVolume.Should().Be(percent);
        f.VolumePreferences.Pending.Should().BeFalse();
        f.Audit.Events.Skip(before).Should().HaveCount(2).And.OnlyContain(item =>
            item.ActionId == "configuration.playback-volume" && item.Initiator ==
            (sourceVoice || reviewVoice || confirmVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand));
        await f.RunAsync("confirm volume change");
        f.ViewModel.ResponseTitle.Should().Be("Volume source unavailable.");
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
    public async Task VolumeSuggestionInvalidationCannotRetargetOrLaunderOriginalVoice(string loss)
    {
        var f = new Fixture(enablePlaybackVolume: true);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        var (store, model) = BindSpeechSuggestions(f);
        model.Answer = "set speech.playback-volume to 30";
        await SubmitSpeechSuggestion(f, store, voice: true);
        f.TextToSpeech.ClearSpokenResponse();
        if (loss is not "confirm-first") { await f.RunAsync("review volume suggestion"); }
        if (loss is "cancel") { await f.RunAsync("cancel volume review"); }
        if (loss is "unprefixed") { await f.RaiseActivatedTranscriptAsync("confirm volume change", 1); }
        if (loss is "call") { f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; }
        if (loss is "source") { store.RequestMessage = store.RequestMessage! with { Body = "different retained source" }; }
        if (loss is "settings") { await f.RunAsync("set speech.playback-volume to 50"); }
        if (loss is "owner") { f.ViewModel.BindCallOwnershipGate(() => false); }
        if (loss is "privacy") { f.PrivacyObservation.Current = f.PrivacyObservation.Current with { TopologyRevision = 2 }; }
        if (loss is "rebind") { BindSpeechSuggestions(f); }
        if (loss is "consent") { await f.ViewModel.SetVoiceConsentAsync(false); }
        if (loss is "dispose") { f.ViewModel.Dispose(); }
        await f.RunAsync("confirm volume change");
        f.ViewModel.ResponseTitle.Should().NotBe("Kora volume saved.");
        f.VolumePreferences.Value.Should().Be(loss is "settings" ? new PlaybackVolume(50) : null);
        f.Audit.Events.Should().NotContain(item => item.ActionId == "configuration.playback-volume"
            && item.Outcome == SecurityAuditOutcome.Succeeded && !string.Equals(loss, "settings", StringComparison.Ordinal));
        model.Calls.Should().Be(1);
        f.Reasoner.Requests.Should().BeEmpty();
        f.TextToSpeech.SpokenText.Should().BeNull();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("review volume suggestion", PlaybackVolumeSuggestionOperation.Review)]
    [InlineData("Nova, confirm volume change", PlaybackVolumeSuggestionOperation.Confirm)]
    [InlineData("Nova cancel volume review", PlaybackVolumeSuggestionOperation.Cancel)]
    [InlineData("confirm volume change now", null)]
    public void VolumeSuggestionCommandsAreExactAndCurrentNameAware(string text, PlaybackVolumeSuggestionOperation? expected) =>
        PlaybackVolumeSuggestionCommand.Parse(text, "Nova").Should().Be(expected);

    [Fact]
    public async Task VolumeSuggestionListeningGrammarIsReservedBeforeAnyModelFallback()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RaiseActivatedTranscriptAsync("Kora, review volume suggestion", 1);
        f.ViewModel.ResponseTitle.Should().Be("Volume source unavailable.");
        f.Voice.StartedPhrases.Should().Contain(PlaybackVolumeSuggestionCommand.FixedPhrases)
            .And.Contain(["Kora review volume suggestion", "Kora confirm volume change", "Kora cancel volume review"]);
        f.Reasoner.Requests.Should().BeEmpty();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("noncanonical")]
    [InlineData("foreign")]
    [InlineData("range")]
    [InlineData("write")]
    [InlineData("readback")]
    [InlineData("audit-denied")]
    [InlineData("source-cancelled")]
    [InlineData("late-dispose")]
    public async Task VolumeSuggestionCommandReportsFailuresAndDenialsWithoutRetryOrLatePrivateFeedback(string failure)
    {
        var f = new Fixture(enablePlaybackVolume: true);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        var (store, model) = BindSpeechSuggestions(f);
        model.Answer = failure switch
        {
            "noncanonical" => "set speech.playback-volume to 030",
            "foreign" => "set speech.windows-rate to 1",
            "range" => "set speech.playback-volume to 101",
            _ => "set speech.playback-volume to 30",
        };
        await SubmitSpeechSuggestion(f, store, voice: false);
        await f.RunAsync("review volume suggestion");
        if (failure is "write") { f.VolumePreferences.WriteFailure = new UnauthorizedAccessException("write denied"); }
        if (failure is "readback") { f.VolumePreferences.AfterWrite = () => f.VolumePreferences.Value = new(50); }
        if (failure is "source-cancelled") { store.BeforeSpeechExactRead = () => throw new OperationCanceledException("source retired"); }
        if (failure is "audit-denied" or "late-dispose")
        {
            f.Audit.BeforeWrite = item =>
            {
                if (!string.Equals(item.ActionId, "configuration.playback-volume", StringComparison.Ordinal)
                    || item.Outcome != SecurityAuditOutcome.Requested) { return; }
                if (failure is "audit-denied") { f.ViewModel.BindCallOwnershipGate(() => false); }
                else { f.ViewModel.Dispose(); throw new IOException("retired host"); }
            };
        }
        await f.RunAsync("confirm volume change");
        f.ViewModel.ResponseTitle.Should().NotBe("Kora volume saved.");
        f.Audit.Events.Should().NotContain(item => item.ActionId == "configuration.playback-volume"
            && item.Outcome == SecurityAuditOutcome.Succeeded);
        model.Calls.Should().Be(1);
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Audit.BeforeWrite = null;
        await f.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task VolumeSuggestionPublicReviewAndConfirmationRejectUnboundBusyAndDisposedHosts()
    {
        var f = new Fixture(enablePlaybackVolume: true);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        var (store, model) = BindSpeechSuggestions(f, bindSpeech: false);
        model.Answer = "set speech.playback-volume to 30";
        await SubmitSpeechSuggestion(f, store, voice: false);
        var message = store.RequestMessage!;
        using var input = HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        await f.ViewModel.Invoking(vm => vm.ReviewPlaybackVolumeSuggestionAsync(message, () => true,
            _ => Task.CompletedTask, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        var (nextStore, nextModel) = BindSpeechSuggestions(f);
        nextModel.Answer = model.Answer;
        await SubmitSpeechSuggestion(f, nextStore, voice: false);
        message = nextStore.RequestMessage!;
        using var reviewInput = HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var confirming = false;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Validate(CancellationToken _)
        {
            if (!confirming) { return; }
            entered.TrySetResult();
            await release.Task;
        }
        using var review = await f.ViewModel.ReviewPlaybackVolumeSuggestionAsync(message, () => true, Validate,
            TestContext.Current.CancellationToken);
        using var confirmInput = HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        confirming = true;
        var work = f.ViewModel.ConfirmPlaybackVolumeSuggestionAsync(review, TestContext.Current.CancellationToken);
        await entered.Task;
        await f.ViewModel.Invoking(vm => vm.ReviewPlaybackVolumeSuggestionAsync(message, () => true, Validate,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        await f.ViewModel.Invoking(vm => vm.ConfirmPlaybackVolumeSuggestionAsync(review,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        f.ViewModel.Dispose();
        var disposal = f.ViewModel.DisposeAsync().AsTask();
        work.IsCompleted.Should().BeFalse();
        disposal.IsCompleted.Should().BeFalse("owned persistence must join even a cancellation-insensitive exact-source callback");
        release.SetResult();
        await FluentActions.Awaiting(() => work).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => disposal).Should().ThrowAsync<InvalidOperationException>();
        await f.ViewModel.Invoking(vm => vm.ConfirmPlaybackVolumeSuggestionAsync(review,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        f.VolumePreferences.Value.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VolumeSuggestionJoinsPendingReviewAndCannotBypassAnActiveNativeVolumeOperation(bool direct)
    {
        var f = new Fixture(enablePlaybackVolume: true);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        var (store, model) = BindSpeechSuggestions(f);
        model.Answer = "set speech.playback-volume to 30";
        await SubmitSpeechSuggestion(f, store, voice: false);
        var message = store.RequestMessage!;
        using var input = HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        using var review = await f.ViewModel.ReviewPlaybackVolumeSuggestionAsync(message, () => true,
            _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var creation = new TaskCompletionSource<Kora.Core.Authorization.WorkSessionAuthorization>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task work;
        if (direct)
        {
            f.ManualCallStore.PendingCreation = creation.Task;
            work = f.ViewModel.ExecutePlaybackVolumeCommandAsync(new(AppearanceCommandOperation.Set, "50"),
                SecurityAuditInitiator.LocalUser, TestContext.Current.CancellationToken);
        }
        else
        {
            work = f.ViewModel.ReviewPlaybackVolumeSuggestionAsync(message, () => true,
                _ => release.Task, TestContext.Current.CancellationToken);
        }
        work.IsCompleted.Should().BeFalse();
        await f.ViewModel.Invoking(vm => vm.ReviewPlaybackVolumeSuggestionAsync(message, () => true,
            _ => Task.CompletedTask, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        await f.ViewModel.Invoking(vm => vm.ConfirmPlaybackVolumeSuggestionAsync(review,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        if (direct)
        {
            creation.SetResult(f.ManualCallStore.Authority!);
            await work;
            f.VolumePreferences.Value.Should().Be(new PlaybackVolume(50));
            await f.ViewModel.DisposeAsync();
        }
        else
        {
            f.ViewModel.Dispose();
            var disposal = f.ViewModel.DisposeAsync().AsTask();
            disposal.IsCompleted.Should().BeFalse();
            release.SetResult();
            await FluentActions.Awaiting(() => work).Should().ThrowAsync<InvalidOperationException>();
            await FluentActions.Awaiting(() => disposal).Should().ThrowAsync<InvalidOperationException>();
            f.VolumePreferences.Value.Should().BeNull();
        }
    }
}
