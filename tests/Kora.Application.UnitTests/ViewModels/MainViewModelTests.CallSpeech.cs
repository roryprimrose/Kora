using AwesomeAssertions;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Platform;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(CallState.Active, false)]
    [InlineData(CallState.Suspected, true)]
    [InlineData(CallState.Unknown, true)]
    public async Task CallSpeechCompletesSeparateExactReviewConfirmationAndOneJoinedPlayback(CallState state, bool typed)
    {
        var f = await CreateCallSpeechResponse(state);
        var native = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(native);
        f.ViewModel.CurrentResponseChanged += (_, _) => { };
        native.BeforeOpen = () => f.ViewModel.CanReviewCallSpeech.Should().BeFalse();
        f.ViewModel.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(f.ViewModel.CallSpeechStatus), StringComparison.Ordinal)
                && f.ViewModel.CallSpeechStatus.StartsWith("Speaking only", StringComparison.Ordinal))
            { f.ViewModel.CanReviewCallSpeech.Should().BeFalse(); }
        };
        var text = $"{f.ViewModel.ResponseTitle}. {f.ViewModel.ResponseBody}";
        var call = f.ViewModel.CallObservation;
        f.ViewModel.IsSpeechResponseEnabled.Should().BeFalse();
        f.ViewModel.CanReviewCallSpeech.Should().BeTrue();
        if (typed) { await f.RunAsync("review call speech"); }
        else { await f.ViewModel.ReviewCallSpeechCommand.ExecuteAsync(); }
        native.Content!.Source.Should().EndWith("\n\n" + text)
            .And.Contain("Output:").And.Contain("voice:").And.Contain("No setting");
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.ViewModel.CanConfirmCallSpeech.Should().BeTrue();
        await f.ViewModel.GetCallSpeechStatusCommand.ExecuteAsync();
        f.ViewModel.CallSpeechStatus.Should().Contain("awaiting separate local confirmation");
        if (typed) { await f.RunAsync("confirm call speech"); }
        else { await f.ViewModel.ConfirmCallSpeechCommand.ExecuteAsync(); }
        f.TextToSpeech.SpokenText.Should().Be(text);
        f.TextToSpeech.SpokenOutputDevice!.IsSystemDefault.Should().BeFalse();
        f.ViewModel.CallSpeechStatus.Should().Contain("completed once");
        f.ViewModel.CanConfirmCallSpeech.Should().BeFalse();
        await f.RunAsync("confirm call speech");
        f.TextToSpeech.SpeakCalls.Should().Be(1);
        f.ViewModel.CallObservation.Should().Be(call);
        f.CallPreferences.SavedSettings.Should().BeNull();
        f.Audit.Events.Where(item => item.ActionId is "speech.call-once").Select(item => item.Outcome)
            .Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Suspected)]
    [InlineData(CallState.Unknown)]
    public async Task ProtectedVoiceCannotInitiateOrRelabelNativeReviewOrConfirmation(CallState state)
    {
        var f = await CreateCallSpeechResponse(state);
        var native = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(native);
        using (var voice = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request))
        {
            await f.ViewModel.ReviewCallSpeechCommand.ExecuteAsync();
            f.ViewModel.CallSpeechStatus.Should().Contain("original voice");
        }
        native.Calls.Should().Be(0);
        await f.RunAsync("confirm call speech");
        f.TextToSpeech.SpokenText.Should().BeNull();
        await f.RunAsync("review call speech");
        using (var voice = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request))
        {
            await f.ViewModel.ConfirmCallSpeechCommand.ExecuteAsync();
            f.ViewModel.CallSpeechStatus.Should().Contain("original voice");
            await f.ViewModel.GetCallSpeechStatusCommand.ExecuteAsync();
            f.ViewModel.CallSpeechStatus.Should().Contain("awaiting separate");
            await f.ViewModel.CancelCallSpeechCommand.ExecuteAsync();
        }
        f.ViewModel.CanConfirmCallSpeech.Should().BeFalse();
        f.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("call")]
    [InlineData("owner")]
    [InlineData("native")]
    [InlineData("privacy")]
    [InlineData("mute")]
    [InlineData("output")]
    [InlineData("mode")]
    [InlineData("decisions")]
    public async Task CallSpeechReviewRetiresOnEveryConsumedSourceAndOutputBoundary(string change)
    {
        var f = await CreateCallSpeechResponse();
        var native = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(native);
        await f.RunAsync("review call speech");
        switch (change)
        {
            case "source": f.ViewModel.RetireCurrentResponseSpeech(); break;
            case "call": f.CallState.SetState(CallState.Suspected); await f.ViewModel.CallClosureTask; break;
            case "owner": f.ViewModel.BindCallOwnershipGate(() => false); break;
            case "native": native.Alive = false; break;
            case "privacy": f.Session.IsUnlocked = false; break;
            case "mute": f.TextToSpeech.OutputDevices = [new("0", "Default output", IsMuted: true)]; break;
            case "output": f.TextToSpeech.DefaultOutputDeviceId = null; break;
            case "mode": f.ViewModel.DefaultResponseMode = ResponseOutputMode.VisualOnly; break;
            case "decisions": f.ViewModel.BindResponseReadDecisionGate(() => false); break;
        }
        await f.RunAsync("confirm call speech");
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, "speech.call-once", StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded);
        f.ViewModel.CanConfirmCallSpeech.Should().BeFalse();
        f.CallState.SetState(CallState.Clear);
        await f.ViewModel.CallClosureTask;
        f.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Fact]
    public async Task CallSpeechCannotDuplicateConfirmationWhileItsRequestedAuditIsPending()
    {
        var f = await CreateCallSpeechResponse();
        f.ViewModel.BindCurrentResponsePresentation(new ResponsePresenter(f.ViewModel));
        await f.RunAsync("review call speech");
        Task? concurrent = null;
        f.Audit.BeforeWrite = audit =>
        {
            if (audit.ActionId is not "speech.call-once" || audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            f.Audit.BeforeWrite = null;
            f.ViewModel.CanReviewCallSpeech.Should().BeFalse();
            concurrent = f.RunAsync("confirm call speech");
        };
        await f.RunAsync("confirm call speech");
        await concurrent!;
        f.TextToSpeech.SpeakCalls.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallSpeechCancellationOrCallRevisionJoinsIgnoredCancellationWithoutLateSuccess(bool callChange)
    {
        var f = await CreateCallSpeechResponse();
        f.ViewModel.BindCurrentResponsePresentation(new ResponsePresenter(f.ViewModel));
        await f.RunAsync("review call speech");
        f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.TextToSpeech.IgnoreSpeakCancellation = true;
        f.TextToSpeech.HoldSpeechCompletionOnStop = true;
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.TextToSpeech.BeforeStop = () => stopped.TrySetResult();
        var playback = f.RunAsync("confirm call speech");
        await f.TextToSpeech.SpeakStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await f.ViewModel.GetCallSpeechStatusCommand.ExecuteAsync();
        f.ViewModel.CallSpeechStatus.Should().Contain("in progress");
        Task cancellation;
        if (callChange)
        {
            f.CallState.SetState(CallState.Unknown);
            cancellation = f.ViewModel.CallClosureTask;
        }
        else { cancellation = f.ViewModel.CancelCallSpeechCommand.ExecuteAsync(); }
        try
        {
            await stopped.Task.WaitAsync(TestContext.Current.CancellationToken);
            playback.IsCompleted.Should().BeFalse("resource completion is separate from cancellation or Stop");
            f.ViewModel.IsCurrentResponseReadActive.Should().BeTrue();
        }
        finally { f.TextToSpeech.SpeakGate.TrySetResult(); }
        await Task.WhenAll(playback, cancellation);
        f.ViewModel.IsCurrentResponseReadActive.Should().BeFalse();
        f.ViewModel.CanConfirmCallSpeech.Should().BeFalse();
        f.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, "speech.call-once", StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded);
        await f.RunAsync("confirm call speech");
        f.TextToSpeech.SpeakCalls.Should().Be(1);
    }

    [Theory]
    [InlineData("review")]
    [InlineData("apply")]
    public async Task CallSpeechRechecksAuthorityAfterRequestedAuditBeforeDisclosure(string phase)
    {
        var f = await CreateCallSpeechResponse();
        var native = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(native);
        if (phase is "apply") { await f.RunAsync("review call speech"); }
        f.Audit.BeforeWrite = audit =>
        {
            if (!string.Equals(audit.ActionId, phase is "review" ? "speech.call-once.review" : "speech.call-once", StringComparison.Ordinal)
                || audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            f.Audit.BeforeWrite = null;
            f.ViewModel.BindCallOwnershipGate(() => false);
        };
        await f.RunAsync(phase is "review" ? "review call speech" : "confirm call speech");
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, "speech.call-once", StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded);
        f.ViewModel.CanConfirmCallSpeech.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallSpeechRequiresAcknowledgedCompleteNativeReviewAndCancelDoesNotPlay(bool acknowledge)
    {
        var f = await CreateCallSpeechResponse();
        f.ViewModel.BindCurrentResponsePresentation(new ResponsePresenter(f.ViewModel) { Acknowledge = acknowledge });
        await f.RunAsync("review call speech");
        f.ViewModel.CanConfirmCallSpeech.Should().Be(acknowledge);
        await f.RunAsync("cancel call speech");
        await f.ViewModel.GetCallSpeechStatusCommand.ExecuteAsync();
        f.ViewModel.CallSpeechStatus.Should().Contain("No current");
        f.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Fact]
    public async Task CallSpeechUsesConfiguredSpeechAndOutputWithoutChangingFeedbackModeOrLeakingContent()
    {
        var capture = new ResponseReadLogger();
        var f = new Fixture(logger: capture, enableOutputConfiguration: true, enablePlaybackVolume: true,
            enableWindowsSpeechRate: true, enableResponseModeConfiguration: true, enableInCallFeedback: true,
            enableSpeechText: true);
        await using var admission = f.OutputAdmission;
        await PrepareCallSpeechResponse(f, CallState.Active);
        f.ViewModel.BindCurrentResponsePresentation(new ResponsePresenter(f.ViewModel));
        var saved = f.FeedbackConfiguration!.Get();
        await f.RunAsync("review call speech");
        f.ViewModel.CanConfirmCallSpeech.Should().BeTrue();
        var text = $"{f.ViewModel.ResponseTitle}. {f.ViewModel.ResponseBody}";
        f.TextToSpeech.SpeakException = new IOException(text);
        await f.RunAsync("confirm call speech");
        f.ViewModel.CallSpeechStatus.Should().NotContain("completed once");
        capture.Messages.Should().NotContain(message => message.Contains(text, StringComparison.Ordinal));
        capture.Exceptions.Should().NotContain(exception => exception != null && exception.ToString().Contains(text, StringComparison.Ordinal));
        f.Audit.Events.Should().NotContain(item => (item.ActionId + item.TargetId + item.ReasonCode).Contains(text, StringComparison.Ordinal));
        f.FeedbackConfiguration.Get().Should().Be(saved);
        await f.RunAsync("confirm call speech");
        f.TextToSpeech.SpeakCalls.Should().Be(1);
    }

    [Theory]
    [InlineData("review call speech", 0)]
    [InlineData("Kora confirm call speech!", 1)]
    [InlineData("cancel call speech", 2)]
    [InlineData("call speech status", 3)]
    [InlineData("review call speech and approve", -1)]
    [InlineData("review call", -1)]
    [InlineData("review\ncall speech", -1)]
    [InlineData("call speech", -1)]
    [InlineData("please review call speech", -2)]
    [InlineData("Nova review call speech", -2)]
    public void CallSpeechGrammarIsExactAndReservesMalformedCommands(string text, int expected)
    {
        var command = CallSpeechCommand.Parse(text, "Kora");
        (command?.Operation is { } operation ? (int)operation : command is null ? -2 : -1).Should().Be(expected);
        CallSpeechCommand.Parse("review call speech " + new string('x', 1024), "Kora")!.Operation.Should().BeNull();
        CallSpeechCommand.Parse("review call speech\0", "Kora")!.Operation.Should().BeNull();
        CallSpeechCommand.Parse("\nreview call speech\n", "Kora")!.Operation.Should().BeNull();
        CallSpeechCommand.Parse("review call speech" + new string(' ', 1024), "Kora")!.Operation.Should().BeNull();
    }

    [Fact]
    public async Task CallSpeechInvalidReservedCommandsCannotAnswerQuestionsOrReachInference()
    {
        var f = CreateSummaryFixture(SpeechProviderIds.Windows);
        await PrepareCallSpeechResponse(f, CallState.Active);
        f.Reasoner.Question = new("Which color?", ["Blue", "Green"]);
        await f.RunAsync("Kora, choose something");
        await f.ViewModel.ActiveReasoningTask!;
        f.Reasoner.Requests.Clear();
        await f.RunAsync("review call speech and approve once");
        f.ViewModel.CallSpeechStatus.Should().Contain("was not accepted");
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        f.Reasoner.Requests.Should().BeEmpty();
        f.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData(RequestOrigin.LocalUi)]
    public async Task CallSpeechRejectsUnknownOrStoppedOriginalScopes(RequestOrigin origin)
    {
        var f = await CreateCallSpeechResponse();
        f.ViewModel.BindCurrentResponsePresentation(new ResponsePresenter(f.ViewModel));
        using var input = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        if (origin == RequestOrigin.LocalUi) { input.Activity!.Stop(); }
        await f.ViewModel.ReviewCallSpeechCommand.ExecuteAsync();
        f.ViewModel.CanConfirmCallSpeech.Should().BeFalse();
        f.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallSpeechExpiryIsBoundedFromReviewRatherThanRenewedByConfirmation(bool confirm)
    {
        var f = await CreateCallSpeechResponse();
        var time = BindCallSpeechTime(f);
        f.ViewModel.BindCurrentResponsePresentation(new ResponsePresenter(f.ViewModel));
        await f.RunAsync("review call speech");
        time.Advance(TimeSpan.FromSeconds(119));
        f.ViewModel.CanConfirmCallSpeech.Should().BeTrue();
        if (confirm)
        {
            f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var playing = f.RunAsync("confirm call speech");
            await f.TextToSpeech.SpeakStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            time.Advance(TimeSpan.FromSeconds(1));
            await playing;
            f.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, "speech.call-once", StringComparison.Ordinal)
                && item.Outcome == SecurityAuditOutcome.Succeeded);
        }
        else
        {
            time.Advance(TimeSpan.FromSeconds(1));
            f.ViewModel.CallSpeechStatus.Should().Contain("expired");
            f.TextToSpeech.SpokenText.Should().BeNull();
        }
        f.ViewModel.CanConfirmCallSpeech.Should().BeFalse();
    }

    [Theory]
    [InlineData("unavailable-native")]
    [InlineData("unavailable-source")]
    [InlineData("output-refresh")]
    [InlineData("output-changed")]
    [InlineData("over-limit")]
    [InlineData("unknown-limits")]
    [InlineData("cancelled-review")]
    [InlineData("cancelled-apply")]
    public async Task CallSpeechNeverSubstitutesForUnavailableReviewInputsOrCancelledAdmission(string failure)
    {
        var f = await CreateCallSpeechResponse();
        f.ViewModel.BindCurrentResponsePresentation(new ResponsePresenter(f.ViewModel));
        switch (failure)
        {
            case "unavailable-native": f.ViewModel.BindCurrentResponsePresentation(null); break;
            case "unavailable-source":
                f.ViewModel.RetireCurrentResponseSpeech();
                f.ViewModel.CanReviewCallSpeech.Should().BeFalse();
                break;
            case "output-refresh": f.TextToSpeech.OutputEnumerationException = new IOException("Synthetic metadata unavailable"); break;
            case "output-changed": f.TextToSpeech.DefaultOutputDeviceId = null; break;
            case "over-limit":
                f.Preferences.SummaryLimits = new(1, 1);
                f.SpeechConfiguration.Reload();
                await f.RunAsync("what power action is pending");
                break;
            case "unknown-limits":
                f.Preferences.SummaryLimitsLoadFailure = new InvalidDataException("Synthetic invalid limits");
                f.SpeechConfiguration.Reload();
                await f.RunAsync("what power action is pending");
                break;
        }
        if (failure is "cancelled-apply") { await f.RunAsync("review call speech"); }
        if (failure is "cancelled-review" or "cancelled-apply")
        {
            f.Audit.BeforeWrite = audit =>
            {
                if (audit.Outcome == SecurityAuditOutcome.Requested && audit.ActionId.StartsWith("speech.call-once", StringComparison.Ordinal))
                { f.Audit.BeforeWrite = null; throw new OperationCanceledException("Synthetic cancellation"); }
            };
        }
        await f.RunAsync(failure is "cancelled-apply" ? "confirm call speech" : "review call speech");
        f.ViewModel.CanConfirmCallSpeech.Should().BeFalse();
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, "speech.call-once", StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallSpeechCancelReportsUnconfirmedPhysicalReleaseAndPreservesNativeStopRecovery(bool stopThrows)
    {
        var f = await CreateCallSpeechResponse();
        f.ViewModel.BindCurrentResponsePresentation(new ResponsePresenter(f.ViewModel));
        await f.RunAsync("review call speech");
        f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var playing = f.RunAsync("confirm call speech");
        await f.TextToSpeech.SpeakStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        f.TextToSpeech.RemainSpeakingOnStop = !stopThrows;
        if (stopThrows) { f.TextToSpeech.StopException = new IOException("Synthetic unconfirmed stop"); }
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.ViewModel.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(f.ViewModel.CallSpeechStatus), StringComparison.Ordinal)
                && f.ViewModel.CallSpeechStatus.Contains("resource release is unconfirmed", StringComparison.Ordinal))
            { reported.TrySetResult(); }
        };
        f.ViewModel.CancelCallSpeechCommand.Execute(null);
        await reported.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await playing;
        f.ViewModel.StopSpeechCommand.CanExecute(null).Should().BeTrue();
        f.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, "speech.call-once", StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded);
        if (!stopThrows)
        {
            await f.ViewModel.StopSpeechCommand.ExecuteAsync();
            f.ViewModel.ResponseBody.Should().Contain("release remains unconfirmed");
            f.ViewModel.StopSpeechCommand.CanExecute(null).Should().BeTrue();
        }
        f.TextToSpeech.RemainSpeakingOnStop = false;
        f.TextToSpeech.StopException = null;
        await f.ViewModel.StopSpeechCommand.ExecuteAsync();
        f.TextToSpeech.IsSpeaking.Should().BeFalse();
    }

    [Fact]
    public async Task CallSpeechExpiredQueuedCallbackCannotRetireANewExplicitReview()
    {
        var f = await CreateCallSpeechResponse();
        var time = BindCallSpeechTime(f);
        f.ViewModel.BindCurrentResponsePresentation(new ResponsePresenter(f.ViewModel));
        await f.RunAsync("review call speech");
        f.Dispatcher.DeferPosts = true;
        time.Advance(TimeSpan.FromMinutes(2));
        await f.RunAsync("cancel call speech");
        await f.RunAsync("review call speech");
        f.Dispatcher.FlushPosts();
        f.ViewModel.CanConfirmCallSpeech.Should().BeTrue();
        await f.RunAsync("confirm call speech");
        f.TextToSpeech.SpeakCalls.Should().Be(1);
    }

    [Fact]
    public async Task CallSpeechUnscopedLegacyPresentationCannotSupplyOriginalResponseIdentity()
    {
        var f = await CreateCallSpeechResponse();
        f.ViewModel.BindCurrentResponsePresentation(new ResponsePresenter(f.ViewModel));
        // Simulate the legacy unscoped presentation callback; it must not create source authority.
        typeof(Kora.Application.ViewModels.MainViewModel).GetMethod("BindCurrentResponseSpeech",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(f.ViewModel, [true, null]);
        await f.RunAsync("review call speech");
        f.ViewModel.CallSpeechStatus.Should().Contain("No fallback");
        f.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Fact]
    public async Task CallSpeechApplicationBoundaryIndependentlyRejectsUnknownInitiator()
    {
        var f = await CreateCallSpeechResponse();
        f.ViewModel.BindCurrentResponsePresentation(new ResponsePresenter(f.ViewModel));
        using var scope = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var apply = typeof(Kora.Application.ViewModels.MainViewModel).GetMethod("ExecuteCallSpeechAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .CreateDelegate<Func<CallSpeechOperation, SecurityAuditInitiator, Task>>(f.ViewModel);
        await apply(CallSpeechOperation.Review, SecurityAuditInitiator.System);
        f.ViewModel.CanConfirmCallSpeech.Should().BeFalse();
        f.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Fact]
    public async Task CallSpeechKokoroReviewDoesNotInventAWindowsRateOrRequireMicrophoneConsent()
    {
        var f = new Fixture(enableWindowsSpeechRate: true);
        await using var admission = f.OutputAdmission;
        f.TextToSpeech.Providers = [CreateWindowsProvider(), CreateKokoroProvider(true)];
        f.TextToSpeech.Voices = [new("female", "Windows", "en-US", SpeechVoiceGender.Female), CreateKokoroVoice("af_heart", "Heart")];
        f.Preferences.ProviderId = SpeechProviderIds.Kokoro;
        await PrepareCallSpeechResponse(f, CallState.Active);
        await f.ViewModel.SetVoiceConsentAsync(false);
        await f.RunAsync("what power action is pending");
        var native = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(native);
        await f.RunAsync("review call speech");
        f.ViewModel.CanConfirmCallSpeech.Should().BeTrue();
        f.ViewModel.HasVoiceConsent.Should().BeFalse();
        native.Content!.Source.Should().Contain("Windows rate:");
        await f.RunAsync("confirm call speech");
        f.TextToSpeech.SpokenVoice!.ProviderId.Should().Be(SpeechProviderIds.Kokoro);
    }

    [Fact]
    public async Task CallSpeechUnconfirmedEarlyProviderReleaseRetainsRecoveryWithoutLoggingTheResponse()
    {
        var capture = new ResponseReadLogger();
        var f = new Fixture(logger: capture);
        await PrepareCallSpeechResponse(f, CallState.Active);
        f.ViewModel.BindCurrentResponsePresentation(new ResponsePresenter(f.ViewModel));
        await f.RunAsync("review call speech");
        var exact = $"{f.ViewModel.ResponseTitle}. {f.ViewModel.ResponseBody}";
        f.TextToSpeech.ReturnWhileSpeaking = true;
        f.TextToSpeech.StopException = new IOException(exact);
        await f.RunAsync("confirm call speech");
        f.ViewModel.CanConfirmCallSpeech.Should().BeFalse();
        f.ViewModel.StopSpeechCommand.CanExecute(null).Should().BeTrue();
        await f.ViewModel.StopSpeechCommand.ExecuteAsync();
        capture.Messages.Should().NotContain(message => message.Contains(exact, StringComparison.Ordinal));
        capture.Exceptions.Should().NotContain(exception => exception != null && exception.ToString().Contains(exact, StringComparison.Ordinal));
        f.TextToSpeech.StopException = null;
        await f.ViewModel.StopSpeechCommand.ExecuteAsync();
        await f.RunAsync("confirm call speech");
        f.TextToSpeech.SpeakCalls.Should().Be(1);
        f.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, "speech.call-once", StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData("no-scope")]
    [InlineData("stopped")]
    [InlineData("completed")]
    [InlineData("invocation")]
    [InlineData("unrelated-activity")]
    public async Task CallSpeechTypedAndNativeBoundariesRejectRetiredOrUnresolvedInput(string invalid)
    {
        var f = await CreateCallSpeechResponse();
        f.ViewModel.BindCurrentResponsePresentation(new ResponsePresenter(f.ViewModel));
        if (invalid is "no-scope")
        {
            await InvokeTranscriptForTest(f.ViewModel, "call speech status", SecurityAuditInitiator.TypedCommand);
            f.ViewModel.CallSpeechStatus.Should().Contain("live original");
            return;
        }
        using var scope = HostActivity.BeginRoot(new(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()),
            RequestOrigin.LocalUi, invalid is "invocation" ? new HostId<InvocationIdentity>(Guid.NewGuid()) : null),
            HostActivityLayer.Application, HostOperation.Request);
        if (invalid is "stopped")
        {
            scope.Activity!.Stop();
            System.Diagnostics.Activity.Current = scope.Activity;
        }
        if (invalid is "completed") { scope.Complete(HostOperationOutcome.Completed); }
        using var unrelated = invalid is "unrelated-activity" ? new System.Diagnostics.Activity("unrelated").Start() : null;
        await InvokeTranscriptForTest(f.ViewModel, "review call speech", SecurityAuditInitiator.TypedCommand);
        await f.ViewModel.ReviewCallSpeechCommand.ExecuteAsync();
        f.ViewModel.CanConfirmCallSpeech.Should().BeFalse();
        f.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.ModelSuggestion)]
    [InlineData(SecurityAuditInitiator.VoiceCommand)]
    public async Task CallSpeechRejectsInitiatorRelabellingBeforeProposingAReview(SecurityAuditInitiator initiator)
    {
        var f = await CreateCallSpeechResponse();
        f.ViewModel.BindCurrentResponsePresentation(new ResponsePresenter(f.ViewModel));
        using var scope = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        await InvokeTranscriptForTest(f.ViewModel, "review call speech", initiator);
        f.ViewModel.CallSpeechStatus.Should().Contain("was not accepted");
        f.ViewModel.CanConfirmCallSpeech.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallSpeechCaptionsUseOnlyObservedReviewedOutputAndRetireWithItsAdmission(bool decisionRevoked)
    {
        var f = new Fixture(enableSpeechText: true);
        await using var admission = f.OutputAdmission;
        await PrepareCallSpeechResponse(f, CallState.Active);
        var decisionsEligible = true;
        f.ViewModel.BindResponseReadDecisionGate(() => decisionsEligible);
        await f.RunAsync("set display.speech-text to CurrentUtterance");
        await f.RunAsync("what power action is pending");
        f.ViewModel.BindCurrentResponsePresentation(new ResponsePresenter(f.ViewModel));
        await f.RunAsync("review call speech");
        f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var playing = f.RunAsync("confirm call speech");
        await f.TextToSpeech.SpeakStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        f.TextToSpeech.CaptionPlaybackId.Should().NotBeNull();
        f.TextToSpeech.SpokenOutputDevice!.IsSystemDefault.Should().BeFalse();
        f.ViewModel.IsSpeechResponseEnabled.Should().BeFalse();
        f.TextToSpeech.PlaybackFrame = new(true, 0.5, f.TextToSpeech.CaptionPlaybackId, 1);
        f.ViewModel.RefreshSpeechPlaybackFrame();
        f.ViewModel.IsSpeechCaptionVisible.Should().BeTrue();
        if (decisionRevoked) { decisionsEligible = false; }
        else { f.ViewModel.RetireCurrentResponseSpeech(); }
        f.ViewModel.IsSpeechCaptionVisible.Should().BeFalse();
        f.ViewModel.RetireCurrentResponseSpeech();
        await playing;
        f.ViewModel.IsCurrentResponseReadActive.Should().BeFalse();
    }

    private static async Task<Fixture> CreateCallSpeechResponse(CallState state = CallState.Active)
    {
        var f = new Fixture();
        await PrepareCallSpeechResponse(f, state);
        return f;
    }

    private static Communication.CallSpeechReviewTests.Clock BindCallSpeechTime(Fixture f)
    {
        var time = new Communication.CallSpeechReviewTests.Clock();
        var store = new SessionCommandStore(f);
        var workspace = new Kora.Application.Hosting.SessionWorkspaceService(store, new(f.HostStore), store,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Kora.Application.Hosting.SessionWorkspaceService>.Instance);
        var model = new SpeechSuggestionModel();
        var conversation = new Kora.Application.Hosting.SessionConversationService(store, store, workspace, store, model, time,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Kora.Application.Hosting.SessionConversationService>.Instance, execution: model);
        f.ViewModel.BindSpeechSummarySuggestions(conversation, time);
        return time;
    }

    private static async Task PrepareCallSpeechResponse(Fixture f, CallState state)
    {
        await f.ViewModel.InitializeAsync();
        f.ViewModel.DefaultResponseMode = ResponseOutputMode.Hybrid;
        f.CallState.SetState(state);
        await f.ViewModel.CallClosureTask;
        await f.RunAsync("what power action is pending");
        f.TextToSpeech.ClearSpokenResponse();
        f.Reasoner.Requests.Clear();
    }
}
