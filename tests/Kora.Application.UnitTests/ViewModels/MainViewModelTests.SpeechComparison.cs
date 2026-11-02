using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Application.Configuration;
using Kora.Application.Configuration.RegisteredSettings;
using Kora.Application.Hosting;
using Kora.Application.UnitTests.Interaction;
using Kora.Application.UnitTests.Voice;
using Kora.Application.Voice;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Platform;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task SpeechComparisonMissingRuntimeIsExplicitAndPresentationPropertiesRemainUnavailable()
    {
        var f = new Fixture();
        f.ViewModel.SpeechComparisonPhrase.Should().BeEmpty();
        f.ViewModel.SpeechComparisonPhrase = "ignored";
        f.ViewModel.SpeechComparisonStatus.Should().Contain("unavailable");
        f.ViewModel.SpeechComparisonProfiles.Should().BeEmpty();
        f.ViewModel.SpeechComparisonResults.Should().BeEmpty();
        f.ViewModel.CanTestSpeechComparison.Should().BeFalse();
        f.ViewModel.IsSpeechComparisonRunning.Should().BeFalse();
        await FluentActions.Awaiting(() => f.ViewModel.ReviewSpeechComparisonCommand.ExecuteAsync()).Should().ThrowAsync<InvalidOperationException>();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("name")]
    [InlineData("presenter")]
    [InlineData("microphone")]
    public async Task SpeechComparisonRejectsUnavailableOriginalAdmissionBeforeCapture(string reason)
    {
        var runtime = new SpeechSetupComparisonTests.Runtime();
        await using var comparison = new SpeechSetupComparison(runtime, runtime);
        var f = new Fixture(speechSetupComparison: comparison);
        f.VoiceConsent.Consent = true;
        if (!string.Equals(reason, "microphone", StringComparison.Ordinal))
        {
            f.Voice.Microphones = [new("mic", "Headset")];
            f.Voice.DefaultMicrophoneId = "mic";
        }
        if (string.Equals(reason, "name", StringComparison.Ordinal)) { f.NamePreferences.Name = "Supported Commands"; }
        await f.ViewModel.InitializeAsync();
        f.ViewModel.BindSpeechInputNativeLifetime(static () => true);
        f.ViewModel.SpeechComparisonPhrase.Should().Contain("quiet garden");
        if (string.Equals(reason, "presenter", StringComparison.Ordinal))
        {
            await FluentActions.Awaiting(() => f.RunAsync("compare speech input")).Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            using var root = HostActivity.BeginRoot(
                HostRequest.Create(string.Equals(reason, "origin", StringComparison.Ordinal) ? RequestOrigin.HostSystem : RequestOrigin.LocalUi),
                HostActivityLayer.Application, HostOperation.Request);
            await FluentActions.Awaiting(() => f.ViewModel.ReviewSpeechComparisonCommand.ExecuteAsync()).Should().ThrowAsync<InvalidOperationException>();
        }
        runtime.Captures.Should().Be(0);
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("native", false)]
    [InlineData("typed", false)]
    [InlineData("activated", false)]
    [InlineData("native", true)]
    [InlineData("typed", true)]
    [InlineData("activated", true)]
    public async Task SpeechComparisonNativeAndTypedRoutesExecuteSameInertOperationWithoutPreferenceOrRoutingEffects(
        string route, bool enableConversationExecution)
    {
        var runtime = new SpeechSetupComparisonTests.Runtime();
        await using var comparison = new SpeechSetupComparison(runtime, runtime);
        var f = new Fixture(speechSetupComparison: comparison, enableConversationExecution: enableConversationExecution);
        f.VoiceConsent.Consent = true;
        f.Voice.Microphones = [new("mic", "Headset")];
        f.Voice.DefaultMicrophoneId = "mic";
        await f.ViewModel.InitializeAsync();
        f.ViewModel.BindSpeechInputNativeLifetime(static () => true);
        var navigation = new SettingsNavigationFake();
        f.ViewModel.BindSettingsSectionNavigation(navigation);
        if (enableConversationExecution)
        {
            await f.RunAsync("review conversations.execution-maximum to 1");
            f.ViewModel.CanConfirmConversationExecution.Should().BeTrue();
        }
        var listening = f.ViewModel.IsVoiceEnabled;
        var preference = f.AudioPreferences.MicrophoneId;
        var audits = f.Audit.Events.Count;
        var phrase = enableConversationExecution ? "Kora confirm conversations.execution-maximum" : "Kora confirm delete";
        f.ViewModel.SpeechComparisonPhrase = phrase;
        runtime.Capture = (_, _, _) => Task.FromResult(new Kora.Core.Voice.SpeechComparisonObservation(
            phrase, TimeSpan.FromMilliseconds(10)));
        if (string.Equals(route, "typed", StringComparison.Ordinal)) { await f.RunAsync("compare speech input"); }
        else if (string.Equals(route, "activated", StringComparison.Ordinal))
        { await f.RaiseActivatedTranscriptAsync("Kora compare speech input", 1); }
        else { await f.ViewModel.ReviewSpeechComparisonCommand.ExecuteAsync(); }
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        f.ViewModel.SpeechComparisonProfiles.Should().Contain("Installed test profile");
        f.ViewModel.SpeechComparisonDisclosure.Should().Contain("Nothing is executed");
        f.ViewModel.CanTestSpeechComparison.Should().BeTrue(f.ViewModel.SpeechComparisonStatus);
        f.ViewModel.IsSpeechComparisonRunning.Should().BeFalse();
        if (string.Equals(route, "typed", StringComparison.Ordinal)) { await f.RunAsync("test speech comparison"); }
        else if (string.Equals(route, "activated", StringComparison.Ordinal))
        {
            await f.ViewModel.BeginPushToTalkAsync();
            await f.ViewModel.EndPushToTalkAsync();
            await f.Voice.RaiseTranscriptAsync("Kora test speech comparison", 1);
        }
        else { await f.ViewModel.TestSpeechComparisonCommand.ExecuteAsync(); }
        f.ViewModel.SpeechComparisonResults.Should().Contain("correct").And.Contain("10.0 ms");
        f.ViewModel.SpeechComparisonStatus.Should().Contain("complete");
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.Voice.StartCalls.Should().Be(string.Equals(route, "activated", StringComparison.Ordinal) ? 2 : 0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.AudioPreferences.MicrophoneId.Should().Be(preference);
        f.ViewModel.IsVoiceEnabled.Should().Be(listening);
        f.VoiceConsent.Consent.Should().BeTrue();
        f.Audit.Events.Count.Should().Be(audits);
        f.ConversationExecutionPreferences.Writes.Should().Be(0);
        if (!string.Equals(route, "native", StringComparison.Ordinal))
        { navigation.Sections.Should().Equal(SettingsSection.SpeechAudio); await f.RunAsync("cancel speech comparison"); }
        else { await f.ViewModel.CancelSpeechComparisonCommand.ExecuteAsync(); }
        f.ViewModel.SpeechComparisonResults.Should().BeEmpty();
        if (enableConversationExecution)
        {
            f.ViewModel.CanConfirmConversationExecution.Should().BeTrue();
            await f.ViewModel.ConfirmConversationExecutionCommand.ExecuteAsync();
            f.ConversationExecutionPreferences.Value!.Value.Should().Be(1);
            f.ConversationExecutionPreferences.Writes.Should().Be(1);
            f.Reasoner.Requests.Should().BeEmpty();
            f.ViewModel.IsVoiceEnabled.Should().Be(listening);
        }
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("appearance", false, false)]
    [InlineData("appearance", false, true)]
    [InlineData("appearance", true, false)]
    [InlineData("appearance", true, true)]
    [InlineData("speech", false, false)]
    [InlineData("speech", false, true)]
    [InlineData("speech", true, false)]
    [InlineData("speech", true, true)]
    [InlineData("volume", false, false)]
    [InlineData("volume", false, true)]
    [InlineData("volume", true, false)]
    [InlineData("volume", true, true)]
    [InlineData("response", false, false)]
    [InlineData("response", false, true)]
    [InlineData("response", true, false)]
    [InlineData("response", true, true)]
    public async Task SpeechComparisonSettingsMutationAndUndoRetireResultsWithoutListeningOrReplay(string setting, bool undo, bool running)
    {
        var runtime = new SpeechSetupComparisonTests.Runtime();
        await using var comparison = new SpeechSetupComparison(runtime, runtime);
        var f = new Fixture(speechSetupComparison: comparison, enablePlaybackVolume: true, enableResponseModeConfiguration: true);
        await using var admission = f.OutputAdmission;
        await using var model = f.ViewModel;
        f.VoiceConsent.Consent = true;
        f.Voice.Microphones = [new("mic", "Headset")];
        f.Voice.DefaultMicrophoneId = "mic";
        await model.InitializeAsync();
        if (model.IsVoiceEnabled) { await model.ToggleListeningCommand.ExecuteAsync(); }
        await model.RefreshResponseModeCommand.ExecuteAsync();
        model.BindSpeechInputNativeLifetime(static () => true);
        Task NativeUndo(RegisteredSettingSuggestionOperation operation) => HostRequestRunner.RunAsync(RequestOrigin.LocalUi,
            () => model.ExecuteSettingsUndoAsync(operation, SecurityAuditInitiator.LocalUser,
                TestContext.Current.CancellationToken, static () => true));
        async Task Mutate()
        {
            switch (setting)
            {
                case "appearance":
                    model.ThemeMode = ApplicationThemeMode.Dark;
                    await model.AppearanceUpdate;
                    break;
                case "speech": model.SelectedSummaryWords = 40; break;
                case "volume":
                    model.SelectedPlaybackVolume = 30;
                    await model.SavePlaybackVolumeCommand.ExecuteAsync();
                    break;
                default:
                    model.SelectedResponseModeChoice = model.ResponseModeChoices.Single(choice => choice.Mode == ResponseOutputMode.VisualOnly);
                    await model.SaveResponseModeCommand.ExecuteAsync();
                    break;
            }
        }
        if (undo)
        {
            await Mutate();
            await NativeUndo(RegisteredSettingSuggestionOperation.Review);
        }
        await model.ReviewSpeechComparisonCommand.ExecuteAsync();
        await model.TestSpeechComparisonCommand.ExecuteAsync();
        model.SpeechComparisonResults.Should().NotBeEmpty();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<SpeechComparisonObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken captureToken = default;
        var work = Task.CompletedTask;
        if (running)
        {
            runtime.Capture = (_, _, token) => { captureToken = token; entered.TrySetResult(); return release.Task; };
            await model.ReviewSpeechComparisonCommand.ExecuteAsync();
            work = model.TestSpeechComparisonCommand.ExecuteAsync();
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        }
        var starts = f.Voice.StartCalls;
        f.TextToSpeech.ClearSpokenResponse();
        var cancelled = false;
        var joinedEarly = false;
        try
        {
            if (undo) { await NativeUndo(RegisteredSettingSuggestionOperation.Confirm); }
            else { await Mutate(); }
            cancelled = captureToken.IsCancellationRequested;
            joinedEarly = work.IsCompleted;
        }
        finally
        {
            release.TrySetResult(new("Late inert result", TimeSpan.FromMilliseconds(10)));
            await work;
        }
        if (running)
        {
            cancelled.Should().BeTrue();
            joinedEarly.Should().BeFalse();
        }
        model.SpeechComparisonResults.Should().BeEmpty();
        model.CanTestSpeechComparison.Should().BeFalse();
        if (string.Equals(setting, "appearance", StringComparison.Ordinal))
        { f.AppearancePreferences.Mode.Should().Be(undo ? null : ApplicationThemeMode.Dark); }
        else if (string.Equals(setting, "speech", StringComparison.Ordinal))
        { f.Preferences.SummaryLimits.Should().Be(undo ? null : new SpokenSummaryLimits(3, 40)); }
        else if (string.Equals(setting, "volume", StringComparison.Ordinal))
        { f.VolumePreferences.Value.Should().Be(undo ? null : new PlaybackVolume(30)); }
        else { f.OutputPreferences.Mode.Should().Be(undo ? null : ResponseOutputMode.VisualOnly); }
        f.Voice.StartCalls.Should().Be(starts);
        model.IsVoiceEnabled.Should().BeFalse();
        model.HasVoiceConsent.Should().BeTrue();
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
        runtime.ProfileGeneration.Should().Be(new SpeechInputGeneration(0, SpeechInputSelection.Default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SpeechComparisonHostDisposalClearsTextAndJoinsCaptureWhileDisposingUndoAndCompletionNotices(bool bindCompletionNotices)
    {
        var runtime = new SpeechSetupComparisonTests.Runtime();
        await using var comparison = new SpeechSetupComparison(runtime, runtime);
        var f = new Fixture(speechSetupComparison: comparison);
        await using var admission = f.OutputAdmission;
        f.VoiceConsent.Consent = true;
        f.Voice.Microphones = [new("mic", "Headset")];
        f.Voice.DefaultMicrophoneId = "mic";
        await f.ViewModel.InitializeAsync();
        using var source = new LocalEventBrokerTests.Fixture();
        await using var broker = source.Create();
        if (bindCompletionNotices)
        {
            var target = BindSessions(f).Session;
            source.Session = target.Authority.SessionId;
            f.ViewModel.BindLocalEvents(broker);
            if (f.ViewModel.IsVoiceEnabled) { await f.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
            await f.ViewModel.ToggleListeningCommand.ExecuteAsync();
            f.ViewModel.PublishForegroundConversationTarget(new(target, static () => true));
            using var root = HostActivity.BeginRoot(new(new(Guid.NewGuid()), source.Session, new(Guid.NewGuid()), RequestOrigin.LocalUi),
                HostActivityLayer.Desktop, HostOperation.Policy);
            await f.ViewModel.ChangeCompletionNoticesNativeAsync(source.Session, target.Authority.Generation, true,
                static () => true, TestContext.Current.CancellationToken);
            f.ViewModel.CompletionNoticeStatus.Should().Contain("On");
        }
        f.ViewModel.BindSpeechInputNativeLifetime(static () => true);
        f.ViewModel.ThemeMode = ApplicationThemeMode.Dark;
        await f.ViewModel.AppearanceUpdate;
        f.ViewModel.CanReviewSettingsUndo.Should().BeTrue();
        await f.ViewModel.ReviewSpeechComparisonCommand.ExecuteAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<SpeechComparisonObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken captureToken = default;
        runtime.Capture = (_, _, token) => { captureToken = token; entered.TrySetResult(); return release.Task; };
        var work = f.ViewModel.TestSpeechComparisonCommand.ExecuteAsync();
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var disposal = f.ViewModel.DisposeAsync().AsTask();
        try
        {
            captureToken.IsCancellationRequested.Should().BeTrue();
            disposal.IsCompleted.Should().BeFalse();
            f.ViewModel.SpeechComparisonResults.Should().BeEmpty();
            f.ViewModel.CanReviewSettingsUndo.Should().BeFalse();
            f.ViewModel.CanConfirmSettingsUndo.Should().BeFalse();
            f.ViewModel.CompletionNoticeStatus.Should().Contain("Off");
            runtime.Disposals.Should().Be(0);
        }
        finally
        {
            release.TrySetResult(new("Late inert result", TimeSpan.FromMilliseconds(10)));
            await Task.WhenAll(work, disposal);
        }
        runtime.Captures.Should().Be(1);
        runtime.Disposals.Should().Be(1);
        f.ViewModel.SpeechComparisonResults.Should().BeEmpty();
        f.Voice.StartCalls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("phrase")]
    [InlineData("profile")]
    [InlineData("privacy")]
    [InlineData("close")]
    [InlineData("owner")]
    [InlineData("source")]
    public async Task SpeechComparisonCurrentReviewIsInvalidatedByRelevantInputChanges(string change)
    {
        var runtime = new SpeechSetupComparisonTests.Runtime();
        await using var comparison = new SpeechSetupComparison(runtime, runtime);
        var f = new Fixture(speechSetupComparison: comparison);
        f.VoiceConsent.Consent = true;
        f.Voice.Microphones = [new("mic", "Headset")];
        f.Voice.DefaultMicrophoneId = "mic";
        await f.ViewModel.InitializeAsync();
        f.ViewModel.BindSpeechInputNativeLifetime(static () => true);
        await f.ViewModel.ReviewSpeechComparisonCommand.ExecuteAsync();
        f.ViewModel.CanTestSpeechComparison.Should().BeTrue(f.ViewModel.SpeechComparisonStatus);
        switch (change)
        {
            case "phrase": f.ViewModel.SpeechComparisonPhrase = "Different"; break;
            case "profile": f.ViewModel.SelectedSpeechInputResource = Kora.Core.Voice.SpeechInputResource.Gpu; break;
            case "privacy":
                var previous = f.PrivacyObservation.Current;
                f.PrivacyObservation.Current = f.PrivacyObservation.Current with { SessionState = WindowsSessionState.Locked };
                f.PrivacyObservation.Publish(new(previous, f.PrivacyObservation.Current)); break;
            case "close": f.ViewModel.BindSpeechInputNativeLifetime(static () => false); break;
            case "owner": f.ViewModel.BindVoiceOwnershipGate(static () => false); break;
            default: await f.RunAsync("help"); break;
        }
        f.ViewModel.CanTestSpeechComparison.Should().BeFalse();
        f.ViewModel.SpeechComparisonResults.Should().BeEmpty();
        runtime.Acquisitions.Should().Be(0);
        await f.ViewModel.DisposeAsync();
    }
}
