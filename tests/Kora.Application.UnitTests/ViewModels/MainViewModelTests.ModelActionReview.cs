using System.Reflection;
using AwesomeAssertions;
using Kora.Application.ViewModels;
using Kora.Core;
using Kora.Core.Commands;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task ReadProposalWithoutAnOrdinaryAnswerCannotStartPlanningOrSpeech()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.RetireCurrentResponseSpeech();
        var planner = new ResponseReadPlannerFake();
        f.ViewModel.BindResponseReadPlanner(planner, TimeProvider.System);
        var starts = f.TextToSpeech.SpeakCalls;
        await f.RunAsync("please read this answer aloud");
        f.ViewModel.HasResponseReadProposal.Should().BeFalse();
        f.ViewModel.LastResponseReadOutcome.Should().Be(Kora.Core.Voice.SpeechReadOutcome.Denied);
        f.ViewModel.Transcript.Should().Contain("Current-answer proposal unavailable");
        planner.Admissions.Should().Be(0);
        f.TextToSpeech.SpeakCalls.Should().Be(starts);
    }

    [Fact]
    public async Task DeferredReadProposalRevalidatesOwnerAfterCapturedInputAdmission()
    {
        var f = await Fixture.CreateInitializedAsync();
        var ownerEligible = true;
        f.ViewModel.BindCallOwnershipGate(() => ownerEligible);
        var armed = false;
        f.ViewModel.BindResponseReadDecisionGate(() =>
        {
            if (armed) { armed = false; ownerEligible = false; }
            return true;
        });
        f.ViewModel.DefaultResponseMode = Kora.Core.Voice.ResponseOutputMode.Hybrid;
        await f.RunAsync("what power action is pending");
        var planner = new ResponseReadPlannerFake();
        f.ViewModel.BindResponseReadPlanner(planner, TimeProvider.System);
        using var original = Kora.Core.Diagnostics.HostActivity.BeginRoot(
            Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.LocalUi),
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        var capture = typeof(MainViewModel).GetMethod("CaptureResponseReadInput", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var input = capture.Invoke(f.ViewModel, [Kora.Core.Auditing.SecurityAuditInitiator.TypedCommand, null])!;
        ((Func<bool>)input.GetType().GetProperty("Eligible")!.GetValue(input)!)().Should().BeTrue();
        var body = f.ViewModel.ResponseBody;
        var starts = f.TextToSpeech.SpeakCalls;
        armed = true;
        var request = typeof(MainViewModel).GetMethod("TryHandleResponseReadProposal", BindingFlags.Instance | BindingFlags.NonPublic)!;
        (await (Task<bool>)request.Invoke(f.ViewModel,
            ["please read this answer aloud", Kora.Core.Auditing.SecurityAuditInitiator.TypedCommand, input])!).Should().BeTrue();
        ownerEligible.Should().BeFalse();
        f.ViewModel.HasResponseReadProposal.Should().BeFalse();
        f.ViewModel.LastResponseReadOutcome.Should().Be(Kora.Core.Voice.SpeechReadOutcome.Denied);
        f.ViewModel.ResponseBody.Should().Be(body);
        planner.Admissions.Should().Be(0);
        f.TextToSpeech.SpeakCalls.Should().Be(starts);
    }

    [Fact]
    public async Task DeferredReadProposalCannotEnterWhenRetainedAnswerDecisionsOwnInput()
    {
        var f = await CreateReadableResponseAsync();
        var planner = new ResponseReadPlannerFake();
        f.ViewModel.BindResponseReadPlanner(planner, TimeProvider.System);
        f.ViewModel.BindRetainedAnswerReadDecisionGate(static () => false);
        using var original = Kora.Core.Diagnostics.HostActivity.BeginRoot(
            Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.LocalUi),
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        var body = f.ViewModel.ResponseBody;
        var starts = f.TextToSpeech.SpeakCalls;
        var request = typeof(MainViewModel).GetMethod("TryHandleResponseReadProposal", BindingFlags.Instance | BindingFlags.NonPublic)!;
        (await (Task<bool>)request.Invoke(f.ViewModel,
            ["please read this answer aloud", Kora.Core.Auditing.SecurityAuditInitiator.TypedCommand, null])!).Should().BeTrue();
        f.ViewModel.HasResponseReadProposal.Should().BeFalse();
        f.ViewModel.LastResponseReadOutcome.Should().Be(Kora.Core.Voice.SpeechReadOutcome.Denied);
        f.ViewModel.ResponseBody.Should().Be(body);
        planner.Admissions.Should().Be(0);
        f.TextToSpeech.SpeakCalls.Should().Be(starts);
    }

    [Fact]
    public async Task DeferredReadProposalCannotSelectAReplacementOrdinaryAnswerWithIdenticalText()
    {
        var f = await CreateReadableResponseAsync();
        var planner = new ResponseReadPlannerFake();
        f.ViewModel.BindResponseReadPlanner(planner, TimeProvider.System);
        using var original = Kora.Core.Diagnostics.HostActivity.BeginRoot(
            Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.LocalUi),
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        var capture = typeof(MainViewModel).GetMethod("CaptureResponseReadInput", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var input = capture.Invoke(f.ViewModel, [Kora.Core.Auditing.SecurityAuditInitiator.TypedCommand, null]);
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync("what power action is pending");
        f.ViewModel.ResponseBody.Should().Be(body);
        var starts = f.TextToSpeech.SpeakCalls;
        var request = typeof(MainViewModel).GetMethod("TryHandleResponseReadProposal", BindingFlags.Instance | BindingFlags.NonPublic)!;
        (await (Task<bool>)request.Invoke(f.ViewModel,
            ["please read this answer aloud", Kora.Core.Auditing.SecurityAuditInitiator.TypedCommand, input])!).Should().BeTrue();
        f.ViewModel.HasResponseReadProposal.Should().BeFalse();
        f.ViewModel.LastResponseReadOutcome.Should().Be(Kora.Core.Voice.SpeechReadOutcome.Denied);
        f.ViewModel.ResponseBody.Should().Be(body);
        planner.Admissions.Should().Be(0);
        f.TextToSpeech.SpeakCalls.Should().Be(starts);
    }

    [Fact]
    public async Task RetiredDetailsOfferCannotAuthorizeSpeechOrAnotherOutputDiscovery()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.CallState.SetState(Kora.Core.Communication.CallState.Clear);
        await f.Dispatcher.LastInvocation;
        BindCompletedAnswerRouting(f, _ => Task.FromResult(DetailOfferAnswer));
        await f.RunAsync("original request");
        f.ViewModel.IsDetailsOfferPending.Should().BeTrue();
        Func<bool> originalOffer = () => f.ViewModel.IsDetailsOfferPending;
        f.ViewModel.DeclineCurrentDetailsOffer();
        var discoveries = f.TextToSpeech.OutputEnumerationCalls;
        using var request = Kora.Core.Diagnostics.HostActivity.BeginRoot(
            Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.LocalUi),
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        f.ViewModel.IsSpeechResponseEnabled.Should().BeTrue();
        var speak = typeof(MainViewModel).GetMethod("TrySpeakCurrentResponseAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        (await (Task<bool>)speak.Invoke(f.ViewModel,
            [null, null, originalOffer, null, null, null, CancellationToken.None])!).Should().BeFalse();
        f.TextToSpeech.SpeakCalls.Should().Be(0);
        f.TextToSpeech.OutputEnumerationCalls.Should().Be(discoveries);
    }

    [Fact]
    public async Task OverBudgetOrdinarySpeechKeepsTheCompleteVisualResultWithoutAWindowSubscriber()
    {
        var f = new Fixture(subscribeToWindowActions: false);
        f.Preferences.SummaryLimits = new(1, 1);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("what power action is pending");
        f.ViewModel.ResponseBody.Should().Contain("Power execution is intentionally disabled");
        f.ViewModel.ResponseOutputStatus.Should().Contain("Speech withheld");
        f.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        f.TextToSpeech.SpeakCalls.Should().Be(0);
    }

    [Fact]
    public async Task DeferredReadProposalCannotStartWhileTheExactOrdinaryAnswerIsAlreadySpeaking()
    {
        var f = await Fixture.CreateInitializedAsync();
        var planner = new ResponseReadPlannerFake();
        f.ViewModel.BindResponseReadPlanner(planner, TimeProvider.System);
        using var sourceRequest = Kora.Core.Diagnostics.HostActivity.BeginRoot(
            Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.LocalUi),
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = f.RunAsync("what power action is pending");
        try
        {
            await Task.WhenAny(response, f.TextToSpeech.SpeakStarted.Task).WaitAsync(TestContext.Current.CancellationToken);
            f.TextToSpeech.SpeakStarted.Task.IsCompleted.Should().BeTrue();
            f.ViewModel.CanAnswerRemainingWork.Should().BeTrue();
            var body = f.ViewModel.ResponseBody;
            var source = typeof(MainViewModel).GetField("ordinaryResponse", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(f.ViewModel)!;
            source.GetType().GetProperty("Request")!.GetValue(source).Should().NotBeNull();
            using var original = Kora.Core.Diagnostics.HostActivity.BeginRoot(
                Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.LocalUi),
                Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
            var capture = typeof(MainViewModel).GetMethod("CaptureResponseReadInput", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var input = capture.Invoke(f.ViewModel, [Kora.Core.Auditing.SecurityAuditInitiator.TypedCommand, null])!;
            ((Func<bool>)input.GetType().GetProperty("Eligible")!.GetValue(input)!)().Should().BeTrue();
            f.ViewModel.IsSpeaking.Should().BeTrue();
            var request = typeof(MainViewModel).GetMethod("TryHandleResponseReadProposal", BindingFlags.Instance | BindingFlags.NonPublic)!;
            (await (Task<bool>)request.Invoke(f.ViewModel,
                ["please read this answer aloud", Kora.Core.Auditing.SecurityAuditInitiator.TypedCommand, input])!).Should().BeTrue();
            f.ViewModel.HasResponseReadProposal.Should().BeFalse();
            f.ViewModel.LastResponseReadOutcome.Should().Be(Kora.Core.Voice.SpeechReadOutcome.Denied);
            f.ViewModel.ResponseBody.Should().Be(body);
            f.ViewModel.Transcript.Should().Contain("Current-answer proposal unavailable");
            planner.Admissions.Should().Be(0);
            f.TextToSpeech.SpeakCalls.Should().Be(1);
        }
        finally
        {
            f.TextToSpeech.SpeakGate.TrySetResult();
            await response;
        }
    }

    [Fact]
    public async Task AdmittedVoiceCannotDispatchWhenEarlierOrdinarySpeechCannotStop()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.CommandText = "what power action is pending";
        if (!f.ViewModel.IsVoiceEnabled) { await f.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        var settings = 0;
        f.ViewModel.SettingsRequested += (_, _) => settings++;
        f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = f.ViewModel.RunTypedCommand.ExecuteAsync();
        try
        {
            await Task.WhenAny(response, f.TextToSpeech.SpeakStarted.Task).WaitAsync(TestContext.Current.CancellationToken);
            f.TextToSpeech.SpeakStarted.Task.IsCompleted.Should().BeTrue();
            f.TextToSpeech.StopException = new IOException("Owned playback could not stop.");
            await f.Voice.RaiseTranscriptAsync("Kora, open settings", 1);
            f.ViewModel.ResponseTitle.Should().Be("Speech output could not be stopped.");
            settings.Should().Be(0);
            f.ViewModel.IsSpeaking.Should().BeTrue();
        }
        finally
        {
            f.TextToSpeech.StopException = null;
            f.TextToSpeech.SpeakGate.TrySetResult();
            await response;
        }
    }

    [Fact]
    public async Task ManualCallChangesRetireCapturedOrdinarySpeechEvenAfterReturningToClear()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("what power action is pending");
        var body = f.ViewModel.ResponseBody;
        var capturedSpeech = (long)typeof(MainViewModel)
            .GetField("manualCallSpeechRevision", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.ViewModel)!;
        await f.ViewModel.EnableManualCallCommand.ExecuteAsync();
        await f.ViewModel.ClearManualCallCommand.ExecuteAsync();
        var starts = f.TextToSpeech.SpeakCalls;
        using var original = Kora.Core.Diagnostics.HostActivity.BeginRoot(
            Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.LocalUi),
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        var speak = typeof(MainViewModel).GetMethod("TrySpeakCurrentResponseAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var played = await (Task<bool>)speak.Invoke(f.ViewModel,
            [null, capturedSpeech, null, null, null, null, CancellationToken.None])!;
        played.Should().BeFalse();
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        f.ViewModel.ResponseOutputStatus.Should().Contain("retired by manual call control");
        f.TextToSpeech.SpeakCalls.Should().Be(starts);
    }

    [Theory]
    [InlineData(Kora.Core.Hosting.RequestOrigin.HostSystem)]
    [InlineData(Kora.Core.Hosting.RequestOrigin.ActivatedVoice)]
    public async Task DeferredMetadataReviewRequiresOriginalUserOriginAndItsCurrentVoicePrefix(Kora.Core.Hosting.RequestOrigin origin)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.CallState.SetState(Kora.Core.Communication.CallState.Clear);
        await f.Dispatcher.LastInvocation;
        f.ViewModel.BindVoiceOwnershipGate(static () => true);
        const string text = "review session metadata 10000000-0000-0000-0000-000000000001 for 10000000-0000-0000-0000-000000000002: Original";
        var requests = 0;
        f.ViewModel.SessionMetadataRequested += (_, _, _) => { requests++; return Task.CompletedTask; };
        using var original = Kora.Core.Diagnostics.HostActivity.BeginRoot(
            Kora.Core.Hosting.HostRequest.Create(origin),
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        var input = typeof(MainViewModel).GetMethod("CaptureManualCallInput", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(f.ViewModel, [origin, f.ViewModel.CallPolicyRevision, null, false]);
        var callback = typeof(MainViewModel).GetMethod("ExecuteSessionMetadata", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)callback.Invoke(f.ViewModel,
            [Kora.Application.Tools.SessionMetadataCommand.Parse(text, f.ViewModel.AssistantName), text, input, (Func<bool>)(static () => true)])!;
        (f.ViewModel.Transcript + " " + f.ViewModel.ResponseBody).Should().Contain("requires original private typed input");
        requests.Should().Be(0);
        f.HostStore.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Kora.Core.Dependencies.DependencyReadiness.Ready, "local model ready")]
    [InlineData(Kora.Core.Dependencies.DependencyReadiness.Failed, "local reasoning unavailable")]
    public async Task VersionReportsObservedInferenceAvailabilityWithoutStartingAModel(Kora.Core.Dependencies.DependencyReadiness readiness, string expected)
    {
        var f = new Fixture();
        f.ModelExecutionPreferences.Save(new Kora.Core.Configuration.ModelExecutionSettings(true, false));
        f.Probe.Status = new("local.inference", "Local inference", readiness, "Observed inference status.");
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("what version are you running");
        f.ViewModel.ResponseBody.Should().Contain(expected);
        f.HostStore.Records.Select(record => record.State).Should().Equal(
            Kora.Core.Hosting.HostTaskState.IntentRecorded, Kora.Core.Hosting.HostTaskState.DispatchRecorded, Kora.Core.Hosting.HostTaskState.Succeeded);
    }

    [Fact]
    public async Task CurrentTaskProgressUsesTheAdmittedSetupPercentageWithoutModelReasoning()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local inference", Kora.Core.Dependencies.DependencyReadiness.Missing, "Needs approved setup.");
        await f.ViewModel.InitializeAsync();
        f.ViewModel.CanInstallLocalModel.Should().BeTrue();
        f.LocalModel.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var setup = f.ViewModel.InstallLocalModelAsync();
        try
        {
            await Task.WhenAny(setup, f.LocalModel.Started.Task).WaitAsync(TestContext.Current.CancellationToken);
            f.LocalModel.Started.Task.IsCompleted.Should().BeTrue();
            await f.RunAsync("what is the current task status");
            f.ViewModel.ResponseBody.Should().Contain("42%").And.Contain("Downloading the selected local model.");
            f.HostStore.Records.Should().BeEmpty();
        }
        finally
        {
            f.LocalModel.Gate.TrySetResult();
            await setup;
        }
    }

    [Theory]
    [InlineData("show session metrics", "Session metrics are unavailable")]
    [InlineData("session metrics active", "Session metrics are unavailable")]
    [InlineData("session remaining 10000000-0000-0000-0000-000000000001", "Remaining-work answer unavailable")]
    [InlineData("session remaining invalid", "Remaining-work answer unavailable")]
    [InlineData("open conversation \"Work\"", "Conversation opening unavailable")]
    public async Task QuestionArrivingAfterInputAdmissionCannotBeDisplacedByLocalObservation(string text, string refusal)
    {
        var f = await Fixture.CreateInitializedAsync();
        await using var details = new PendingDetailsQuestion(f);
        var injected = false;
        Task? question = null;
        f.ViewModel.CommandText = text;
        f.ViewModel.PropertyChanged += (_, args) =>
        {
            if (injected || !string.Equals(args.PropertyName, nameof(MainViewModel.QuietHoursReviewText), StringComparison.Ordinal)) { return; }
            injected = true;
            question = details.PresentAsync();
        };
        var tasks = f.HostStore.Records.Count;
        await f.ViewModel.RunTypedCommand.ExecuteAsync();
        injected.Should().BeTrue();
        await question!;
        (f.ViewModel.Transcript + " " + f.ViewModel.ResponseBody).Should().Contain(refusal);
        f.ViewModel.HasDetailsQuestion.Should().BeTrue();
        f.HostStore.Records.Count.Should().Be(tasks);
        f.TextToSpeech.SpeakCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("open conversation \"Work\"", "Conversation opening unavailable")]
    [InlineData("show session metrics", "Session metrics are unavailable")]
    [InlineData("session remaining 10000000-0000-0000-0000-000000000001", "Remaining-work answer unavailable")]
    public async Task CapturedEditorInputCannotBeRelabelledAsSessionObservation(string text, string refusal)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.CallState.SetState(Kora.Core.Communication.CallState.Clear);
        await f.Dispatcher.LastInvocation;
        f.ViewModel.CanAnswerRemainingWork.Should().BeTrue();
        f.ViewModel.CommandText = text;
        var tasks = f.HostStore.Records.Count;
        using var original = Kora.Core.Diagnostics.HostActivity.BeginRoot(
            Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.LocalUi),
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        var dispatch = typeof(MainViewModel).GetMethod("HandleTranscriptAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)dispatch.Invoke(f.ViewModel,
            [text, 1f, Kora.Core.Auditing.SecurityAuditInitiator.TypedCommand, null, null, null, null, null, null, EditorVoiceTarget(), null])!;
        (f.ViewModel.Transcript + " " + f.ViewModel.ResponseBody).Should().Contain(refusal);
        f.HostStore.Records.Count.Should().Be(tasks);
        f.TextToSpeech.SpeakCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("ExecuteCustomizationDiscovery", "discover customizations Which can help?", "cannot replace a pending question")]
    [InlineData("ExecuteInstructionUseCommand", InstructionRequest, "unavailable while a question")]
    [InlineData("ExecuteKnowledgeSourceAnswer", "answer using source \"Notes\": Exact request", "unavailable while a question")]
    [InlineData("ExecuteSkillReviewCommand", "open skill packages", "unavailable while a question")]
    [InlineData("ExecuteSpeechCommandAsync", "list speech settings", "pending question")]
    [InlineData("ExecuteSpeechSummarySuggestionAsync", "Review", "Speech limit review blocked.")]
    [InlineData("ExecuteSessionMetadata", "review session metadata 10000000-0000-0000-0000-000000000001 for 10000000-0000-0000-0000-000000000002: Original", "requires original private typed input")]
    [InlineData("ExecuteResponseWindowCommandAsync", "set response-window.topmost to false", "Resolve pending questions")]
    [InlineData("ExecuteResponseWindowCommandAsync", "set response-window.topmost to invalid", "Clarify the response-window setting.")]
    [InlineData("ExecuteManualCallCommandAsync", "set call.manual-active to on", "pending question")]
    [InlineData("ExecuteManualCallCommandAsync", "set call.manual-active to invalid", "call.manual-active")]
    [InlineData("ExecuteWebPageCommandAsync", "get web page https://example.invalid/", "")]
    public async Task DeferredNativeCallbacksCannotConsumeAnExistingDetailsQuestion(string methodName, string text, string refusal)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.CallState.SetState(Kora.Core.Communication.CallState.Clear);
        await f.Dispatcher.LastInvocation;
        f.ViewModel.BindVoiceOwnershipGate(static () => true);
        await using var details = new PendingDetailsQuestion(f);
        await details.PresentAsync();
        var question = f.ViewModel.CurrentDetailsOffer;
        var questionText = f.ViewModel.DetailsQuestionText;
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        var tasks = f.HostStore.Records.Count;
        var audit = f.Audit.Events.Count;
        var speech = f.TextToSpeech.SpeakCalls;
        var stops = f.TextToSpeech.StopCalls;
        using var original = Kora.Core.Diagnostics.HostActivity.BeginRoot(
            Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.LocalUi),
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        var input = typeof(MainViewModel).GetMethod("CaptureManualCallInput", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(f.ViewModel, [Kora.Core.Hosting.RequestOrigin.LocalUi, f.ViewModel.CallPolicyRevision, null, false]);
        var callback = typeof(MainViewModel).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        var parameters = callback.GetParameters();
        var commandType = parameters[0].ParameterType;
        object command;
        if (commandType.IsEnum) { command = Enum.Parse(commandType, text); }
        else
        {
            var parse = commandType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("The maintained callback command has no parser.");
            var parseArguments = parse.GetParameters().Select((parameter, index) =>
                index == 0 ? (object?)text : parameter.ParameterType == typeof(string)
                    ? f.ViewModel.AssistantName : parameter.HasDefaultValue
                        ? parameter.DefaultValue : throw new InvalidOperationException("The maintained parser needs an explicit argument.")).ToArray();
            command = parse.Invoke(null, parseArguments)
                ?? throw new InvalidOperationException("The regression input did not parse into its maintained command.");
        }
        var arguments = parameters.Select((parameter, index) => index == 0 ? command
            : parameter.ParameterType == typeof(string) ? text
            : parameter.ParameterType == typeof(Kora.Core.Auditing.SecurityAuditInitiator) ? (object)Kora.Core.Auditing.SecurityAuditInitiator.TypedCommand
            : parameter.ParameterType == typeof(CancellationToken) ? TestContext.Current.CancellationToken
            : parameter.ParameterType == typeof(bool) ? true
            : parameter.ParameterType == typeof(Func<bool>) ? (Func<bool>)(() => true)
            : parameter.Name is "input" or "originalInput" ? input
            : throw new InvalidOperationException("The maintained callback needs an explicit argument.")).ToArray();
        await (Task)callback.Invoke(f.ViewModel, arguments)!;
        if (methodName is "ExecuteWebPageCommandAsync")
        {
            f.ViewModel.ResponseTitle.Should().Be(title);
            f.ViewModel.ResponseBody.Should().Be(body);
        }
        else { (f.ViewModel.Transcript + " " + f.ViewModel.ResponseTitle + " " + f.ViewModel.ResponseBody).Should().Contain(refusal); }
        f.ViewModel.CurrentDetailsOffer.Should().BeSameAs(question);
        f.ViewModel.DetailsQuestionText.Should().Be(questionText);
        f.HostStore.Records.Count.Should().Be(tasks);
        f.Audit.Events.Should().HaveCount(audit);
        f.TextToSpeech.SpeakCalls.Should().Be(speech);
        f.TextToSpeech.StopCalls.Should().Be(stops);
        f.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task PendingDetailsRetiresNativeQuestionNavigationAndUndoAdmission()
    {
        var f = await Fixture.CreateInitializedAsync();
        var admitted = f.ViewModel.CaptureQuestionNoticeNavigationEligibility();
        admitted().Should().BeTrue();
        await using var details = new PendingDetailsQuestion(f);
        await details.PresentAsync();
        var question = f.ViewModel.CurrentDetailsOffer;
        admitted().Should().BeFalse();
        f.ViewModel.CaptureQuestionNoticeNavigationEligibility()().Should().BeFalse();
        await f.ViewModel.ExecuteSettingsUndoAsync(
            Kora.Application.Configuration.RegisteredSettings.RegisteredSettingSuggestionOperation.Review,
            Kora.Core.Auditing.SecurityAuditInitiator.LocalUser, TestContext.Current.CancellationToken);
        f.ViewModel.Transcript.Should().Contain("Nothing was applied.");
        var body = f.ViewModel.ResponseBody;
        f.ViewModel.ReportEvidencePresentationUnavailable();
        f.ViewModel.Transcript.Should().Contain("reader text scale is unconfirmed");
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.CurrentDetailsOffer.Should().BeSameAs(question);
    }

    [Fact]
    public async Task ArtifactFreshnessCompletedAfterCancellationCannotAuthorizeInvocation()
    {
        var f = await Fixture.CreateInitializedAsync();
        var artifact = f.ArtifactCatalogue.SourceCandidates.Single() with { UseControlId = "file-artifact" };
        using var cancellation = new CancellationTokenSource();
        var observations = 0;
        f.ViewModel.BindFileArtifactFreshness((candidate, token) =>
        {
            candidate.Should().BeSameAs(artifact);
            token.Should().Be(cancellation.Token);
            observations++;
            cancellation.Cancel();
            return Task.FromResult(true);
        });
        var tasks = f.HostStore.Records.Count;
        var observe = typeof(MainViewModel).GetMethod("IsArtifactSourceCurrent", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var current = await (Task<bool>)observe.Invoke(f.ViewModel, [artifact, cancellation.Token])!;
        current.Should().BeFalse();
        observations.Should().Be(1);
        f.HostStore.Records.Count.Should().Be(tasks);
        f.Session.LockCalls.Should().Be(0);
        f.TextToSpeech.SpeakCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("unmatched", false)]
    [InlineData("unmatched", true)]
    [InlineData("artifact", false)]
    [InlineData("artifact", true)]
    [InlineData("clarification", false)]
    [InlineData("clarification", true)]
    public async Task RetiredModelEntryRejectsImmediatelyWithoutTaskSpeechOrSend(string entry, bool voiceOnly)
    {
        var f = await Fixture.CreateInitializedAsync();
        if (voiceOnly) { f.ViewModel.TaskResponseMode = Kora.Core.Voice.ResponseOutputMode.VoiceOnly; }
        await f.ViewModel.ResponseOutputUpdate;
        var sends = 0;
        f.ViewModel.BindSessionRouting((_, _, _) =>
        {
            sends++;
            throw new InvalidOperationException("Artifact and clarification rejection must not route a conversation.");
        });
        if (entry is "unmatched")
        {
            f.ViewModel.BindSessionRouting(null);
            f.ViewModel.TryPresentCapabilityCommand("explain this unmatched request").Should().BeFalse();
        }
        f.ViewModel.BindFileArtifactFreshness((_, _) =>
            throw new InvalidOperationException("A retired non-prompt artifact must not start preparation."));
        var states = new List<AssistantState>();
        var titles = new List<string>();
        var taskChanges = 0;
        f.ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.State)) { states.Add(f.ViewModel.State); }
            if (args.PropertyName is nameof(MainViewModel.ResponseTitle)) { titles.Add(f.ViewModel.ResponseTitle); }
            if (args.PropertyName is nameof(MainViewModel.SetupTasks)) { taskChanges++; }
        };
        var tasks = f.ViewModel.SetupTasks.ToArray();
        var hostTasks = f.HostStore.Records.Count;
        var stops = f.TextToSpeech.StopCalls;
        var discoveries = f.TextToSpeech.OutputEnumerationCalls;
        if (entry is "clarification")
        {
            // The retired UI has no caller; exercise its retained fail-closed re-entry guard.
            var guard = typeof(MainViewModel).GetMethod("SubmitModelQuestionAnswerAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var operation = (Task)guard.Invoke(f.ViewModel, null)!;
            operation.IsCompletedSuccessfully.Should().BeTrue();
            await operation;
        }
        else { await f.RunAsync(entry is "artifact" ? "/lock original request" : "explain this unmatched request"); }
        f.ViewModel.ResponseTitle.Should().Be("Model request unavailable.");
        f.ViewModel.ResponseBody.Should().StartWith("Nothing was sent.");
        f.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        states.Should().NotContain(AssistantState.Calculating);
        titles.Should().NotContain(title => title.Contains("Thinking locally", StringComparison.Ordinal));
        f.ViewModel.SetupTasks.Should().Equal(tasks);
        taskChanges.Should().Be(0);
        f.HostStore.Records.Count.Should().Be(hostTasks);
        f.ViewModel.IsLocalTaskCancellable.Should().BeFalse();
        f.ViewModel.IsSpeaking.Should().BeFalse();
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.TextToSpeech.StopCalls.Should().Be(stops);
        f.TextToSpeech.OutputEnumerationCalls.Should().Be(discoveries);
        sends.Should().Be(0);
        f.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("list grants")]
    [InlineData("manage grants")]
    [InlineData("review pending action")]
    [InlineData("allow once")]
    [InlineData("always allow")]
    public async Task RetiredNamedApprovalCommandsCannotExecuteAnAction(string text)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync(text);
        f.ViewModel.ResponseTitle.Should().Be("Model request unavailable.");
        f.ViewModel.ResponseBody.Should().StartWith("Nothing was sent.");
        f.Session.LockCalls.Should().Be(0);
        f.ViewModel.IsResponseInteractionPending.Should().BeFalse();
        f.TextToSpeech.SpokenText.Should().BeNull();
    }

    private sealed class PendingDetailsQuestion(Fixture fixture) : IAsyncDisposable
    {
        private readonly Kora.Application.UnitTests.Presentation.DetailsOfferServiceTests.DetailsOfferTestSource source = new();
        private Kora.Application.Presentation.DetailsOfferService? offers;

        internal async Task PresentAsync()
        {
            offers ??= new(source.Store, source.Time);
            fixture.ViewModel.BindDetailsOfferPriority(static () => true);
            fixture.ViewModel.BindDetailsOffers(offers, () => () => source.Live, (_, _) => Task.FromResult(true));
            fixture.ViewModel.PresentDetailsOffer(source.Request);
            await fixture.ViewModel.DetailsOfferPresented;
            fixture.ViewModel.HasDetailsQuestion.Should().BeTrue();
        }

        public async ValueTask DisposeAsync()
        {
            if (offers is not null) { await offers.DisposeAsync(); }
            source.Dispose();
        }
    }
}
