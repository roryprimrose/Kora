using AwesomeAssertions;
using Kora.Application.Interaction;
using Kora.Application.UnitTests.Interaction;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task CountdownCommandsAndNativeControlsShareOneRunWithoutSpeechModelStorageOrTranscriptTicks()
    {
        var f = await Fixture.CreateInitializedAsync();
        var time = new CountdownTestClock();
        using var timer = new CountdownWorkflow(time, f.Dispatcher, NullLogger<CountdownWorkflow>.Instance);
        f.ViewModel.BindCountdown(timer);
        var opens = 0;
        f.ViewModel.CountdownRequested += (_, _) => opens++;
        await f.RunAsync("set assistant name to Nova");
        var events = f.Events.ToArray();
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        var auditCount = f.Audit.Events.Count;
        await f.RunAsync("timer start 3");
        var transcript = f.ViewModel.Transcript;
        time.Advance(TimeSpan.FromSeconds(1));
        timer.Status.Should().Contain("2 seconds");
        f.ViewModel.Transcript.Should().Be(transcript);
        f.ViewModel.ExecuteNativeCountdown(CountdownOperation.Status);
        f.ViewModel.Transcript.Should().Contain("2 seconds");
        await f.RunAsync("timer start 1");
        f.ViewModel.Transcript.Should().Contain("not replaced");
        f.ViewModel.ExecuteNativeCountdown(CountdownOperation.Cancel);
        timer.Status.Should().Contain("cancelled");
        await f.RunAsync("timer start 1");
        transcript = f.ViewModel.Transcript;
        time.Advance(TimeSpan.FromSeconds(1));
        timer.Status.Should().Contain("complete");
        f.ViewModel.Transcript.Should().Be(transcript);
        await f.RunAsync("timer help");
        f.ViewModel.Transcript.Should().Be(CountdownCommand.Syntax);
        await f.RunAsync("timer start +2");
        f.ViewModel.Transcript.Should().Be(CountdownCommand.Syntax);
        opens.Should().Be(3);
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.Events.Should().Equal(events);
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        f.Audit.Events.Should().HaveCount(auditCount);
        await f.RaiseActivatedTranscriptAsync("Nova, timer status", 1);
        f.Voice.StartedPhrases.Should().Contain("timer status").And.Contain("Nova timer status").And.NotContain("Kora timer status");
        await f.RaiseActivatedTranscriptAsync("timer status", 1);
        f.ViewModel.Transcript.Should().Contain("unavailable");
        var invalid = () => f.ViewModel.ExecuteNativeCountdown(CountdownOperation.Start);
        invalid.Should().Throw<ArgumentOutOfRangeException>();
        f.ViewModel.BindCountdown(null);
        f.ViewModel.ExecuteNativeCountdown(CountdownOperation.Cancel);
        f.ViewModel.Transcript.Should().Contain("unavailable");
        await f.RunAsync("timer status");
        f.ViewModel.Transcript.Should().Contain("unavailable");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimerNeverAnswersOrApprovesPendingDecisions(bool approval)
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "Ready");
        if (approval) { f.Reasoner.Action = BuiltInAction.LockMachine; }
        else { f.Reasoner.Question = new("Which target?", ["One", "Two"]); }
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("choose");
        await f.ViewModel.ActiveReasoningTask!;
        using var timer = new CountdownWorkflow(new CountdownTestClock(), f.Dispatcher, NullLogger<CountdownWorkflow>.Instance);
        f.ViewModel.BindCountdown(timer);
        f.ViewModel.CountdownRequested += (_, _) => { };
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        var requests = f.Reasoner.Requests.Count;
        await f.RunAsync("timer start 2");
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        await f.RaiseActivatedTranscriptAsync("Kora, timer status and approve once", 1);
        f.ViewModel.IsModelQuestionPending.Should().Be(!approval);
        f.ViewModel.IsModelActionApprovalPending.Should().Be(approval);
        f.Reasoner.Requests.Should().HaveCount(requests);
        f.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task TimerPreservesGrantReviewAndIsAvailableWhileBusy()
    {
        var f = await Fixture.CreateInitializedAsync();
        using var timer = new CountdownWorkflow(new CountdownTestClock(), f.Dispatcher, NullLogger<CountdownWorkflow>.Instance);
        f.ViewModel.BindCountdown(timer);
        f.ViewModel.CountdownRequested += (_, _) => { };
        await f.ViewModel.PrepareGrantChangeAsync(new(
            GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Session));
        await f.RunAsync("timer help");
        f.ViewModel.IsGrantChangePending.Should().BeTrue();
        await f.ViewModel.RejectPendingGrantChangeAsync();
        f.Probe.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = f.ViewModel.DetectMicrophonesAsync();
        f.ViewModel.CommandText = "timer status";
        f.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue();
        await f.RunAsync("timer status");
        f.Probe.Gate.SetResult();
        await busy;
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("owner-revision")]
    [InlineData("owner-rebind")]
    [InlineData("call")]
    [InlineData("call-roundtrip")]
    [InlineData("topology")]
    [InlineData("privacy")]
    [InlineData("dispose")]
    public async Task CountdownRetiresWithoutCompletionWhenOriginalLifetimeIsLost(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        var time = new CountdownTestClock();
        using var timer = new CountdownWorkflow(time, f.Dispatcher, NullLogger<CountdownWorkflow>.Instance);
        f.ViewModel.BindCountdown(timer);
        f.ViewModel.CountdownRequested += (_, _) => { };
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(() => !string.Equals(change, "owner", StringComparison.Ordinal) || owner == 1, () => owner);
        await f.RunAsync("timer start 2");
        switch (change)
        {
            case "owner":
            case "owner-revision": owner++; break;
            case "owner-rebind": f.ViewModel.BindCallOwnershipGate(() => true, () => owner); break;
            case "call": f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; break;
            case "call-roundtrip":
                f.CallState.SetState(CallState.Active);
                f.CallState.SetState(CallState.Clear);
                await f.Dispatcher.LastInvocation;
                break;
            case "topology":
                f.PrivacyObservation.Current = f.PrivacyObservation.Current with
                { TopologyRevision = f.PrivacyObservation.Current.TopologyRevision + 1 };
                break;
            case "privacy": f.Session.IsUnlocked = false; break;
            case "dispose": f.ViewModel.Dispose(); break;
        }
        time.Advance(TimeSpan.FromSeconds(3));
        timer.Status.Should().Contain("cancelled").And.NotContain("complete");
        timer.IsRunning.Should().BeFalse();
        f.ViewModel.ExecuteNativeCountdown(CountdownOperation.Status);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("owner-revision")]
    [InlineData("owner-rebind")]
    [InlineData("call")]
    [InlineData("call-roundtrip")]
    [InlineData("name")]
    [InlineData("name-case")]
    [InlineData("original-owner-denied")]
    [InlineData("privacy")]
    [InlineData("generation")]
    [InlineData("dispose")]
    public async Task QueuedVoiceCannotAcquireReplacementCountdownAuthority(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        var time = new CountdownTestClock();
        using var timer = new CountdownWorkflow(time, f.Dispatcher, NullLogger<CountdownWorkflow>.Instance);
        f.ViewModel.BindCountdown(timer);
        f.ViewModel.CountdownRequested += (_, _) => { };
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        await f.RaiseActivatedTranscriptAsync("Kora, timer help", 1);
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        if (string.Equals(change, "original-owner-denied", StringComparison.Ordinal))
        { f.ViewModel.BindCallOwnershipGate(static () => false, () => owner); }
        f.Dispatcher.BeforeInvoke = () =>
        {
            f.Dispatcher.BeforeInvoke = null;
            switch (change)
            {
                case "owner": f.ViewModel.BindCallOwnershipGate(static () => false); break;
                case "owner-revision": owner++; break;
                case "owner-rebind":
                    f.ViewModel.BindCallOwnershipGate(static () => false, () => owner);
                    f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
                    break;
                case "call": f.CallState.SetState(CallState.Active); break;
                case "call-roundtrip":
                    f.CallState.SetState(CallState.Active);
                    f.CallState.SetState(CallState.Clear);
                    break;
                case "name": f.ViewModel.GetType().GetProperty(nameof(f.ViewModel.AssistantName))!.SetValue(f.ViewModel, "Nova"); break;
                case "name-case": f.ViewModel.GetType().GetProperty(nameof(f.ViewModel.AssistantName))!.SetValue(f.ViewModel, "KORA"); break;
                case "original-owner-denied": f.ViewModel.BindCallOwnershipGate(static () => true, () => owner); break;
                case "privacy": f.Session.IsUnlocked = false; break;
                case "generation": f.Voice.AdvanceGeneration(); break;
                case "dispose": f.ViewModel.Dispose(); break;
            }
        };
        await f.Voice.RaiseTranscriptAsync("Kora, timer start 2", 1);
        time.Timers.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task TimerIsUnavailableWithoutPresenterOrValidNameOrClearCall()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("timer help");
        f.ViewModel.Transcript.Should().Contain("unavailable");
        f.ViewModel.ExecuteNativeCountdown(CountdownOperation.Status);
        f.ViewModel.Transcript.Should().Contain("unavailable");
        using var timer = new CountdownWorkflow(new CountdownTestClock(), f.Dispatcher, NullLogger<CountdownWorkflow>.Instance);
        f.ViewModel.BindCountdown(timer);
        await f.RunAsync("timer start 1");
        f.ViewModel.Transcript.Should().Contain("unavailable");
        f.ViewModel.ExecuteNativeCountdown(CountdownOperation.Status);
        f.ViewModel.Transcript.Should().Contain("unavailable");
        f.ViewModel.CountdownRequested += (_, _) => { };
        f.CallState.SetState(CallState.Active);
        await f.Dispatcher.LastInvocation;
        await f.RunAsync("timer start 1");
        f.ViewModel.Transcript.Should().Contain("unavailable");
        timer.IsRunning.Should().BeFalse();
        var badName = new Fixture();
        badName.NamePreferences.Name = "\0";
        await badName.ViewModel.InitializeAsync();
        await badName.RunAsync("timer status");
        badName.ViewModel.Transcript.Should().Contain("unavailable");
    }

    [Theory]
    [InlineData(Kora.Core.Auditing.SecurityAuditInitiator.LocalUser)]
    [InlineData(Kora.Core.Auditing.SecurityAuditInitiator.ModelSuggestion)]
    [InlineData((Kora.Core.Auditing.SecurityAuditInitiator)99)]
    public async Task TimerRejectsNonCommandInitiators(Kora.Core.Auditing.SecurityAuditInitiator initiator)
    {
        var f = await Fixture.CreateInitializedAsync();
        var routing = f.ViewModel.GetType().GetMethod("HandleTranscriptAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (Task)routing.Invoke(f.ViewModel, ["timer start 1", 1f, initiator, null, null, null])!;
        f.ViewModel.Transcript.Should().Contain("unavailable");
        await Kora.Application.Hosting.HostRequestRunner.RunAsync(RequestOrigin.HostSystem,
            () => f.RunAsync("timer start 1"));
        f.ViewModel.Transcript.Should().Contain("unavailable");
    }

    [Fact]
    public async Task PresenterDisposalDuringStartupCannotLeaveAnActiveTimerOrPublishAnOpening()
    {
        var f = await Fixture.CreateInitializedAsync();
        var time = new CountdownTestClock();
        using var timer = new CountdownWorkflow(time, f.Dispatcher, NullLogger<CountdownWorkflow>.Instance);
        f.ViewModel.BindCountdown(timer);
        var opens = 0;
        EventHandler presenter = (_, _) => opens++;
        f.ViewModel.CountdownRequested += presenter;
        timer.PropertyChanged += (_, _) =>
        {
            f.ViewModel.CountdownRequested -= presenter;
            f.ViewModel.BindCountdown(null);
        };
        await f.RunAsync("timer start 1");
        opens.Should().Be(0);
        timer.IsRunning.Should().BeFalse();
        time.Timers.Should().OnlyContain(item => item.Disposed);
    }

    [Theory]
    [InlineData("Timer")]
    [InlineData("Timer Status")]
    public async Task TimerNamedAssistantStillRequiresADistinctVoiceActivationPrefix(string name)
    {
        var f = await Fixture.CreateInitializedAsync();
        using var timer = new CountdownWorkflow(new CountdownTestClock(), f.Dispatcher, NullLogger<CountdownWorkflow>.Instance);
        f.ViewModel.BindCountdown(timer);
        f.ViewModel.CountdownRequested += (_, _) => { };
        await f.RunAsync("set assistant name to " + name);
        await f.RaiseActivatedTranscriptAsync(name + ", timer start 60", 1);
        timer.IsRunning.Should().BeTrue();
        await f.RaiseActivatedTranscriptAsync("timer cancel", 1);
        f.ViewModel.Transcript.Should().Contain("unavailable");
        timer.IsRunning.Should().BeTrue();
        await f.RunAsync("timer cancel");
        timer.IsRunning.Should().BeFalse();
    }
}
