using AwesomeAssertions;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Platform;
using Kora.Core.Voice;
using Kora.Tools.Speech;
using Kora.Application.ViewModels;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("read that aloud", "Kora")]
    [InlineData("Kora, read current response", "Kora")]
    [InlineData("Nova read that aloud!", "Nova")]
    public void ResponseReadMatchesOnlyExactCurrentNameCommands(string text, string name) =>
        ResponseReadCommand.Matches(text, name).Should().BeTrue();

    [Theory]
    [InlineData("please read that aloud")]
    [InlineData("read current response and lock windows")]
    [InlineData("read that aloud from clipboard")]
    [InlineData("Nova, read that aloud")]
    [InlineData("read\nthat aloud")]
    public void ResponseReadRejectsAmbiguousOrRawSourceCommands(string text) =>
        ResponseReadCommand.Matches(text, "Kora").Should().BeFalse();

    [Theory]
    [InlineData("native")]
    [InlineData("typed")]
    [InlineData("voice")]
    public async Task ResponseReadNativeTypedAndActivatedVoiceConvergeOnTheSameExactAction(string lane)
    {
        var fixture = await CreateReadableResponseAsync();
        var title = fixture.ViewModel.ResponseTitle;
        var body = fixture.ViewModel.ResponseBody;
        var before = fixture.TextToSpeech.SpeakCalls;

        if (string.Equals(lane, "native", StringComparison.Ordinal)) { await fixture.ViewModel.ReadCurrentResponseCommand.ExecuteAsync(); }
        else if (string.Equals(lane, "typed", StringComparison.Ordinal)) { await fixture.RunAsync("read that aloud"); }
        else { await fixture.RaiseActivatedTranscriptAsync("Kora, read current response", 1); }

        fixture.TextToSpeech.SpeakCalls.Should().Be(before + 1);
        fixture.TextToSpeech.SpokenText.Should().Be($"{title}. {body}");
        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.ViewModel.ReadCurrentResponseStatus.Should().Contain("completed once");
        var audits = fixture.Audit.Events.Where(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)).ToArray();
        audits.Should().HaveCount(2);
        audits[0].Outcome.Should().Be(SecurityAuditOutcome.Requested);
        audits[1].Outcome.Should().Be(SecurityAuditOutcome.Succeeded);
        audits[1].TargetId.Should().Be(audits[0].TargetId);
        audits[0].Initiator.Should().Be(string.Equals(lane, "voice", StringComparison.Ordinal)
            ? SecurityAuditInitiator.VoiceCommand : string.Equals(lane, "typed", StringComparison.Ordinal)
                ? SecurityAuditInitiator.TypedCommand : SecurityAuditInitiator.LocalUser);
        fixture.ViewModel.ResponseReadModelUnavailableReason.Should().Be("current-response-model-tool-loop-not-qualified");
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Suspected)]
    [InlineData(CallState.Unknown)]
    public async Task ResponseReadRefusesProtectedCallsAndNeverReplaysOnClearance(CallState state)
    {
        var fixture = await CreateReadableResponseAsync();
        fixture.CallState.SetState(state);
        await fixture.ViewModel.CallClosureTask;
        await fixture.RunAsync("read current response");
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.ReadCurrentResponseStatus.Should().Contain("unavailable");
        fixture.CallState.SetState(CallState.Clear);
        await fixture.ViewModel.CallClosureTask;
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.CanReadCurrentResponse.Should().BeFalse();
    }

    [Fact]
    public async Task ResponseReadDoesNotCreateAVisualOnlyExceptionOrChangePreferences()
    {
        var fixture = await CreateReadableResponseAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VisualOnly;
        await fixture.RunAsync("read current response");
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.ReadCurrentResponseStatus.Should().Contain("Normal speech mode");
        fixture.ViewModel.DefaultResponseMode.Should().Be(ResponseOutputMode.VisualOnly);
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.Hybrid;
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ResponseReadUsesBothExactSummaryCapsWithoutShortening(bool lowerWords, bool lowerSentences)
    {
        var fixture = await CreateReadableResponseAsync();
        var full = $"{fixture.ViewModel.ResponseTitle}. {fixture.ViewModel.ResponseBody}";
        var measure = SpokenSummaryMeasure.Count(full);
        fixture.Preferences.SummaryLimits = new(measure.Sentences - (lowerSentences ? 1 : 0),
            measure.Words - (lowerWords ? 1 : 0));
        fixture.SpeechConfiguration.Reload();
        await fixture.RunAsync("read that aloud");
        if (lowerWords || lowerSentences)
        {
            fixture.TextToSpeech.SpokenText.Should().BeNull();
            fixture.ViewModel.ResponseOutputStatus.Should().Contain("No text was shortened");
        }
        else { fixture.TextToSpeech.SpokenText.Should().Be(full); }
        fixture.ViewModel.ResponseBody.Should().Be(full[(fixture.ViewModel.ResponseTitle.Length + 2)..]);
    }

    [Theory]
    [InlineData("lock")]
    [InlineData("private")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("stop")]
    [InlineData("close")]
    [InlineData("dispose")]
    public async Task ResponseReadRetiresPendingSynthesisWithoutLateReplay(string transition)
    {
        var fixture = await CreateReadableResponseAsync();
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.TextToSpeech.SpeakStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = fixture.ViewModel.ReadCurrentResponseCommand.ExecuteAsync();
        await fixture.TextToSpeech.SpeakStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.ViewModel.IsCurrentResponseReadActive.Should().BeTrue();
        switch (transition)
        {
            case "lock":
                fixture.Session.IsUnlocked = false;
                fixture.ViewModel.CloseForObservedPrivacyEvent("Locked", true, WindowsSessionState.Locked);
                break;
            case "private":
                fixture.ViewModel.CloseForObservedPrivacyEvent("Private transition", true);
                break;
            case "owner": fixture.ViewModel.BindCallOwnershipGate(static () => false); break;
            case "call": fixture.CallState.SetState(CallState.Unknown); break;
            case "stop": await fixture.ViewModel.StopSpeechCommand.ExecuteAsync(); break;
            case "close": fixture.ViewModel.RetireCurrentResponseSpeech(); break;
            case "dispose": fixture.ViewModel.Dispose(); break;
        }
        await read.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.ViewModel.IsCurrentResponseReadActive.Should().BeFalse();
        fixture.TextToSpeech.IsSpeaking.Should().BeFalse();
        fixture.ViewModel.IsSpeaking.Should().BeFalse();
        fixture.Audit.Events.Should().Contain(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Cancelled);
        var calls = fixture.TextToSpeech.SpeakCalls;
        fixture.TextToSpeech.SpeakGate.TrySetResult();
        fixture.TextToSpeech.SpeakCalls.Should().Be(calls);
    }

    [Fact]
    public async Task ResponseReadIdenticalTextReplacementCannotRetargetTheOriginalInvocation()
    {
        var fixture = await CreateReadableResponseAsync();
        var body = fixture.ViewModel.ResponseBody;
        var before = fixture.TextToSpeech.SpeakCalls;
        Task? replacement = null;
        fixture.Audit.BeforeWrite = audit =>
        {
            if (!string.Equals(audit.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal) || audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            fixture.Audit.BeforeWrite = null;
            replacement = fixture.RunAsync("what power action is pending");
        };
        await fixture.ViewModel.ReadCurrentResponseCommand.ExecuteAsync();
        await replacement!;
        fixture.ViewModel.ResponseBody.Should().Be(body);
        // Only the new request's ordinary automatic speech ran, not the retired one-shot.
        fixture.TextToSpeech.SpeakCalls.Should().Be(before + 1);
        fixture.Audit.Events.Should().Contain(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Cancelled);
    }

    [Theory]
    [InlineData("muted")]
    [InlineData("missing")]
    [InlineData("enumeration")]
    [InlineData("volume")]
    [InlineData("limits")]
    public async Task ResponseReadPreservesTheFullVisualAnswerOnOutputOrPreferenceFailure(string failure)
    {
        var fixture = await CreateReadableResponseAsync();
        var body = fixture.ViewModel.ResponseBody;
        switch (failure)
        {
            case "muted":
                fixture.TextToSpeech.SpeakException = new AudioOutputDeviceUnavailableException(AudioOutputFailureReason.Muted, "Muted.");
                break;
            case "missing": fixture.TextToSpeech.OutputDevices = []; break;
            case "enumeration": fixture.TextToSpeech.OutputEnumerationException = new InvalidOperationException("No endpoint."); break;
            case "volume": fixture.TextToSpeech.SpeakException = new PlaybackVolumeUnavailableException("Volume unavailable."); break;
            case "limits":
                fixture.Preferences.SummaryLimitsLoadFailure = new InvalidDataException("Invalid limits.");
                fixture.SpeechConfiguration.Reload();
                break;
        }
        await fixture.RunAsync("read current response");
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.ReadCurrentResponseStatus.Should().Contain("not played completely");
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseReadDoesNotAnswerOrCancelPendingQuestionsOrApprovals(bool approval)
    {
        var fixture = CreateSummaryFixture(SpeechProviderIds.Windows);
        await fixture.ViewModel.InitializeAsync();
        if (approval) { fixture.Reasoner.Action = Kora.Core.Commands.BuiltInAction.LockMachine; }
        else { fixture.Reasoner.Question = new("Which color?", ["Blue", "Green"]); }
        await fixture.RunAsync("choose something");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.TextToSpeech.ClearSpokenResponse();
        var body = fixture.ViewModel.ResponseBody;
        await fixture.RunAsync("read that aloud");
        fixture.ViewModel.IsResponseInteractionPending.Should().BeTrue();
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Fact]
    public async Task ResponseReadRejectsForeignTargetsAndSeparateNativeDecisions()
    {
        var fixture = await CreateReadableResponseAsync();
        using var activity = Kora.Core.Diagnostics.HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        await ((ISpeechReadOncePlayback)fixture.ViewModel).ReadOnceAsync(
            new(Guid.NewGuid(), 1, fixture.ViewModel.CallPolicyRevision), SecurityAuditInitiator.LocalUser, TestContext.Current.CancellationToken);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.BindResponseReadDecisionGate(static () => false);
        await fixture.RunAsync("read current response");
        await ((ISpeechReadOncePlayback)fixture.ViewModel).ReadOnceAsync(
            new(Guid.NewGuid(), 1, fixture.ViewModel.CallPolicyRevision), SecurityAuditInitiator.LocalUser, TestContext.Current.CancellationToken);
        fixture.ViewModel.CanReadCurrentResponse.Should().BeFalse();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Fact]
    public async Task ResponseReadUsesTheCurrentRenamedVoiceGrammarWithoutChangingTheAnswer()
    {
        var fixture = await CreateReadableResponseAsync();
        await fixture.RunAsync("set assistant.name to Nova");
        await fixture.RunAsync("what power action is pending");
        fixture.TextToSpeech.ClearSpokenResponse();
        await fixture.RaiseActivatedTranscriptAsync("Nova, read that aloud", 1);
        fixture.TextToSpeech.SpokenText.Should().StartWith("No power action is pending.");
        fixture.Voice.StartedPhrases.Should().Contain("Nova read that aloud").And.Contain("Nova read current response");
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ResponseReadCannotReadRawWebResultBodies()
    {
        var fixture = new Fixture(webPageGet: new(
            new ViewModelWebTransport(WebResponse("https://example.com/", "raw-private-web-body")),
            new ViewModelWebAudit()));
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("get web page https://example.com/");
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.ViewModel.CanReadCurrentResponse.Should().BeFalse();
        await fixture.RunAsync("read that aloud");
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.ResponseBody.Should().Contain("raw-private-web-body");
    }

    [Theory]
    [InlineData("speech")]
    [InlineData("mode")]
    [InlineData("owner")]
    [InlineData("privacy")]
    [InlineData("output")]
    [InlineData("voice")]
    public async Task ResponseReadBindsAllAdmissionRevisionsBeforeAwaitedAudit(string revision)
    {
        var fixture = await CreateReadableResponseAsync();
        fixture.Audit.BeforeWrite = audit =>
        {
            if (!string.Equals(audit.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
                || audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            fixture.Audit.BeforeWrite = null;
            switch (revision)
            {
                case "speech":
                    fixture.Preferences.SummaryLimits = new(1, 1);
                    fixture.SpeechConfiguration.Reload();
                    break;
                case "mode": fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VisualOnly; break;
                case "owner": fixture.ViewModel.BindCallOwnershipGate(static () => false); break;
                case "privacy": fixture.Session.IsUnlocked = false; break;
                case "output": fixture.TextToSpeech.OutputDevices = []; break;
                case "voice": fixture.Voice.AdvanceGeneration(); break;
            }
        };
        if (string.Equals(revision, "voice", StringComparison.Ordinal))
        {
            await fixture.RaiseActivatedTranscriptAsync("Kora, read that aloud", 1);
        }
        else { await fixture.RunAsync("read that aloud"); }
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Audit.Events.Should().Contain(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Cancelled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseReadContainsCancelledAuditWithoutInventingSuccess(bool terminal)
    {
        var fixture = await CreateReadableResponseAsync();
        fixture.Audit.BeforeWrite = audit =>
        {
            if (!string.Equals(audit.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
                || terminal != (audit.Outcome == SecurityAuditOutcome.Succeeded)) { return; }
            fixture.Audit.BeforeWrite = null;
            throw new OperationCanceledException("Audit cancelled");
        };
        await fixture.RunAsync("read that aloud");
        fixture.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded);
        fixture.ViewModel.IsSpeaking.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseReadAuditFailureRetainsTheFullAnswerWithoutLoggingContent(bool dispose)
    {
        var log = new ResponseReadLogger();
        var fixture = new Fixture(logger: log);
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("what power action is pending");
        fixture.TextToSpeech.ClearSpokenResponse();
        var body = fixture.ViewModel.ResponseBody;
        fixture.Audit.BeforeWrite = audit =>
        {
            if (!string.Equals(audit.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)) { return; }
            fixture.Audit.BeforeWrite = null;
            if (dispose) { fixture.ViewModel.Dispose(); }
            throw new IOException("Do not log this raw content");
        };
        await fixture.RunAsync("read that aloud");
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded);
        log.Messages.Should().NotContain(message => message.Contains("Do not log this raw content", StringComparison.Ordinal));
        log.Exceptions.Should().NotContain(exception => exception != null
            && exception.Message.Contains("Do not log this raw content", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResponseReadProviderFailureLogsOnlyTheExceptionTypeAndRetainsRecovery()
    {
        var log = new ResponseReadLogger();
        var fixture = new Fixture(logger: log);
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("what power action is pending");
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.TextToSpeech.SpeakException = new InvalidOperationException("private-answer-fragment");
        await fixture.RunAsync("read that aloud");
        fixture.ViewModel.ResponseBody.Should().Contain("Power execution is");
        log.Exceptions.Should().NotContain(exception => exception != null
            && exception.Message.Contains("private-answer-fragment", StringComparison.Ordinal));
        log.Messages.Should().NotContain(message => message.Contains("private-answer-fragment", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResponseReadQueuedVoiceInputCannotSelectAReplacementResponse()
    {
        var fixture = await CreateReadableResponseAsync();
        if (!fixture.ViewModel.IsVoiceEnabled) { await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
        await fixture.ViewModel.BeginPushToTalkAsync();
        await fixture.ViewModel.EndPushToTalkAsync();
        fixture.Dispatcher.BeforeInvoke = () => fixture.ViewModel.RetireCurrentResponseSpeech();
        await fixture.Voice.RaiseTranscriptAsync("Kora, read that aloud", 1);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.ReadCurrentResponseStatus.Should().Contain("unavailable");
    }

    [Fact]
    public async Task ResponseReadUnavailableAssistantNameRefusesThePrefixWithoutSelectingAnotherAnswer()
    {
        var fixture = new Fixture();
        fixture.NamePreferences.Name = "Supported Commands";
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("Kora, read current response");
        fixture.ViewModel.ReadCurrentResponseStatus.Should().Contain("prefix routing is unavailable");
        await fixture.RunAsync("read current response");
        fixture.ViewModel.ReadCurrentResponseStatus.Should().Contain("unavailable");
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Fact]
    public async Task ResponseReadUnconfirmedReleaseRetainsOwnedActiveStateAndNativeStopRecovery()
    {
        var fixture = await CreateReadableResponseAsync();
        fixture.TextToSpeech.ReturnWhileSpeaking = true;
        fixture.TextToSpeech.StopException = new IOException("Owned release unconfirmed");
        await fixture.RunAsync("read current response");
        fixture.ViewModel.IsSpeaking.Should().BeTrue();
        fixture.ViewModel.StopSpeechCommand.CanExecute(null).Should().BeTrue();
        fixture.ViewModel.ReadCurrentResponseStatus.Should().Contain("could not be confirmed");
        fixture.TextToSpeech.StopException = null;
        await fixture.ViewModel.StopSpeechCommand.ExecuteAsync();
        fixture.ViewModel.IsSpeaking.Should().BeFalse();
    }

    [Fact]
    public async Task ResponseReadEarlyProviderReturnStopsOwnedPlaybackWithoutClaimingCompletion()
    {
        var fixture = await CreateReadableResponseAsync();
        fixture.TextToSpeech.ReturnWhileSpeaking = true;
        await fixture.RunAsync("read current response");
        fixture.TextToSpeech.IsSpeaking.Should().BeFalse();
        fixture.ViewModel.IsSpeaking.Should().BeFalse();
        fixture.ViewModel.ReadCurrentResponseStatus.Should().Contain("not played completely");
        fixture.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task ResponseReadRejectsAConcurrentExactTargetWhileTheOriginalAuditIsPending()
    {
        var fixture = await CreateReadableResponseAsync();
        Task? concurrent = null;
        var calls = fixture.TextToSpeech.SpeakCalls;
        fixture.Audit.BeforeWrite = audit =>
        {
            if (!string.Equals(audit.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
                || audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            fixture.Audit.BeforeWrite = null;
            var revision = (long)typeof(MainViewModel).GetField("ordinaryResponseRevision",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.ViewModel)!;
            concurrent = ((ISpeechReadOncePlayback)fixture.ViewModel).ReadOnceAsync(
                new(Guid.Parse(audit.TargetId), revision, fixture.ViewModel.CallPolicyRevision),
                SecurityAuditInitiator.LocalUser, TestContext.Current.CancellationToken);
        };
        await fixture.RunAsync("read current response");
        await concurrent!;
        fixture.TextToSpeech.SpeakCalls.Should().Be(calls + 1);
    }

    [Theory]
    [InlineData(SecurityAuditOutcome.Requested)]
    [InlineData(SecurityAuditOutcome.Cancelled)]
    public async Task ResponseReadDisposedHostCannotForgeAnUnownedCancelledAudit(SecurityAuditOutcome outcome)
    {
        var fixture = await CreateReadableResponseAsync();
        var write = typeof(MainViewModel).GetMethod("WriteAuditAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<SecurityAuditEvent, bool, Task>>(fixture.ViewModel);
        fixture.ViewModel.Dispose();
        var foreign = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ApplicationExecution,
            SpeechReadOnce.AuditActionId, outcome, SecurityAuditInitiator.LocalUser, "foreign");
        var run = () => write(foreign, true);
        await run.Should().ThrowAsync<ObjectDisposedException>();
        fixture.Audit.Events.Should().NotContain(foreign);
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.System)]
    [InlineData(SecurityAuditInitiator.ModelSuggestion)]
    [InlineData(SecurityAuditInitiator.VoiceCommand)]
    public async Task ResponseReadLocalUiCannotRelabelItsInitiator(SecurityAuditInitiator initiator)
    {
        var fixture = await CreateReadableResponseAsync();
        await fixture.ViewModel.ReadCurrentResponseCommand.ExecuteAsync();
        var audit = fixture.Audit.Events.Last(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal));
        var revision = (long)typeof(MainViewModel).GetField("ordinaryResponseRevision",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.ViewModel)!;
        fixture.TextToSpeech.ClearSpokenResponse();
        using var activity = Kora.Core.Diagnostics.HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        await ((ISpeechReadOncePlayback)fixture.ViewModel).ReadOnceAsync(
            new(Guid.Parse(audit.TargetId), revision, fixture.ViewModel.CallPolicyRevision),
            initiator, TestContext.Current.CancellationToken);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.ReadCurrentResponseStatus.Should().Contain("original native");
    }

    private sealed class ResponseReadLogger : ILogger<MainViewModel>
    {
        public List<string> Messages { get; } = [];
        public List<Exception?> Exceptions { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }

    [Fact]
    public async Task ResponseReadUsesConfiguredOutputVolumeRateModeFeedbackAndCaptions()
    {
        var fixture = new Fixture(enableOutputConfiguration: true, enablePlaybackVolume: true,
            enableWindowsSpeechRate: true, enableResponseModeConfiguration: true, enableInCallFeedback: true,
            enableSpeechText: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("set display.speech-text to CurrentUtterance");
        await fixture.RunAsync("what power action is pending");
        fixture.TextToSpeech.ClearSpokenResponse();
        await fixture.ViewModel.ReadCurrentResponseCommand.ExecuteAsync();
        fixture.TextToSpeech.SpokenText.Should().StartWith("No power action is pending.");
        fixture.TextToSpeech.CaptionPlaybackId.Should().NotBeNull();
        fixture.ViewModel.SelectedPlaybackVolume = 0;
        await fixture.ViewModel.SavePlaybackVolumeCommand.ExecuteAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        await fixture.RunAsync("read that aloud");
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.CanReadCurrentResponse.Should().BeFalse();
    }

    private static async Task<Fixture> CreateReadableResponseAsync()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.Hybrid;
        await fixture.RunAsync("what power action is pending");
        fixture.TextToSpeech.ClearSpokenResponse();
        return fixture;
    }
}
