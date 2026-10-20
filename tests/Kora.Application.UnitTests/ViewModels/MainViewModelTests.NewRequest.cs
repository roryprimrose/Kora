using System.Diagnostics;

using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Platform;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task NewRequestNavigationKeepsOriginalHostIdentityAndTerminatesItsChildTrace()
    {
        var f = await Fixture.CreateInitializedAsync();
        var started = new List<Activity>();
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "Kora.Application", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = started.Add,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        Activity? navigation = null;
        f.ViewModel.NewRequestOpeningRequested += admitted =>
        {
            admitted().Should().BeTrue();
            var current = HostActivity.RequireCurrent();
            current.Request.Origin.Should().Be(RequestOrigin.LocalUi);
            navigation = current.Activity;
            navigation!.ParentId.Should().NotBeNull();
            return Task.FromResult(true);
        };
        await f.RunAsync("open new request");
        navigation.Should().NotBeNull();
        var parent = started.Single(activity => string.Equals(activity.Id, navigation!.ParentId, StringComparison.Ordinal));
        navigation!.TraceId.Should().Be(parent.TraceId);
        navigation.GetTagItem("kora.request.id").Should().Be(parent.GetTagItem("kora.request.id"));
        navigation.Status.Should().Be(ActivityStatusCode.Ok);
        stopped.Should().Contain(navigation!);
        HostActivity.Current.Should().BeNull();
    }

    [Fact]
    public async Task NewRequestExactTypedAndCurrentNameVoiceOnlyRequestNativeNavigation()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova");
        var origins = new List<RequestOrigin>();
        string? voiceBody = null;
        f.ViewModel.NewRequestOpeningRequested += admitted =>
        {
            admitted().Should().BeTrue();
            origins.Add(HostActivity.RequireCurrent().Request.Origin);
            if (origins[^1] == RequestOrigin.ActivatedVoice) { voiceBody = f.ViewModel.ResponseBody; }
            return Task.FromResult(true);
        };
        var records = f.HostStore.Records.Count;
        var audit = f.Audit.Events.Count;
        var starts = f.Voice.StartCalls;
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync("open new request");
        await f.RunAsync("Nova, open new request");
        f.ViewModel.ResponseBody.Should().Be(body);
        f.Voice.StartCalls.Should().Be(starts);
        f.Audit.Events.Should().HaveCount(audit);
        await f.RaiseActivatedTranscriptAsync("Nova, open new request", 1);
        origins.Should().Equal(RequestOrigin.LocalUi, RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice);
        f.Voice.StartedPhrases.Should().Contain("open new request").And.Contain("Nova open new request")
            .And.NotContain("Kora open new request");
        f.ViewModel.Transcript.Should().Contain("activated only");
        f.ViewModel.ResponseBody.Should().Be(voiceBody);
        f.HostStore.Records.Should().HaveCount(records);
        f.Reasoner.Requests.Should().BeEmpty();
        f.Session.LockCalls.Should().Be(0);
        await f.RaiseActivatedTranscriptAsync("open new request", 1);
        origins.Should().HaveCount(3);
        f.ViewModel.Transcript.Should().Contain("current assistant name prefix");
    }

    [Theory]
    [InlineData("open new request extra")]
    [InlineData("Kora, open new request; submit")]
    [InlineData("open-new-request")]
    [InlineData("open new request\n")]
    public async Task NewRequestMalformedReservedInputsNeverOpenReasonOrAnswer(string input)
    {
        var f = await Fixture.CreateInitializedAsync();
        var calls = 0;
        f.ViewModel.NewRequestOpeningRequested += _ => { calls++; return Task.FromResult(true); };
        await f.RunAsync(input);
        f.ViewModel.Transcript.Should().Contain("not accepted");
        calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData("question")]
    [InlineData("grant")]
    [InlineData("action")]
    public async Task NewRequestCommandsPreserveTheEntirePendingDecision(string pending)
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "Ready");
        if (pending is "question") { f.Reasoner.Question = new("Keep this exact choice?", ["One", "Two"]); }
        if (pending is "action") { f.Reasoner.Action = BuiltInAction.LockMachine; }
        await f.ViewModel.InitializeAsync();
        if (pending is "grant")
        {
            await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add,
                BuiltInAction.LockMachine, ModelApprovalScope.Always));
        }
        else
        {
            await f.RunAsync("please choose this work");
            await f.ViewModel.ActiveReasoningTask!;
        }
        var calls = 0;
        f.ViewModel.NewRequestOpeningRequested += _ => { calls++; return Task.FromResult(true); };
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        var choices = f.ViewModel.ModelQuestionChoices;
        var audit = f.Audit.Events.Count;
        var requests = f.Reasoner.Requests.Count;
        foreach (var input in new[] { "open new request", "open new request extra" })
        {
            await f.RunAsync(input);
            f.ViewModel.Transcript.Should().Contain("opening blocked");
            f.ViewModel.ResponseTitle.Should().Be(title);
            f.ViewModel.ResponseBody.Should().Be(body);
            f.ViewModel.ModelQuestionChoices.Should().BeSameAs(choices);
            f.ViewModel.IsResponseInteractionPending.Should().BeTrue();
        }
        f.Audit.Events.Should().HaveCount(audit);
        f.Reasoner.Requests.Should().HaveCount(requests);
        calls.Should().Be(0);
    }

    [Fact]
    public async Task NewRequestDoesNotSupersedeAnUnpublishedApprovalPreparation()
    {
        var f = await Fixture.CreateInitializedAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        f.Audit.BeforeWrite = audit =>
        {
            if (audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            entered.TrySetResult();
            release.Wait(token);
        };
        f.ViewModel.NewRequestOpeningRequested += admitted => Task.FromResult(admitted());
        var preparation = f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add,
            BuiltInAction.LockMachine, ModelApprovalScope.Session));
        try
        {
            await entered.Task.WaitAsync(token);
            await f.RunAsync("open new request");
            f.ViewModel.Transcript.Should().Contain("activated only");
            preparation.IsCompleted.Should().BeFalse();
        }
        finally { release.Set(); }
        await preparation;
        f.ViewModel.IsGrantChangePending.Should().BeTrue();
        f.ViewModel.ResponseBody.Should().Contain(nameof(BuiltInAction.LockMachine));
        f.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("New Request")]
    [InlineData("Open New Request")]
    public async Task NewRequestConflictingNamesFailExplicitlyWithoutRoutingElsewhere(string name)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync(name);
        await f.RunAsync("open new request");
        f.ViewModel.Transcript.Should().Contain("conflicting current assistant name");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task NewRequestMissingNativeBindingAndUnavailableNameAreExplicit()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("open new request");
        f.ViewModel.Transcript.Should().Contain("native composer is not bound").And.Contain("Model navigation is not registered");
        var invalid = new Fixture();
        invalid.NamePreferences.Name = "Supported Commands";
        await invalid.ViewModel.InitializeAsync();
        await invalid.RunAsync("Kora, open new request");
        invalid.ViewModel.Transcript.Should().Contain("available current assistant name prefix");
        invalid.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task NewRequestRemainsNavigationOnlyWhileBusyOrProtectedWithoutCancellingWork()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.Probe.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = f.ViewModel.DetectMicrophonesAsync();
        f.ViewModel.NewRequestOpeningRequested += admitted => Task.FromResult(admitted());
        f.ViewModel.CommandText = "open new request";
        f.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue();
        await f.RunAsync("open new request");
        f.ViewModel.IsBusy.Should().BeTrue();
        f.ViewModel.CommandText = "open new request extra";
        f.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue();
        await f.RunAsync("open new request extra");
        f.ViewModel.IsBusy.Should().BeTrue();
        f.Probe.Gate.SetResult();
        await busy;
        f.CallState.SetState(CallState.Active);
        await f.ViewModel.CallClosureTask;
        await f.RunAsync("open new request");
        f.ViewModel.Transcript.Should().Contain("activated only");
        await f.RaiseActivatedTranscriptAsync("Kora, open new request", 1);
        f.ViewModel.Transcript.Should().Contain("activated only");
        await f.ViewModel.SetAllowVoiceActivationDuringCallsAsync(false);
        var transcript = f.ViewModel.Transcript;
        await f.Voice.RaiseTranscriptAsync("Kora, open new request", 1);
        f.ViewModel.Transcript.Should().Be(transcript);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("owner-revision")]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("dispose")]
    [InlineData("source")]
    public async Task NewRequestFencesOriginalVoiceAdmissionBeforeUiDispatch(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        var ownerRevision = 0L;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => ownerRevision);
        var calls = 0;
        f.ViewModel.NewRequestOpeningRequested += _ => { calls++; return Task.FromResult(true); };
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        f.Dispatcher.BeforeInvoke = () =>
        {
            switch (change)
            {
                case "owner": f.ViewModel.BindCallOwnershipGate(static () => false); break;
                case "owner-revision": ownerRevision++; break;
                case "privacy":
                    f.PrivacyObservation.Current = f.PrivacyObservation.Current with { TopologyRevision = 99 };
                    break;
                case "call": f.CallState.SetState(CallState.Active); break;
                case "dispose": f.ViewModel.Dispose(); break;
                case "source": f.Voice.AdvanceGeneration(); break;
            }
        };
        await f.Voice.RaiseTranscriptAsync("Kora, open new request", 1);
        calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("host")]
    [InlineData("private")]
    public async Task NewRequestRejectsIneligibleTypedOrHostSystemRoutes(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        var calls = 0;
        f.ViewModel.NewRequestOpeningRequested += _ => { calls++; return Task.FromResult(true); };
        if (change is "owner") { f.ViewModel.BindCallOwnershipGate(static () => false); }
        if (change is "private") { f.Session.IsUnlocked = false; }
        if (change is "host")
        {
            using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
                HostActivityLayer.Application, HostOperation.Request);
            await f.RunAsync("open new request");
        }
        else { await f.RunAsync("open new request"); }
        calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not-opened")]
    [InlineData("failure")]
    [InlineData("late")]
    [InlineData("late-failure")]
    [InlineData("name")]
    [InlineData("pending")]
    public async Task NewRequestNeverClaimsSuccessAfterNativeFailureOrAdmissionLoss(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.NewRequestOpeningRequested += async admitted =>
        {
            admitted().Should().BeTrue();
            if (change is "late" or "late-failure") { f.ViewModel.BindCallOwnershipGate(static () => false); }
            if (change is "name") { await f.ViewModel.SetAssistantNameAsync("Nova"); }
            if (change is "pending")
            {
                await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add,
                    BuiltInAction.LockMachine, ModelApprovalScope.Always));
            }
            if (change is "failure" or "late-failure") { throw new IOException("Synthetic native failure"); }
            return false;
        };
        await f.RunAsync("open new request");
        f.ViewModel.Transcript.Should().NotContain("activated only");
        if (change is "failure") { f.ViewModel.Transcript.Should().Contain("opening failed"); }
        if (change is "not-opened") { f.ViewModel.Transcript.Should().Contain("not confirmed"); }
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
