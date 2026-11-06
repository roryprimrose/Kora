using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Kora.Tools.Speech;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("Could you please read this answer out loud for me?")]
    [InlineData("Please speak the current response aloud.")]
    [InlineData("read this answer aloud")]
    [InlineData("Please read this answer to me.")]
    [InlineData("Could you read the current answer to me?")]
    [InlineData("Please read this response to me.")]
    [InlineData("Please read the current response to me.")]
    [InlineData("read this current answer")]
    public async Task ResponseReadProposalCompletesOriginalRequestOnlyAfterJoinedPlanningAndSeparateDecision(string request)
    {
        var fixture = await CreateReadableResponseAsync();
        var planner = new ResponseReadPlannerFake();
        fixture.ViewModel.BindResponseReadPlanner(planner, TimeProvider.System);
        var title = fixture.ViewModel.ResponseTitle;
        var body = fixture.ViewModel.ResponseBody;
        var calls = fixture.TextToSpeech.SpeakCalls;
        var reviewShown = false;
        fixture.ViewModel.WindowActionRequested += (_, _) => reviewShown = true;
        await fixture.RunAsync(request);
        reviewShown.Should().BeTrue();
        fixture.ViewModel.HasResponseReadProposal.Should().BeTrue();
        fixture.ViewModel.ResponseReadProposalReview.Should().Contain(request).And.Contain("Only host session/task receipts");
        planner.Admissions.Should().Be(0);
        await fixture.ViewModel.CaptureResponseReadProposalDecision(confirm: true)();
        fixture.TextToSpeech.SpeakCalls.Should().Be(calls);
        await fixture.ViewModel.PlanCurrentResponseReadCommand.ExecuteAsync();
        planner.Admissions.Should().Be(1);
        planner.Disposals.Should().Be(1);
        planner.Context!.ToolMode.Should().Be(ModelToolMode.None);
        planner.Context.Tools.Should().BeEmpty();
        planner.Context.UserRequest.Should().Be(request);
        planner.Context.Evidence.Should().ContainSingle().Which.Disclosure.Should().Be(ModelEvidenceDisclosure.LocalOnly);
        System.Text.Encoding.UTF8.GetString(planner.Context.Serialize()).Should().NotContain(body);
        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.TextToSpeech.SpeakCalls.Should().Be(calls);
        fixture.ViewModel.CanConfirmCurrentResponseRead.Should().BeTrue();
        fixture.ViewModel.CanPlanCurrentResponseRead.Should().BeFalse();
        fixture.ViewModel.ResponseReadProposalReview.Should().Contain("local planner has joined");
        var confirm = fixture.ViewModel.CaptureResponseReadProposalDecision(confirm: true);
        await confirm();
        await confirm();
        fixture.ViewModel.HasResponseReadProposal.Should().BeFalse();
        fixture.ViewModel.LastResponseReadOutcome.Should().Be(SpeechReadOutcome.Played);
        fixture.TextToSpeech.SpeakCalls.Should().Be(calls + 1);
        fixture.TextToSpeech.SpokenText.Should().Be($"{title}. {body}");
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.Session.LockCalls.Should().Be(0);
        var audits = fixture.Audit.Events.Where(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)).ToArray();
        audits.Should().HaveCount(2);
        audits[0].Outcome.Should().Be(SecurityAuditOutcome.Requested);
        audits[1].Outcome.Should().Be(SecurityAuditOutcome.Succeeded);
        audits[1].TargetId.Should().Be(audits[0].TargetId);
        audits[0].Initiator.Should().Be(SecurityAuditInitiator.TypedCommand);
    }

    [Theory]
    [InlineData("{\"target\":\"forged\"}")]
    [InlineData("{\"target\":null}")]
    [InlineData("{\"target\":42}")]
    [InlineData("{\"text\":\"speak arbitrary model prose\"}")]
    [InlineData("speak arbitrary model prose")]
    [InlineData("[]")]
    [InlineData("{\"target\":\"x\",\"target\":\"x\"}")]
    [InlineData("{\"target\":\"x\",\"text\":\"extra\"}")]
    public async Task ResponseReadProposalRejectsUnservedReferencesOrModelProseWithoutReplacingAnswer(string answer)
    {
        var fixture = await CreateReadableResponseAsync();
        var planner = new ResponseReadPlannerFake { Answer = answer };
        fixture.ViewModel.BindResponseReadPlanner(planner, TimeProvider.System);
        var body = fixture.ViewModel.ResponseBody;
        await fixture.RunAsync("please read this answer aloud");
        await fixture.ViewModel.PlanCurrentResponseReadCommand.ExecuteAsync();
        await fixture.ViewModel.CaptureResponseReadProposalDecision(confirm: true)();
        fixture.ViewModel.HasResponseReadProposal.Should().BeFalse();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.Reasoner.Requests.Should().BeEmpty();
        planner.Disposals.Should().Be(1);
        fixture.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("planning")]
    [InlineData("review")]
    public async Task ResponseReadProposalIdenticalTextReplacementInvalidatesCapturedTarget(string phase)
    {
        var fixture = await CreateReadableResponseAsync();
        var planner = new ResponseReadPlannerFake();
        fixture.ViewModel.BindResponseReadPlanner(planner, TimeProvider.System);
        var body = fixture.ViewModel.ResponseBody;
        await fixture.RunAsync("please read this answer aloud");
        var confirm = fixture.ViewModel.CaptureResponseReadProposalDecision(confirm: true);
        if (string.Equals(phase, "planning", StringComparison.Ordinal))
        {
            planner.BeforeRun = () => fixture.ViewModel.RetireCurrentResponseSpeech();
        }
        await fixture.ViewModel.PlanCurrentResponseReadCommand.ExecuteAsync();
        if (string.Equals(phase, "review", StringComparison.Ordinal)) { await fixture.RunAsync("what power action is pending"); }
        fixture.TextToSpeech.ClearSpokenResponse();
        await confirm();
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.ViewModel.HasResponseReadProposal.Should().BeFalse();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        planner.Disposals.Should().Be(1);
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("VisualOnly")]
    [InlineData("call")]
    [InlineData("lock")]
    [InlineData("owner")]
    [InlineData("speech")]
    [InlineData("decision")]
    public async Task ResponseReadProposalRevalidatesOriginalAdmissionAndNeverReplaysOnRecovery(string change)
    {
        var fixture = await CreateReadableResponseAsync();
        var planner = new ResponseReadPlannerFake();
        var time = new ResponseReadTime();
        fixture.ViewModel.BindResponseReadPlanner(planner, time);
        var body = fixture.ViewModel.ResponseBody;
        await fixture.RunAsync("please read this answer aloud");
        await fixture.ViewModel.PlanCurrentResponseReadCommand.ExecuteAsync();
        var confirm = fixture.ViewModel.CaptureResponseReadProposalDecision(confirm: true);
        switch (change)
        {
            case "expiry": time.Now = time.Now.AddMinutes(2); break;
            case "VisualOnly": fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VisualOnly; break;
            case "call": fixture.CallState.SetState(Kora.Core.Communication.CallState.Active); await fixture.ViewModel.CallClosureTask; break;
            case "lock": fixture.Session.IsUnlocked = false; break;
            case "owner": fixture.ViewModel.BindCallOwnershipGate(static () => false); break;
            case "speech":
                fixture.Preferences.SummaryLimits = new(1, 1);
                fixture.SpeechConfiguration.Reload();
                break;
            case "decision": fixture.ViewModel.BindResponseReadDecisionGate(static () => false); break;
        }
        await confirm();
        fixture.ViewModel.LastResponseReadOutcome.Should().Be(SpeechReadOutcome.Denied);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.Session.IsUnlocked = true;
        fixture.CallState.SetState(Kora.Core.Communication.CallState.Clear);
        await fixture.ViewModel.CallClosureTask;
        await confirm();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseReadProposalDoesNotAnswerOrRetireAnExistingQuestionOrApproval(bool approval)
    {
        var fixture = CreateSummaryFixture(SpeechProviderIds.Windows);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindResponseReadPlanner(new ResponseReadPlannerFake(), TimeProvider.System);
        if (approval) { fixture.Reasoner.Action = Kora.Core.Commands.BuiltInAction.EnableHostedModels; }
        else { fixture.Reasoner.Question = new("Which color?", ["Blue", "Green"]); }
        await fixture.RunAsync("choose something");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.Reasoner.Requests.Clear();
        fixture.TextToSpeech.ClearSpokenResponse();
        var body = fixture.ViewModel.ResponseBody;
        await fixture.RunAsync("please read this answer aloud");
        fixture.ViewModel.IsResponseInteractionPending.Should().BeTrue();
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.ViewModel.HasResponseReadProposal.Should().BeFalse();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.ViewModel.HostedModelsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task ResponseReadProposalCannotPublishReviewOrPlayBeforePlannerDisposalJoins()
    {
        var fixture = await CreateReadableResponseAsync();
        var planner = new ResponseReadPlannerFake
        {
            DisposeGate = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        fixture.ViewModel.BindResponseReadPlanner(planner, TimeProvider.System);
        await fixture.RunAsync("please read this answer aloud");
        var planning = fixture.ViewModel.PlanCurrentResponseReadCommand.ExecuteAsync();
        await planner.DisposeEntered.Task;
        try
        {
            fixture.ViewModel.CanConfirmCurrentResponseRead.Should().BeFalse();
            await fixture.ViewModel.CaptureResponseReadProposalDecision(confirm: true)();
            fixture.TextToSpeech.SpokenText.Should().BeNull();
        }
        finally { planner.DisposeGate.TrySetResult(); }
        await planning;
        fixture.ViewModel.CanConfirmCurrentResponseRead.Should().BeTrue();
    }

    [Fact]
    public async Task ResponseReadProposalCancellationJoinsIgnoringPlannerAndSuppressesLateOutput()
    {
        var fixture = await CreateReadableResponseAsync();
        var planner = new ResponseReadPlannerFake
        {
            RunGate = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        fixture.ViewModel.BindResponseReadPlanner(planner, TimeProvider.System);
        var body = fixture.ViewModel.ResponseBody;
        await fixture.RunAsync("please read this answer aloud");
        var planning = fixture.ViewModel.PlanCurrentResponseReadCommand.ExecuteAsync();
        await planner.RunEntered.Task;
        var cancellation = fixture.ViewModel.CancelCurrentTaskAsync();
        await planner.Cancelled.Task;
        try
        {
            cancellation.IsCompleted.Should().BeFalse();
            fixture.ViewModel.HasResponseReadProposal.Should().BeFalse();
            fixture.ViewModel.ResponseBody.Should().Be(body);
        }
        finally { planner.RunGate.TrySetResult(); }
        await Task.WhenAll(planning, cancellation);
        fixture.ViewModel.LastResponseReadOutcome.Should().Be(SpeechReadOutcome.Cancelled);
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        planner.Disposals.Should().Be(1);
    }

    [Fact]
    public async Task ResponseReadProposalReportsActualPlaybackFailureWithoutPlannerProseOrRetry()
    {
        var fixture = await CreateReadableResponseAsync();
        var planner = new ResponseReadPlannerFake();
        fixture.ViewModel.BindResponseReadPlanner(planner, TimeProvider.System);
        var body = fixture.ViewModel.ResponseBody;
        await fixture.RunAsync("please read this answer aloud");
        await fixture.ViewModel.PlanCurrentResponseReadCommand.ExecuteAsync();
        fixture.TextToSpeech.SpeakException = new InvalidOperationException("private-provider-content");
        var confirm = fixture.ViewModel.CaptureResponseReadProposalDecision(confirm: true);
        await confirm();
        fixture.ViewModel.LastResponseReadOutcome.Should().Be(SpeechReadOutcome.Failed);
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.Audit.Events.Should().Contain(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Failed);
        fixture.TextToSpeech.SpeakException = null;
        await confirm();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        planner.Admissions.Should().Be(1);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("unknown")]
    [InlineData("cancelled")]
    [InlineData("admission")]
    [InlineData("throw")]
    [InlineData("dispose")]
    [InlineData("operation-cancel")]
    [InlineData("oversized")]
    public async Task ResponseReadProposalContainsPlannerFailuresWithoutPlaybackOrAnswerSubstitution(string failure)
    {
        var fixture = await CreateReadableResponseAsync();
        var planner = new ResponseReadPlannerFake
        {
            Failure = failure,
            Answer = string.Equals(failure, "oversized", StringComparison.Ordinal) ? new string('x', 1025) : null,
        };
        fixture.ViewModel.BindResponseReadPlanner(planner, TimeProvider.System);
        var body = fixture.ViewModel.ResponseBody;
        await fixture.RunAsync("please read this answer aloud");
        await fixture.ViewModel.PlanCurrentResponseReadCommand.ExecuteAsync();
        fixture.ViewModel.HasResponseReadProposal.Should().BeFalse();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.ViewModel.LastResponseReadOutcome.Should().NotBe(SpeechReadOutcome.Played);
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ResponseReadProposalRejectsAReferenceServedToAnEarlierReview()
    {
        var fixture = await CreateReadableResponseAsync();
        var planner = new ResponseReadPlannerFake();
        fixture.ViewModel.BindResponseReadPlanner(planner, TimeProvider.System);
        await fixture.RunAsync("please read this answer aloud");
        await fixture.ViewModel.PlanCurrentResponseReadCommand.ExecuteAsync();
        using var prior = JsonDocument.Parse(planner.Context!.Evidence[0].Content);
        planner.Answer = JsonSerializer.Serialize(new { target = prior.RootElement.GetProperty("target").GetString() });
        var stale = fixture.ViewModel.CaptureResponseReadProposalDecision(confirm: true);
        await fixture.RunAsync("please read this answer aloud again");
        await fixture.ViewModel.PlanCurrentResponseReadCommand.ExecuteAsync();
        await stale();
        fixture.ViewModel.HasResponseReadProposal.Should().BeFalse();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        planner.Admissions.Should().Be(2);
    }

    [Theory]
    [InlineData("interpret current response read")]
    [InlineData("confirm current response read")]
    [InlineData("cancel current response read")]
    public async Task ResponseReadProposalExactDecisionsRequireTheOriginalReview(string decision)
    {
        var fixture = await CreateReadableResponseAsync();
        fixture.ViewModel.BindResponseReadPlanner(new ResponseReadPlannerFake(), TimeProvider.System);
        var body = fixture.ViewModel.ResponseBody;
        await fixture.RunAsync(decision);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ResponseReadProposalTypedDecisionCommandsCompleteTheSameExactAction()
    {
        var fixture = await CreateReadableResponseAsync();
        fixture.ViewModel.BindResponseReadPlanner(new ResponseReadPlannerFake(), TimeProvider.System);
        await fixture.RunAsync("please read this answer aloud");
        await fixture.RunAsync("interpret current response read");
        await fixture.RunAsync("confirm current response read");
        fixture.ViewModel.LastResponseReadOutcome.Should().Be(SpeechReadOutcome.Played);
        fixture.TextToSpeech.SpokenText.Should().NotBeNull();
        await fixture.RunAsync("please read this answer aloud");
        fixture.TextToSpeech.ClearSpokenResponse();
        await fixture.RunAsync("cancel current response read");
        fixture.ViewModel.LastResponseReadOutcome.Should().Be(SpeechReadOutcome.Cancelled);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseReadProposalPreservesOriginalVoiceAuthorityAcrossNativeReview(bool revoke)
    {
        var fixture = await CreateReadableResponseAsync();
        fixture.ViewModel.BindResponseReadPlanner(new ResponseReadPlannerFake(), TimeProvider.System);
        await fixture.RaiseActivatedTranscriptAsync("Kora, please read this answer aloud", 1);
        fixture.ViewModel.HasResponseReadProposal.Should().BeTrue();
        await fixture.ViewModel.PlanCurrentResponseReadCommand.ExecuteAsync();
        var confirm = fixture.ViewModel.CaptureResponseReadProposalDecision(confirm: true);
        if (revoke) { fixture.Voice.AdvanceGeneration(); }
        await confirm();
        fixture.ViewModel.LastResponseReadOutcome.Should().Be(revoke ? SpeechReadOutcome.Denied : SpeechReadOutcome.Played);
        if (revoke) { fixture.TextToSpeech.SpokenText.Should().BeNull(); }
        else
        {
            fixture.Audit.Events.Should().Contain(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
                && item.Initiator == SecurityAuditInitiator.VoiceCommand && item.Outcome == SecurityAuditOutcome.Succeeded);
        }
    }

    [Fact]
    public async Task ResponseReadProposalCloseJoinsIgnoringPlannerBeforeDisposalCompletes()
    {
        var fixture = await CreateReadableResponseAsync();
        var planner = new ResponseReadPlannerFake { RunGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        fixture.ViewModel.BindResponseReadPlanner(planner, TimeProvider.System);
        await fixture.RunAsync("please read this answer aloud");
        var planning = fixture.ViewModel.PlanCurrentResponseReadCommand.ExecuteAsync();
        await planner.RunEntered.Task;
        var close = fixture.ViewModel.DisposeAsync().AsTask();
        await planner.Cancelled.Task;
        try { close.IsCompleted.Should().BeFalse(); }
        finally { planner.RunGate.TrySetResult(); }
        await Task.WhenAll(planning, close);
        planner.Disposals.Should().Be(1);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.HasResponseReadProposal.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseReadProposalAuditFailureNeverClaimsPlayedOrSubstitutesSource(bool terminal)
    {
        var fixture = await CreateReadableResponseAsync();
        fixture.ViewModel.BindResponseReadPlanner(new ResponseReadPlannerFake(), TimeProvider.System);
        var body = fixture.ViewModel.ResponseBody;
        await fixture.RunAsync("please read this answer aloud");
        await fixture.ViewModel.PlanCurrentResponseReadCommand.ExecuteAsync();
        fixture.Audit.BeforeWrite = audit =>
        {
            if (string.Equals(audit.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
                && terminal == (audit.Outcome == SecurityAuditOutcome.Succeeded))
            { throw new IOException("private-audit-content"); }
        };
        await fixture.ViewModel.CaptureResponseReadProposalDecision(confirm: true)();
        fixture.ViewModel.LastResponseReadOutcome.Should().Be(SpeechReadOutcome.Unknown);
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.ViewModel.HasResponseReadProposal.Should().BeFalse();
        fixture.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("invocation")]
    [InlineData("complete")]
    [InlineData("stopped")]
    [InlineData("foreign-activity")]
    public async Task ResponseReadProposalRejectsNonOriginalOrEndedDecisionContexts(string kind)
    {
        var fixture = await CreateReadableResponseAsync();
        fixture.ViewModel.BindResponseReadPlanner(new ResponseReadPlannerFake(), TimeProvider.System);
        await fixture.RunAsync("please read this answer aloud");
        await fixture.ViewModel.PlanCurrentResponseReadCommand.ExecuteAsync();
        var confirm = fixture.ViewModel.CaptureResponseReadProposalDecision(confirm: true);
        var request = HostRequest.Create(string.Equals(kind, "host", StringComparison.Ordinal) ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        if (string.Equals(kind, "invocation", StringComparison.Ordinal))
        { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
        using (var context = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            if (string.Equals(kind, "complete", StringComparison.Ordinal)) { context.Complete(HostOperationOutcome.Completed); }
            if (string.Equals(kind, "stopped", StringComparison.Ordinal)) { context.Activity!.Stop(); }
            using var foreign = string.Equals(kind, "foreign-activity", StringComparison.Ordinal)
                ? new System.Diagnostics.Activity("test.foreign").Start() : null;
            await confirm();
        }
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.HasResponseReadProposal.Should().BeTrue();
    }

    [Theory]
    [InlineData("please read this answer aloud\n")]
    [InlineData("please read this answer aloud\0")]
    public async Task ResponseReadProposalRejectsControlCharactersWithoutModelInput(string text)
    {
        var fixture = await CreateReadableResponseAsync();
        var planner = new ResponseReadPlannerFake();
        fixture.ViewModel.BindResponseReadPlanner(planner, TimeProvider.System);
        var body = fixture.ViewModel.ResponseBody;
        await fixture.RunAsync(text);
        fixture.ViewModel.HasResponseReadProposal.Should().BeFalse();
        fixture.ViewModel.ResponseBody.Should().Be(body);
        planner.Admissions.Should().Be(0);
    }

    [Fact]
    public async Task ResponseReadProposalRejectsOversizedRequestsAndMissingPlanner()
    {
        var fixture = await CreateReadableResponseAsync();
        var body = fixture.ViewModel.ResponseBody;
        await fixture.RunAsync("please read this answer aloud");
        fixture.ViewModel.HasResponseReadProposal.Should().BeFalse();
        fixture.ViewModel.BindResponseReadPlanner(new ResponseReadPlannerFake(), TimeProvider.System);
        await fixture.RunAsync("please read this answer aloud " + new string('x', 4096));
        fixture.ViewModel.HasResponseReadProposal.Should().BeFalse();
        fixture.ViewModel.ResponseBody.Should().Be(body);
    }

    [Fact]
    public async Task ResponseReadProposalDoesNotReenterPlannerOrStartOrdinaryWorkWhileJoining()
    {
        var fixture = await CreateReadableResponseAsync();
        var planner = new ResponseReadPlannerFake { RunGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        fixture.ViewModel.BindResponseReadPlanner(planner, TimeProvider.System);
        await fixture.RunAsync("please read this answer aloud");
        var planning = fixture.ViewModel.PlanCurrentResponseReadCommand.ExecuteAsync();
        await planner.RunEntered.Task;
        try
        {
            fixture.ViewModel.ResponseReadProposalReview.Should().Contain("Interpreting locally");
            fixture.ViewModel.CanPlanCurrentResponseRead.Should().BeFalse();
            await fixture.RunAsync("interpret current response read");
            await fixture.RunAsync("please read this answer aloud again");
            await fixture.RunAsync("some unrelated original request");
            planner.Admissions.Should().Be(1);
            fixture.Reasoner.Requests.Should().BeEmpty();
        }
        finally { planner.RunGate.TrySetResult(); }
        await planning;
    }

    [Fact]
    public async Task ResponseReadProposalRevalidatesAfterRemovingReviewBeforeSharedExecution()
    {
        var fixture = await CreateReadableResponseAsync();
        fixture.ViewModel.BindResponseReadPlanner(new ResponseReadPlannerFake(), TimeProvider.System);
        await fixture.RunAsync("please read this answer aloud");
        await fixture.ViewModel.PlanCurrentResponseReadCommand.ExecuteAsync();
        var confirm = fixture.ViewModel.CaptureResponseReadProposalDecision(confirm: true);
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(fixture.ViewModel.HasResponseReadProposal), StringComparison.Ordinal)
                && !fixture.ViewModel.HasResponseReadProposal)
            { fixture.Session.IsUnlocked = false; }
        };
        await confirm();
        fixture.ViewModel.LastResponseReadOutcome.Should().Be(SpeechReadOutcome.Denied);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseReadProposalAvailabilityRequiresCurrentSourceAndJoinedPlanning(bool expiry)
    {
        var fixture = await CreateReadableResponseAsync();
        var time = new ResponseReadTime();
        fixture.ViewModel.BindResponseReadPlanner(new ResponseReadPlannerFake(), time);
        await fixture.RunAsync("please read this answer aloud");
        if (expiry) { time.Now = time.Now.AddMinutes(2); }
        else { time.Now = time.Now.AddMinutes(-1); }
        fixture.ViewModel.CanPlanCurrentResponseRead.Should().BeFalse();
        await fixture.RunAsync("interpret current response read");
        fixture.ViewModel.CanConfirmCurrentResponseRead.Should().BeFalse();
        fixture.ViewModel.ResponseReadProposalReview.Should().BeEmpty();
    }

    [Theory]
    [InlineData("stop speaking")]
    [InlineData("cancel task")]
    [InlineData("exit the application")]
    public async Task ResponseReadProposalHostStopsJoinTheExactPlanner(string command)
    {
        var fixture = await CreateReadableResponseAsync();
        var planner = new ResponseReadPlannerFake { RunGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        fixture.ViewModel.BindResponseReadPlanner(planner, TimeProvider.System);
        await fixture.RunAsync("please read this answer aloud");
        var planning = fixture.ViewModel.PlanCurrentResponseReadCommand.ExecuteAsync();
        await planner.RunEntered.Task;
        var stop = fixture.RunAsync(command);
        await planner.Cancelled.Task;
        try { stop.IsCompleted.Should().BeFalse(); }
        finally { planner.RunGate.TrySetResult(); }
        await Task.WhenAll(planning, stop);
        fixture.ViewModel.HasResponseReadProposal.Should().BeFalse();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        planner.Disposals.Should().Be(1);
    }

    [Theory]
    [InlineData("voice")]
    [InlineData("host")]
    [InlineData("initiator")]
    [InlineData("invocation")]
    public async Task ResponseReadProposalRejectsUntrustedLegacyTranscriptProvenance(string kind)
    {
        var fixture = await CreateReadableResponseAsync();
        fixture.ViewModel.BindResponseReadPlanner(new ResponseReadPlannerFake(), TimeProvider.System);
        var origin = kind switch
        {
            "voice" => RequestOrigin.ActivatedVoice,
            "host" => RequestOrigin.HostSystem,
            _ => RequestOrigin.LocalUi,
        };
        var request = HostRequest.Create(origin);
        if (string.Equals(kind, "invocation", StringComparison.Ordinal))
        { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var initiator = string.Equals(kind, "initiator", StringComparison.Ordinal) ? SecurityAuditInitiator.System
            : origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand;
        // Invalid provenance cannot be minted through UI commands; exercise the legacy callback seam without mutating private authority.
        var method = typeof(Kora.Application.ViewModels.MainViewModel).GetMethod("HandleTranscriptAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var arguments = method.GetParameters().Select(parameter => parameter.HasDefaultValue ? parameter.DefaultValue : null).ToArray();
        arguments[0] = "please read this answer aloud";
        arguments[1] = 1f;
        arguments[2] = initiator;
        await (Task)method.Invoke(fixture.ViewModel, arguments)!;
        fixture.ViewModel.HasResponseReadProposal.Should().BeFalse();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ResponseReadProposalRefusesAnUnavailableAssistantName()
    {
        var fixture = new Fixture();
        fixture.NamePreferences.Name = "Supported Commands";
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindResponseReadPlanner(new ResponseReadPlannerFake(), TimeProvider.System);
        await fixture.RunAsync("please read this answer aloud");
        fixture.ViewModel.HasResponseReadProposal.Should().BeFalse();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseReadRetainedSourceCancellationReportsUnconfirmedTerminalEvidence(bool auditFailure)
    {
        var fixture = await CreateReadableResponseAsync();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        if (auditFailure)
        {
            fixture.Audit.BeforeWrite = audit =>
            {
                if (string.Equals(audit.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
                    && audit.Outcome == SecurityAuditOutcome.Cancelled)
                { throw new IOException("private-terminal-content"); }
            };
        }
        await fixture.ViewModel.ReadAnswerOnceAsync(SpeechAnswer(request), static () => true,
            _ => throw new OperationCanceledException(), TestContext.Current.CancellationToken);
        fixture.ViewModel.LastResponseReadOutcome.Should().Be(auditFailure ? SpeechReadOutcome.Unknown : SpeechReadOutcome.Cancelled);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.ResponseBody.Should().Contain("Power execution");
    }

    [Fact]
    public async Task ResponseReadAlreadyCancelledInvocationNeverSynthesizes()
    {
        var fixture = await CreateReadableResponseAsync();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var outcome = await ((ISpeechReadOncePlayback)fixture.ViewModel).ReadOnceAsync(
            new(Guid.NewGuid(), 1, 1), SecurityAuditInitiator.LocalUser, cancellation.Token);
        outcome.Should().Be(SpeechReadOutcome.Cancelled);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Fact]
    public async Task ResponseReadTerminalPublicationRevalidatesSourceAfterAwaitedAudit()
    {
        var fixture = await CreateReadableResponseAsync();
        fixture.ViewModel.BindResponseReadPlanner(new ResponseReadPlannerFake(), TimeProvider.System);
        await fixture.RunAsync("please read this answer aloud");
        await fixture.ViewModel.PlanCurrentResponseReadCommand.ExecuteAsync();
        fixture.Audit.BeforeWrite = audit =>
        {
            if (string.Equals(audit.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
                && audit.Outcome == SecurityAuditOutcome.Succeeded)
            { fixture.ViewModel.RetireCurrentResponseSpeech(); }
        };
        await fixture.ViewModel.CaptureResponseReadProposalDecision(confirm: true)();
        fixture.ViewModel.LastResponseReadOutcome.Should().Be(SpeechReadOutcome.Unknown);
    }

    [Fact]
    public async Task ResponseReadProposalRemainsUnavailableDuringFinalPlannerJoinNotifications()
    {
        var fixture = await CreateReadableResponseAsync();
        fixture.ViewModel.BindResponseReadPlanner(new ResponseReadPlannerFake(), TimeProvider.System);
        await fixture.RunAsync("please read this answer aloud");
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(fixture.ViewModel.ReadCurrentResponseStatus), StringComparison.Ordinal)
                && fixture.ViewModel.ReadCurrentResponseStatus.StartsWith("Local interpretation joined", StringComparison.Ordinal))
            { fixture.ViewModel.CanConfirmCurrentResponseRead.Should().BeFalse(); }
            if (string.Equals(args.PropertyName, nameof(fixture.ViewModel.IsLocalTaskCancellable), StringComparison.Ordinal)
                && !fixture.ViewModel.IsLocalTaskCancellable)
            { fixture.ViewModel.CanConfirmCurrentResponseRead.Should().BeFalse(); }
        };
        await fixture.ViewModel.PlanCurrentResponseReadCommand.ExecuteAsync();
        fixture.ViewModel.CanConfirmCurrentResponseRead.Should().BeTrue();
    }

    [Fact]
    public async Task ResponseReadInterruptionFailureRetainsCancellationIgnoringPhysicalOwnership()
    {
        var (fixture, _, read) = await StartInterruptibleAnswerAsync(ignoreCancellation: true);
        fixture.TextToSpeech.IgnoreOutputInvalidation = true;
        await fixture.ViewModel.BeginPushToTalkAsync();
        await fixture.ViewModel.EndPushToTalkAsync();
        fixture.TextToSpeech.StopException = new InvalidOperationException("private-stop-content");
        try
        {
            await fixture.Voice.RaiseTranscriptAsync("Kora, stop speaking", 1);
            fixture.TextToSpeech.IsSpeaking.Should().BeTrue();
            read.IsCompleted.Should().BeFalse();
            fixture.ViewModel.IsSpeaking.Should().BeTrue();
            fixture.Audit.Events.Should().Contain(item => string.Equals(item.ActionId, "speech.interrupt", StringComparison.Ordinal)
                && item.Outcome == SecurityAuditOutcome.Failed);
            fixture.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
                && item.Outcome == SecurityAuditOutcome.Succeeded);
        }
        finally
        {
            fixture.TextToSpeech.StopException = null;
            fixture.TextToSpeech.SpeakGate!.TrySetResult();
            await read;
        }
    }

    [Fact]
    public async Task ResponseReadProposalWithoutAWindowObserverNeverSpeaksOrInfersAutomatically()
    {
        var fixture = new Fixture(subscribeToWindowActions: false);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.Hybrid;
        await fixture.RunAsync("what power action is pending");
        fixture.TextToSpeech.ClearSpokenResponse();
        var body = fixture.ViewModel.ResponseBody;
        var planner = new ResponseReadPlannerFake();
        fixture.ViewModel.BindResponseReadPlanner(planner, TimeProvider.System);
        await fixture.RunAsync("please read this answer aloud");
        fixture.ViewModel.HasResponseReadProposal.Should().BeTrue();
        fixture.ViewModel.ResponseBody.Should().Be(body);
        planner.Admissions.Should().Be(0);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        await fixture.ViewModel.CancelCurrentResponseReadCommand.ExecuteAsync();
    }

    private sealed class ResponseReadTime : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ResponseReadPlannerFake : IModelContextRunner
    {
        internal int Admissions { get; private set; }
        internal int Disposals { get; private set; }
        internal string? Answer { get; set; }
        internal string? Failure { get; init; }
        internal Action? BeforeRun { get; set; }
        internal ModelContextEnvelope? Context { get; private set; }
        internal TaskCompletionSource? RunGate { get; init; }
        internal TaskCompletionSource? DisposeGate { get; init; }
        internal TaskCompletionSource RunEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource DisposeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ModelContextAdmission> AdmitAsync(LocalContextSessionTarget target, Func<bool> sourceCurrent,
            CancellationToken token, Func<CancellationToken, Task>? validateSource = null)
        {
            Admissions++;
            sourceCurrent().Should().BeTrue();
            target.SourceRequest.Should().BeSameAs(HostActivity.RequireCurrent().Request);
            target.Request.Should().NotBeSameAs(target.SourceRequest);
            target.CreatesSession.Should().BeTrue();
            if (string.Equals(Failure, "operation-cancel", StringComparison.Ordinal)) { throw new OperationCanceledException(); }
            if (string.Equals(Failure, "admission", StringComparison.Ordinal))
            {
                return Task.FromResult(new ModelContextAdmission(
                    new LocalContextTurnReceipt(target.Request, ModelTurnOutcome.Denied, LocalContextTurnReason.OriginalInputRequired)));
            }
            return Task.FromResult(new ModelContextAdmission(new Lease(this, target.Request)));
        }

        private sealed class Lease(ResponseReadPlannerFake owner, HostRequest request) : IAdmittedModelContextTurn
        {
            public HostRequest Request => request;
            public HostRevision SessionGeneration => new(1);
            public Task<LocalContextTurnReceipt> Completion { get; private set; } =
                Task.FromResult(new LocalContextTurnReceipt(request, ModelTurnOutcome.Cancelled, LocalContextTurnReason.Cancelled));

            public Task<LocalContextTurnReceipt> RunAsync(ModelContextEnvelope context, CancellationToken token) =>
                Completion = Run(context, token);

            private async Task<LocalContextTurnReceipt> Run(ModelContextEnvelope context, CancellationToken token)
            {
                owner.Context = context;
                if (string.Equals(owner.Failure, "throw", StringComparison.Ordinal)) { throw new IOException("private-model-content"); }
                using var registration = token.Register(() => owner.Cancelled.TrySetResult());
                owner.BeforeRun?.Invoke();
                owner.RunEntered.TrySetResult();
                if (owner.RunGate is { } gate) { await gate.Task; }
                using var evidence = JsonDocument.Parse(context.Evidence[0].Content);
                var answer = owner.Answer ?? JsonSerializer.Serialize(new { target = evidence.RootElement.GetProperty("target").GetString() });
                var outcome = owner.Failure switch
                {
                    "unavailable" => ModelTurnOutcome.Unavailable,
                    "unknown" => ModelTurnOutcome.Unknown,
                    "cancelled" => ModelTurnOutcome.Cancelled,
                    _ => ModelTurnOutcome.Succeeded,
                };
                return new(context.Request, outcome, LocalContextTurnReason.Completed,
                    HostTaskState.Succeeded, new(outcome, ModelTurnReason.None,
                        new(new(Guid.NewGuid()), context.Request, ModelProviderSelection.OllamaCandidate), new(answer, null)));
            }

            public async ValueTask DisposeAsync()
            {
                owner.DisposeEntered.TrySetResult();
                if (owner.DisposeGate is { } gate) { await gate.Task; }
                owner.Disposals++;
                if (string.Equals(owner.Failure, "dispose", StringComparison.Ordinal)) { throw new InvalidOperationException("private-release-content"); }
            }
        }
    }
}
