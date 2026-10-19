using AwesomeAssertions;
using Kora.Application.Interaction;
using Kora.Application.ViewModels;
using Kora.Application.UnitTests.Interaction;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Platform;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Kora.Application.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("stop listening", false)]
    [InlineData("disable listening", false)]
    [InlineData("Nova, stop listening", false)]
    [InlineData("Nova, disable listening", true)]
    [InlineData("stop listening", true)]
    public async Task ListeningStopExactTypedAndActivatedRoutesAwaitTheSameRelease(string command, bool voice)
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova", SecurityAuditInitiator.LocalUser);
        await f.ViewModel.ToggleListeningCommand.ExecuteAsync();
        await f.ViewModel.BeginPushToTalkAsync();
        f.Voice.StartedPhrases.Should().Contain("stop listening").And.Contain("disable listening")
            .And.Contain("Nova stop listening").And.Contain("Nova disable listening")
            .And.NotContain("Kora stop listening");
        await f.ViewModel.EndPushToTalkAsync();
        f.TextToSpeech.ClearSpokenResponse();
        f.Voice.StopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        HostRequest? releaseRequest = null;
        f.Voice.BeforeStopFailure = () => releaseRequest = HostActivity.RequireCurrent().Request;
        var stop = voice ? f.Voice.RaiseTranscriptAsync(command, 1) : f.RunAsync(command);

        stop.IsCompleted.Should().BeFalse();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.ViewModel.CanPushToTalk.Should().BeFalse();
        f.ViewModel.ResponseTitle.Should().Be("Listening stop accepted.");
        f.ViewModel.ResponseBody.Should().Contain("release is pending");
        releaseRequest!.Origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        await f.ViewModel.ToggleListeningCommand.ExecuteAsync();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.Voice.StopGate.SetResult();
        await stop;

        f.ViewModel.ResponseTitle.Should().Be("Listening disabled.");
        f.ViewModel.ResponseBody.Should().Contain("release is confirmed");
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        f.ViewModel.HasVoiceConsent.Should().BeTrue();
        f.Voice.StartCalls.Should().Be(1);
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
        f.Session.LockCalls.Should().Be(0);
        AssertAuditPair(f, SecurityAuditCategory.ConfigurationWrite, "configuration.listening-disabled",
            voice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand, SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData("question", false)]
    [InlineData("question", true)]
    [InlineData("action", false)]
    [InlineData("grant", false)]
    public async Task ListeningStopReservesExactInputBeforePendingDecisionsWithoutChangingThem(string pending, bool voice)
    {
        var f = CreateVoicePrivacyFixture();
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "ready");
        if (string.Equals(pending, "question", StringComparison.Ordinal)) { f.Reasoner.Question = new("Exact choice?", ["stop listening", "continue"]); }
        if (string.Equals(pending, "action", StringComparison.Ordinal)) { f.Reasoner.Action = BuiltInAction.LockMachine; }
        if (string.Equals(pending, "grant", StringComparison.Ordinal)) { f.Reasoner.GrantChange = new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Session); }
        await f.ViewModel.InitializeAsync();
        if (voice) { await f.ViewModel.BeginPushToTalkAsync(); await f.ViewModel.EndPushToTalkAsync(); }
        await f.RunAsync("do this requested work");
        await f.ViewModel.ActiveReasoningTask!;
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        var choices = f.ViewModel.ModelQuestionChoices;
        f.ViewModel.IsResponseInteractionPending.Should().BeTrue();

        if (voice) { await f.Voice.RaiseTranscriptAsync("Kora, stop listening", 1); }
        else { await f.RunAsync("disable listening"); }

        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.ViewModel.IsResponseInteractionPending.Should().BeTrue();
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.ModelQuestionChoices.Should().BeSameAs(choices);
        f.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        f.ViewModel.Transcript.Should().Contain("Listening disabled.");
        f.Reasoner.Requests.Should().HaveCount(1);
        f.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("stop listening")]
    [InlineData("Kora, disable listening")]
    public async Task ListeningStopInNativeQuestionReplyCannotBecomeEvenAnExactChoiceAnswer(string command)
    {
        var f = await ReplyHostAsync();
        using var host = new InteractionFixture();
        var question = await host.QuestionAsync(new("Choose", QuestionKind.SingleChoice,
            [new("stop", command), new("other", "Other choice")]));
        await using var replies = f.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<HostQuestionVoiceReplyService>.Instance)!;
        await replies.OpenAsync(question, static () => true, _ => { }, false, TestContext.Current.CancellationToken);
        f.Voice.StartedPhrases.Should().Contain("stop listening").And.Contain("Kora disable listening");
        f.ReplyVoice!.Speech();

        f.Voice.RaiseTranscript(command, 1);
        await replies.Quiescence;

        host.Store.Snapshot.Questions.Single().Draft.Should().BeNull();
        host.Store.Snapshot.Questions.Single().Status.Should().Be(QuestionStatus.Pending);
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        replies.IsActive.Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
        f.Audit.Events.Where(audit => string.Equals(audit.ActionId, "configuration.listening-disabled", StringComparison.Ordinal))
            .Should().OnlyContain(audit => audit.Initiator == SecurityAuditInitiator.VoiceCommand);
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task ListeningStopRemainsAvailableDuringUnrelatedReasoningWithoutCancellation()
    {
        var f = CreateVoicePrivacyFixture();
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "ready");
        f.Reasoner.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("do unrelated work");
        var reasoning = f.ViewModel.ActiveReasoningTask!;
        f.ViewModel.CommandText = "stop listening";
        reasoning.IsCompleted.Should().BeFalse();
        f.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue();

        await f.ViewModel.RunTypedCommand.ExecuteAsync();

        reasoning.IsCompleted.Should().BeFalse();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.Reasoner.Gate.SetResult("Work completed normally");
        await reasoning;
        f.ViewModel.ResponseBody.Should().Contain("Work completed normally");
    }

    [Fact]
    public async Task ListeningStopDoesNotSupersedeAnUnrelatedTypedApprovalPreparation()
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        f.Audit.BeforeWrite = audit =>
        {
            if (audit.Category != SecurityAuditCategory.SecurityApproval
                || audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            entered.TrySetResult();
            release.Wait(token);
        };
        var preparation = f.ViewModel.PrepareGrantChangeAsync(
            new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Session),
            SecurityAuditInitiator.TypedCommand);
        try
        {
            await entered.Task.WaitAsync(token);
            await f.RunAsync("stop listening");
            f.ViewModel.IsVoiceEnabled.Should().BeFalse();
            preparation.IsCompleted.Should().BeFalse();
        }
        finally { release.Set(); }
        await preparation;

        f.ViewModel.IsGrantChangePending.Should().BeTrue();
        f.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        f.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
        f.Audit.Events.Should().NotContain(audit => audit.Category == SecurityAuditCategory.SecurityApproval
            && audit.Outcome == SecurityAuditOutcome.Cancelled);
        f.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task ListeningStopCannotPublishAStaleDisabledOutcomeAfterRecoveryDuringItsTerminalReceipt()
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        f.Audit.BeforeWrite = audit =>
        {
            if (!string.Equals(audit.ActionId, "configuration.listening-disabled", StringComparison.Ordinal)
                || audit.Outcome != SecurityAuditOutcome.Succeeded) { return; }
            entered.TrySetResult();
            release.Wait(token);
        };
        var stopping = f.ViewModel.DisableListeningFromTrayAsync();
        try
        {
            await entered.Task.WaitAsync(token);
            f.Voice.IsCaptureQuiescent.Should().BeTrue();
            await f.ViewModel.ToggleListeningCommand.ExecuteAsync();
            f.ViewModel.IsVoiceEnabled.Should().BeFalse();
            f.ViewModel.CanPushToTalk.Should().BeFalse();
        }
        finally { release.Set(); }
        await stopping;
        f.ViewModel.ResponseTitle.Should().Be("Listening disabled.");
        await f.ViewModel.ToggleListeningCommand.ExecuteAsync();
        f.ViewModel.IsVoiceEnabled.Should().BeTrue();
        f.Voice.StartCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Unknown)]
    [InlineData(CallState.Suspected)]
    public async Task ListeningStopIsAllowedUnderProtectiveCallStateWithoutWeakeningRecovery(CallState state)
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.SetAllowVoiceActivationDuringCallsAsync(false);
        f.CallState.SetState(state);
        await f.ViewModel.CallClosureTask;
        using var voice = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request);
        var command = f.Catalog.GetCommands().Single(item => item.Action == BuiltInAction.DisableListening);

        await f.ViewModel.ExecuteAsync(command, SecurityAuditInitiator.VoiceCommand);
        await f.ViewModel.ToggleListeningCommand.ExecuteAsync();

        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.ViewModel.AllowVoiceActivationDuringCalls.Should().BeFalse();
        f.ViewModel.AutomaticCallState.Should().Be(state);
        f.Voice.StartCalls.Should().Be(0);
        AssertAuditPair(f, SecurityAuditCategory.ConfigurationWrite, "configuration.listening-disabled",
            SecurityAuditInitiator.VoiceCommand, SecurityAuditOutcome.Succeeded);
        HostActivity.RequireCurrent().Should().BeSameAs(voice);
        voice.Complete(HostOperationOutcome.Completed);
    }

    [Fact]
    public async Task ListeningStopAndStaleNativeDisableAreIdempotentAndCannotToggleBackOn()
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("stop listening");
        await f.ViewModel.DisableListeningFromTrayAsync();
        await f.RunAsync("disable listening");

        f.ViewModel.ResponseTitle.Should().Be("Listening already disabled.");
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.ViewModel.CanPushToTalk.Should().BeFalse();
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        f.VoiceConsent.Consent.Should().BeTrue();
        f.Voice.StartCalls.Should().Be(0);
        f.Voice.StopCalls.Should().Be(3);
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("io")]
    [InlineData("cancelled")]
    [InlineData("unknown")]
    public async Task ListeningStopNeverClaimsReleaseWhenNativeReleaseIsDeniedOrUncertain(string failure)
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        f.Voice.StopException = failure switch
        {
            "denied" => new UnauthorizedAccessException("denied"),
            "io" => new IOException("release failed"),
            "cancelled" => new OperationCanceledException("native stop cancelled"),
            _ => null,
        };
        f.Voice.PreventQuiescence = true;

        await f.RunAsync("stop listening");
        f.ViewModel.ResponseTitle.Should().Be("Listening disabled; microphone release unconfirmed.");
        f.ViewModel.ResponseBody.Should().Contain("could not be confirmed").And.NotContain("release is confirmed");
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.ToggleListeningCommand.ExecuteAsync();

        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.ViewModel.CanPushToTalk.Should().BeFalse();
        f.Voice.StartCalls.Should().Be(0);
        f.ViewModel.HasVoiceConsent.Should().BeTrue();
        AssertAuditPair(f, SecurityAuditCategory.ConfigurationWrite, "configuration.listening-disabled",
            SecurityAuditInitiator.TypedCommand, SecurityAuditOutcome.Failed, "capture-release-unconfirmed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListeningStopFencesQueuedTranscriptBeforeOrDuringUiDispatch(bool afterQueue)
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        var generation = f.Voice.Generation;
        Task? stopped = null;
        if (afterQueue) { f.Dispatcher.BeforeInvoke = () => stopped = f.ViewModel.DisableListeningFromTrayAsync(); }
        else { await f.RunAsync("stop listening"); }

        f.Voice.RaiseTranscriptForGeneration(generation);
        await f.Dispatcher.LastInvocation;
        if (stopped is not null) { await stopped; }

        f.Session.LockCalls.Should().Be(0);
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ListeningStopRetiresLateNativeOpenAndEndCallbacksAndRequiresFreshRecovery()
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        f.Voice.StartGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Voice.IgnoreStartCancellation = true;
        var opening = f.ViewModel.BeginPushToTalkAsync();
        f.Voice.PreventQuiescence = true;

        await f.RunAsync("stop listening");
        f.ViewModel.ResponseTitle.Should().Contain("release unconfirmed");
        var retired = f.Voice.Generation;
        f.Voice.StartGate.SetResult();
        await opening;
        await f.ViewModel.EndPushToTalkAsync();
        f.Voice.RaiseTranscriptForGeneration(retired);
        f.Voice.PublishCompletion(new(retired, VoiceRecognitionCompletionReason.MaximumDuration));
        await f.ViewModel.BeginPushToTalkAsync();

        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.Voice.IsListening.Should().BeFalse();
        f.Voice.StartCalls.Should().Be(1);
        f.Session.LockCalls.Should().Be(0);
        f.Voice.PreventQuiescence = false;
        await f.ViewModel.ToggleListeningCommand.ExecuteAsync();
        f.ViewModel.IsVoiceEnabled.Should().BeTrue();
        f.Voice.StartCalls.Should().Be(1);
    }

    [Fact]
    public async Task ListeningStopRetiresQueuedUnlockRecoveryAndTopologyCannotReleaseItsHold()
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        f.Voice.StopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PublishVoiceSession(f, WindowsSessionState.Locked);
        PublishVoiceSession(f, WindowsSessionState.Unlocked);
        var recovery = f.ViewModel.VoiceUnlockRecoveryTask;
        var stopped = f.ViewModel.DisableListeningFromTrayAsync();
        f.Voice.StopGate.SetResult();
        await stopped;
        await recovery;
        await PublishTopologyAsync(f, 1, WindowsPrivacyChangeReason.DefaultMicrophone);
        await f.ViewModel.PrivacyClosureTask;
        f.ViewModel.RequestVoiceRecovery();
        await f.ViewModel.RefreshMicrophonesAsync();
        await f.ViewModel.BeginPushToTalkAsync();

        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.Voice.StartCalls.Should().Be(0);
        f.ViewModel.RequestVoiceRecovery();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        await f.ViewModel.EnableListeningFromTrayAsync(f.ViewModel.MicrophoneTopologyRevision);
        f.ViewModel.IsVoiceEnabled.Should().BeTrue();
        f.Voice.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task ListeningStopReleaseContinuesAcrossDisposalWithoutPublishingLateSuccess()
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        f.Voice.StopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var stop = f.ViewModel.DisableListeningFromTrayAsync();
        f.ViewModel.Dispose();
        var title = f.ViewModel.ResponseTitle;
        f.Voice.StopGate.SetResult();
        await stop;
        f.ViewModel.ResponseTitle.Should().Be(title);
        await f.ViewModel.DisableListeningFromTrayAsync();
        await f.ViewModel.BeginPushToTalkAsync();

        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        f.Voice.StartCalls.Should().Be(0);
        f.ViewModel.ResponseTitle.Should().NotBe("Listening disabled.");
    }

    [Fact]
    public async Task ListeningStopHasNoQualifiedModelLaneAndDoesNotMutateTheReadOnlyRegistry()
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        var command = f.Catalog.GetCommands().Single(item => item.Action == BuiltInAction.DisableListening);
        await f.ViewModel.ExecuteAsync(command, SecurityAuditInitiator.ModelSuggestion);

        f.ViewModel.IsVoiceEnabled.Should().BeTrue();
        f.ViewModel.ResponseTitle.Should().Be("Model listening control is unavailable.");
        command.SupportsModelSuggestion.Should().BeFalse();
        f.ViewModel.GrantActions.Should().NotContain(item => item.Action == BuiltInAction.DisableListening);
        f.Voice.StopCalls.Should().Be(0);
        Kora.Core.Tools.ReadOnlyCapabilityCatalog.Descriptors.Should().HaveCount(6)
            .And.OnlyContain(descriptor => descriptor.Effect == Kora.Core.Tools.CapabilityEffect.ReadOnlyObservation);
    }

    [Theory]
    [InlineData(SecurityAuditOutcome.Requested)]
    [InlineData(SecurityAuditOutcome.Succeeded)]
    public async Task ListeningStopReleasesCaptureEvenWhenRequiredAuditFails(SecurityAuditOutcome failure)
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.BeginPushToTalkAsync();
        f.Audit.BeforeWrite = audit =>
        {
            if (string.Equals(audit.ActionId, "configuration.listening-disabled", StringComparison.Ordinal)
                && audit.Outcome == failure) { throw new IOException("synthetic audit failure"); }
        };

        await f.RunAsync("stop listening");

        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        f.ViewModel.ResponseBody.Should().Contain("Required audit evidence is unavailable");
        f.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        f.VoiceConsent.Consent.Should().BeTrue();
        f.Voice.StartCalls.Should().Be(1);
    }

    [Fact]
    public async Task ListeningStopHoldCannotBeClearedByVoiceRelabeledThroughANativeCommand()
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("disable listening");
        using (var voice = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request))
        {
            await f.ViewModel.ToggleListeningCommand.ExecuteAsync();
            await f.ViewModel.SetVoiceConsentAsync(true);
            f.ViewModel.IsVoiceEnabled.Should().BeFalse();
            f.ViewModel.CanPushToTalk.Should().BeFalse();
            HostActivity.RequireCurrent().Should().BeSameAs(voice);
            voice.Complete(HostOperationOutcome.Completed);
        }
        await f.ViewModel.ToggleListeningCommand.ExecuteAsync();
        f.ViewModel.IsVoiceEnabled.Should().BeTrue();
        f.Voice.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task ListeningStopActionRefusesPendingOrUnconfirmedRecoveryAndCorrelatesEnabledDiagnostics()
    {
        var f = new Fixture();
        var logger = new ListeningStopLogger();
        var action = new ListeningStopAction(f.Voice, logger);
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Policy);
        f.Voice.StopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var stopping = action.DisableAsync(f.Voice.InvalidateCapture);

        action.IsHeld.Should().BeTrue();
        action.TryRecover().Should().BeFalse();
        f.Voice.PreventQuiescence = true;
        f.Voice.StopGate.SetResult();
        (await stopping).Should().Be(ListeningStopOutcome.ReleaseUnconfirmed);
        action.TryRecover().Should().BeFalse();
        f.Voice.PreventQuiescence = false;
        action.TryRecover().Should().BeTrue();
        action.IsHeld.Should().BeFalse();
        f.Voice.StopException = new UnauthorizedAccessException("Synthetic native release refusal");
        (await action.DisableAsync(f.Voice.InvalidateCapture)).Should().Be(ListeningStopOutcome.ReleaseUnconfirmed);

        logger.Events.Select(entry => entry.Name).Should().Equal("Stopped", "ReleaseFailed");
        logger.Events.Should().OnlyContain(entry => entry.Request == activity.Request
            && entry.Trace == activity.Activity!.TraceId);
        action.IsHeld.Should().BeTrue();
        activity.Complete(HostOperationOutcome.Failed);
    }

    private sealed class ListeningStopLogger : ILogger<MainViewModel>
    {
        public List<(string? Name, HostRequest Request, System.Diagnostics.ActivityTraceId Trace)> Events { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var activity = HostActivity.RequireCurrent();
            Events.Add((eventId.Name, activity.Request, activity.Activity!.TraceId));
        }
    }
}
