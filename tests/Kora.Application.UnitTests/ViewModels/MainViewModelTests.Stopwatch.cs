using AwesomeAssertions;

using Kora.Application.Timekeeping;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Platform;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("StopwatchStatus")]
    [InlineData("What Time")]
    public async Task StopwatchNativeTypedAndCurrentNameVoiceShareOneRunWithoutAutomaticProjectionOrSideEffects(string assistantName)
    {
        var time = new StopwatchTime();
        var f = await CreateStopwatchFixture(time);
        f.ViewModel.BindStopwatchNativeLifetime(true);
        var spoken = f.TextToSpeech.SpeakCalls;
        f.ViewModel.IsStopwatchPanelVisible.Should().BeFalse();
        await f.RunAsync("stopwatch stop");
        f.ViewModel.ResponseBody.Should().Contain("no running");
        await f.RunAsync("stopwatch help");
        f.ViewModel.ResponseBody.Should().Contain(StopwatchCommand.Syntax);
        await f.RunAsync("stopwatch start");
        f.ViewModel.IsStopwatchPanelVisible.Should().BeTrue();
        var initial = f.ViewModel.StopwatchDisplay;
        time.Timestamp = 12345678;
        f.ViewModel.StopwatchDisplay.Should().Be(initial);
        await f.ViewModel.StopwatchStatusCommand.ExecuteAsync();
        f.ViewModel.StopwatchDisplay.Should().Contain("00:00:01.2345678").And.Contain("Not a live clock");
        await f.RunAsync("stopwatch start");
        f.ViewModel.ResponseBody.Should().Contain("Already running");
        time.Timestamp = 20000000;
        await f.ViewModel.StopwatchStopCommand.ExecuteAsync();
        f.ViewModel.StopwatchDisplay.Should().StartWith("Stopped").And.Contain("00:00:02");
        time.Timestamp = 40000000;
        await f.RunAsync("stopwatch stop");
        f.ViewModel.ResponseBody.Should().Contain("Already stopped");
        await f.RunAsync("stopwatch start");
        f.ViewModel.ResponseBody.Should().Contain("Start refused");
        await f.RunAsync("stopwatch status");
        f.ViewModel.StopwatchDisplay.Should().Contain("00:00:02");
        await f.ViewModel.StopwatchResetCommand.ExecuteAsync();
        f.ViewModel.StopwatchDisplay.Should().StartWith("Idle").And.Contain("00:00:00");
        await f.RunAsync("set assistant name to " + assistantName);
        await f.RaiseActivatedTranscriptAsync(assistantName + ", stopwatch start", 1);
        f.ViewModel.StopwatchDisplay.Should().StartWith("Running");
        f.Voice.StartedPhrases.Should().Contain("stopwatch start").And.Contain(assistantName + " stopwatch start");
        await f.RaiseActivatedTranscriptAsync("stopwatch stop", 1);
        f.ViewModel.ResponseBody.Should().Contain("denied");
        f.ViewModel.StopwatchDisplay.Should().StartWith("Idle");
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        f.TextToSpeech.SpeakCalls.Should().Be(spoken);
        f.ViewModel.CanReadCurrentResponse.Should().BeFalse();
        f.Audit.Events.Where(a => a.ActionId.StartsWith("stopwatch.", StringComparison.Ordinal))
            .Should().Contain(a => a.Initiator == SecurityAuditInitiator.VoiceCommand)
            .And.Contain(a => a.Initiator == SecurityAuditInitiator.LocalUser)
            .And.Contain(a => a.Initiator == SecurityAuditInitiator.TypedCommand);
    }

    [Theory]
    [InlineData("question")]
    [InlineData("approval")]
    [InlineData("grant")]
    [InlineData("native-review")]
    [InlineData("grant-editor")]
    public async Task StopwatchReservedCommandsNeverAnswerOrReplaceAnyPendingDecision(string pending)
    {
        var f = await CreateStopwatchFixture(new());
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "Ready");
        await f.ViewModel.RefreshCommand.ExecuteAsync();
        switch (pending)
        {
            case "question":
                f.Reasoner.Question = new("Which target?", ["One", "Two"]);
                await f.RunAsync("choose");
                await f.ViewModel.ActiveReasoningTask!;
                break;
            case "approval":
                f.Reasoner.Action = BuiltInAction.LockMachine;
                await f.RunAsync("please lock");
                await f.ViewModel.ActiveReasoningTask!;
                break;
            case "grant":
                await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
                break;
            case "native-review": f.ViewModel.BindResponseReadDecisionGate(static () => false); break;
            case "grant-editor": await f.RunAsync("manage grants"); break;
        }
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        var questions = f.ViewModel.IsModelQuestionPending;
        var approvals = f.ViewModel.IsModelActionApprovalPending;
        var grants = f.ViewModel.IsGrantChangePending;
        f.Reasoner.Requests.Clear();
        foreach (var input in new[] { "stopwatch start", "stopwatch status and approve", "stopwatch stop", "stopwatch reset", "stopwatch help" })
        { await f.RunAsync(input); }
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.IsModelQuestionPending.Should().Be(questions);
        f.ViewModel.IsModelActionApprovalPending.Should().Be(approvals);
        f.ViewModel.IsGrantChangePending.Should().Be(grants);
        f.Reasoner.Requests.Should().BeEmpty();
        f.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("ownership")]
    [InlineData("owner-revision")]
    [InlineData("dispose")]
    [InlineData("native")]
    [InlineData("handoff")]
    public async Task StopwatchRevocationDiscardsRunningAndFrozenResultsWithoutLaterRecovery(string change)
    {
        var f = await CreateStopwatchFixture(new());
        f.ViewModel.BindStopwatchNativeLifetime(true);
        long owner = 1;
        var eligible = true;
        f.ViewModel.BindCallOwnershipGate(() => eligible, () => owner);
        await f.RunAsync("stopwatch start");
        switch (change)
        {
            case "privacy":
                PublishVoiceSession(f, WindowsSessionState.Locked);
                break;
            case "call": f.CallState.SetState(CallState.Active); break;
            case "ownership": eligible = false; break;
            case "owner-revision": owner++; break;
            case "dispose": f.ViewModel.Dispose(); break;
            case "native": f.ViewModel.BindStopwatchNativeLifetime(false); break;
            case "handoff": await f.ViewModel.TryPrepareHandoffAsync(); break;
        }
        f.ViewModel.StopwatchDisplay.Should().StartWith("Idle");
        eligible = true;
        if (string.Equals(change, "owner-revision", StringComparison.Ordinal)) { owner++; }
        f.ViewModel.StopwatchDisplay.Should().NotStartWith("Running").And.NotStartWith("Stopped");
    }

    [Theory]
    [InlineData("native")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("input")]
    [InlineData("text")]
    [InlineData("decision")]
    [InlineData("terminal")]
    [InlineData("hidden")]
    public async Task StopwatchQueuedNativeCannotSurviveAdmissionChangeAtAuditBoundary(string change)
    {
        var f = await CreateStopwatchFixture(new());
        f.ViewModel.BindStopwatchNativeLifetime(true);
        await f.RunAsync("stopwatch start");
        f.Audit.BeforeWrite = audit =>
        {
            if (!audit.ActionId.StartsWith("stopwatch.", StringComparison.Ordinal)) { return; }
            if (string.Equals(change, "terminal", StringComparison.Ordinal) != (audit.Outcome != SecurityAuditOutcome.Requested)) { return; }
            f.Audit.BeforeWrite = null;
            switch (change)
            {
                case "native": f.ViewModel.BindStopwatchNativeLifetime(false); break;
                case "owner": f.ViewModel.BindCallOwnershipGate(static () => false); break;
                case "call": f.CallState.SetState(CallState.Active); break;
                case "input": f.ViewModel.StopwatchResetCommand.Execute(null); break;
                case "text": f.ViewModel.CommandText = "changed"; break;
                case "decision": f.ViewModel.BindResponseReadDecisionGate(static () => false); break;
                case "terminal": f.Session.IsUnlocked = false; break;
                case "hidden": f.ViewModel.ObserveStopwatchNativeVisibility(false); break;
            }
        };
        await f.ViewModel.StopwatchStopCommand.ExecuteAsync();
        f.ViewModel.StopwatchDisplay.Should().StartWith("Idle");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task StopwatchHasExplicitUncomposedMalformedTimeAndAuditFailures()
    {
        var uncomposed = await Fixture.CreateInitializedAsync();
        await uncomposed.RunAsync("stopwatch status");
        uncomposed.ViewModel.ResponseBody.Should().Contain("not composed");
        await uncomposed.ViewModel.StopwatchStatusCommand.ExecuteAsync();
        uncomposed.ViewModel.ResponseBody.Should().Contain("native lifetime");
        var time = new StopwatchTime();
        var f = await CreateStopwatchFixture(time);
        foreach (var input in new[] { "stopwatch", "stopwatch start!", "stopwatch\nstop", "stopwatch lap", "stopwatch status more" })
        {
            await f.RunAsync(input);
            f.ViewModel.ResponseBody.Should().Contain("Command not accepted");
        }
        await f.RunAsync("stopwatch start");
        time.Timestamp = -1;
        await f.RunAsync("stopwatch status");
        f.ViewModel.ResponseBody.Should().Contain("Time observation failed");
        f.ViewModel.StopwatchDisplay.Should().StartWith("Idle");
        f.Audit.Events.Should().Contain(a => string.Equals(a.ActionId, "stopwatch.status", StringComparison.Ordinal) && a.Outcome == SecurityAuditOutcome.Failed);
        f.Audit.BeforeWrite = a => { if (a.ActionId.StartsWith("stopwatch.", StringComparison.Ordinal)) { throw new IOException("audit failure"); } };
        await f.RunAsync("stopwatch start");
        f.ViewModel.StopwatchDisplay.Should().StartWith("Idle");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task StopwatchTraceIsHostResolvedAndContentFreeWithCorrelatedAuditOutcomes()
    {
        var f = await CreateStopwatchFixture(new());
        var activities = new List<HostActivity>();
        f.Audit.BeforeWrite = a =>
        {
            if (a.ActionId.StartsWith("stopwatch.", StringComparison.Ordinal))
            {
                activities.Add(HostActivity.RequireCurrent());
                HostActivity.RequireCurrent().Activity!.Baggage.Should().BeEmpty();
            }
        };
        using var hostile = new System.Diagnostics.Activity("incoming").AddBaggage("kora.session.id", "hostile").Start();
        await f.RunAsync("stopwatch start");
        activities.Should().HaveCount(2).And.OnlyContain(a => a.Outcome == HostOperationOutcome.Completed);
        var audits = f.Audit.Events.Where(a => string.Equals(a.ActionId, "stopwatch.start", StringComparison.Ordinal)).ToArray();
        audits.Should().HaveCount(2);
        audits[1].CorrelationId.Should().Be(audits[0].CorrelationId);
        audits[1].Outcome.Should().Be(SecurityAuditOutcome.Succeeded);
        System.Diagnostics.Activity.Current.Should().BeSameAs(hostile);
        activities[0].Activity!.ParentId.Should().BeNull();
    }

    private static async Task<Fixture> CreateStopwatchFixture(StopwatchTime time)
    {
        var f = new Fixture(stopwatchWorkflow: new StopwatchWorkflow(time, NullLogger<StopwatchWorkflow>.Instance));
        await f.ViewModel.InitializeAsync();
        return f;
    }

    [Theory]
    [InlineData("Stopwatch")]
    [InlineData("StopwatchStatus")]
    [InlineData("Stopwatch Status")]
    public async Task StopwatchNamesNeverConfuseUnprefixedFamilyWithVoiceActivation(string name)
    {
        var f = await CreateStopwatchFixture(new());
        await f.RunAsync("set assistant name to " + name);
        await f.RunAsync("stopwatch start");
        f.ViewModel.StopwatchDisplay.Should().StartWith("Running");
        await f.RaiseActivatedTranscriptAsync("stopwatch status", 1);
        f.ViewModel.ResponseBody.Should().Contain("denied");
        await f.RaiseActivatedTranscriptAsync(name + ", stopwatch start", 1);
        f.ViewModel.StopwatchDisplay.Should().StartWith("Running");
        await f.RaiseActivatedTranscriptAsync(name + ", stopwatch reset", 1);
        f.ViewModel.StopwatchDisplay.Should().StartWith("Idle");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task StopwatchPriorOwnerRunIsRetiredBeforeAFreshOwnerCanReadIt()
    {
        var f = await CreateStopwatchFixture(new());
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        await f.RunAsync("stopwatch start");
        owner++;
        await f.RunAsync("stopwatch status");
        f.ViewModel.ResponseBody.Should().Contain("previous ownership was retired");
        await f.RunAsync("stopwatch status");
        f.ViewModel.StopwatchDisplay.Should().StartWith("Idle");
        f.CallState.SetState(CallState.Active);
        await f.RunAsync("stopwatch status");
        f.ViewModel.ResponseBody.Should().Contain("denied");
    }

    [Fact]
    public async Task StopwatchUnavailableNameAndNonUserNativeOriginCannotAcquireAuthority()
    {
        var f = new Fixture(stopwatchWorkflow: new StopwatchWorkflow(new StopwatchTime(), NullLogger<StopwatchWorkflow>.Instance));
        f.NamePreferences.Name = "Supported Commands";
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("Kora, stopwatch start");
        f.ViewModel.ResponseBody.Should().Contain("denied");
        await f.RunAsync("stopwatch start");
        f.ViewModel.StopwatchDisplay.Should().StartWith("Running");
        f.ViewModel.BindStopwatchNativeLifetime(true);
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request);
        await f.ViewModel.StopwatchStatusCommand.ExecuteAsync();
        f.ViewModel.ResponseBody.Should().Contain("denied");
        f.ViewModel.StopwatchDisplay.Should().StartWith("Idle");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopwatchAuditCancellationRetiresBeforeAnyLatePresentation(bool terminal)
    {
        var f = await CreateStopwatchFixture(new());
        f.Audit.BeforeWrite = audit =>
        {
            if (audit.ActionId.StartsWith("stopwatch.", StringComparison.Ordinal)
                && terminal == (audit.Outcome == SecurityAuditOutcome.Succeeded))
            { throw new OperationCanceledException("audit cancelled"); }
        };
        await f.RunAsync("stopwatch start");
        f.ViewModel.ResponseBody.Should().Contain("cancelled");
        f.ViewModel.StopwatchDisplay.Should().StartWith("Idle");
    }

    [Fact]
    public async Task StopwatchOriginalVoiceGenerationCannotSurviveQueueRevocation()
    {
        var f = await CreateStopwatchFixture(new());
        await f.RaiseActivatedTranscriptAsync("Kora, stopwatch start", 1);
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        f.Dispatcher.BeforeInvoke = f.Voice.AdvanceGeneration;
        await f.Voice.RaiseTranscriptAsync("Kora, stopwatch status", 1);
        f.ViewModel.Transcript.Should().Contain("Stopwatch unavailable");
        f.ViewModel.StopwatchDisplay.Should().StartWith("Idle");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task StopwatchOriginalPrivateEpochCannotBeRecreatedAfterVoiceQueueing()
    {
        var f = await CreateStopwatchFixture(new());
        await f.RaiseActivatedTranscriptAsync("Kora, stopwatch start", 1);
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        f.Dispatcher.BeforeInvoke = () =>
        {
            var previous = f.PrivacyObservation.Current;
            f.PrivacyObservation.Current = previous with { TopologyRevision = previous.TopologyRevision + 1 };
        };
        await f.Voice.RaiseTranscriptAsync("Kora, stopwatch status", 1);
        f.ViewModel.ResponseBody.Should().Contain("denied");
        f.ViewModel.StopwatchDisplay.Should().StartWith("Idle");
    }

    [Fact]
    public async Task StopwatchPresentationDoesNotInventAnUnboundWindowOrRevealAfterPrivateAdmissionLoss()
    {
        var f = new Fixture(subscribeToWindowActions: false,
            stopwatchWorkflow: new StopwatchWorkflow(new StopwatchTime(), NullLogger<StopwatchWorkflow>.Instance));
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("stopwatch help");
        f.ViewModel.ResponseBody.Should().Contain(StopwatchCommand.Syntax);
        f.ViewModel.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(f.ViewModel.IsStopwatchPanelVisible), StringComparison.Ordinal))
            { f.Session.IsUnlocked = false; }
        };
        await f.RunAsync("stopwatch help");
        f.ViewModel.CanRevealPrivatePresentation.Should().BeFalse();
    }

    private sealed class StopwatchTime : TimeProvider
    {
        public long Timestamp { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Timestamp;
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("Wall clock forbidden.");
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            throw new InvalidOperationException("Automatic polling forbidden.");
    }
}
