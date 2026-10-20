using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private const string RetainedTaskQueryId = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";
    private const string PrepareRetainedTaskQuery = "prepare all work task " + RetainedTaskQueryId;

    [Theory]
    [InlineData("Prepare")]
    [InlineData("All")]
    public async Task AllWorkQueryReservedGrammarCannotImpersonateNameActivation(string name)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("set assistant name to " + name);
        f.ViewModel.AssistantName.Should().Be(name);
        var calls = 0;
        f.ViewModel.AllWorkTaskQueryPreparationRequested += request =>
        {
            calls++;
            request.Acknowledge(static () => true);
            return Task.FromResult(true);
        };
        await f.RunAsync(PrepareRetainedTaskQuery);
        calls.Should().Be(1);
        await f.RaiseActivatedTranscriptAsync(PrepareRetainedTaskQuery, 1);
        calls.Should().Be(1);
        f.ViewModel.Transcript.Should().Contain("current available assistant-name prefix");
        await f.RaiseActivatedTranscriptAsync("all work query help", 1);
        f.ViewModel.Transcript.Should().Contain("current available assistant-name prefix");
        await f.RaiseActivatedTranscriptAsync(name + ", " + PrepareRetainedTaskQuery, 1);
        calls.Should().Be(2);
    }

    [Fact]
    public async Task AllWorkQueryPreparationRequiresNativeAcknowledgementAndNeverReasonsOrStoresInput()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("set assistant name to Nova");
        var calls = 0;
        f.ViewModel.AllWorkTaskQueryPreparationRequested += request =>
        {
            calls++;
            request.TaskId.Should().Be(Guid.Parse(RetainedTaskQueryId));
            request.IsAdmitted().Should().BeTrue();
            request.IsPrepared.Should().BeFalse();
            request.ToString().Should().NotContain(RetainedTaskQueryId);
            Kora.Core.Diagnostics.HostActivity.RequireCurrent().Request.Origin.Should()
                .Be(calls == 1 ? RequestOrigin.LocalUi : RequestOrigin.ActivatedVoice);
            request.Acknowledge(static () => true);
            request.IsPrepared.Should().BeTrue();
            return Task.FromResult(true);
        };
        await f.RunAsync(PrepareRetainedTaskQuery);
        f.ViewModel.Transcript.Should().Contain("PREPARED / REVIEW NEEDED").And.NotContain(RetainedTaskQueryId);
        await f.RaiseActivatedTranscriptAsync("Nova, " + PrepareRetainedTaskQuery, 1);
        calls.Should().Be(2);
        f.Voice.StartedPhrases.Should().Contain("Nova all work query help").And.NotContain("Kora all work query help");
        await f.RaiseActivatedTranscriptAsync(PrepareRetainedTaskQuery, 1);
        f.ViewModel.Transcript.Should().Contain("current available assistant-name prefix");
        calls.Should().Be(2);
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task AllWorkQueryHelpIsConstantReadFreeAndDoesNotRequireNativeBinding()
    {
        var f = await Fixture.CreateInitializedAsync();
        var calls = 0;
        f.ViewModel.AllWorkTaskQueryPreparationRequested += _ => { calls++; return Task.FromResult(false); };
        await f.RunAsync(AllWorkTaskQueryCommand.HelpPhrase);
        f.ViewModel.Transcript.Should().Be(AllWorkTaskQueryCommand.Guidance);
        await f.RaiseActivatedTranscriptAsync("Kora, all work query help", 1);
        f.ViewModel.Transcript.Should().Be(AllWorkTaskQueryCommand.Guidance);
        calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task AllWorkQueryMalformedUnboundAndMissingNativeAcknowledgementAreExplicit()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync(PrepareRetainedTaskQuery);
        f.ViewModel.Transcript.Should().Contain("not bound");
        await f.RunAsync("prepare all work task current");
        f.ViewModel.Transcript.Should().Contain("not accepted");
        await f.RunAsync("all work query help extra");
        f.ViewModel.Transcript.Should().Contain("not accepted");
        f.ViewModel.AllWorkTaskQueryPreparationRequested += _ => Task.FromResult(true);
        await f.RunAsync(PrepareRetainedTaskQuery);
        f.ViewModel.Transcript.Should().Contain("did not acknowledge");
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData("input")]
    [InlineData("edit-and-back")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("name")]
    [InlineData("privacy")]
    [InlineData("dispose")]
    [InlineData("unbind")]
    [InlineData("native")]
    public async Task AllWorkQueryCannotAcknowledgeAfterOriginalAdmissionOrNativeSourceChanges(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AllWorkTaskQueryPreparation? captured = null;
        var native = true;
        async Task<bool> Handler(AllWorkTaskQueryPreparation request)
        {
            captured = request;
            request.Acknowledge(() => native);
            entered.SetResult();
            await release.Task;
            return true;
        }
        f.ViewModel.AllWorkTaskQueryPreparationRequested += Handler;
        var opening = f.RunAsync(PrepareRetainedTaskQuery);
        await entered.Task;
        captured!.IsPrepared.Should().BeTrue();
        switch (change)
        {
            case "input": await f.RunAsync("all work query help"); break;
            case "edit-and-back":
                f.ViewModel.CommandText = "changed";
                f.ViewModel.CommandText = PrepareRetainedTaskQuery;
                break;
            case "owner": owner++; break;
            case "call": f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; break;
            case "name": await f.RunAsync("set assistant name to Nova"); break;
            case "privacy": f.Session.IsUnlocked = false; break;
            case "dispose": f.ViewModel.Dispose(); break;
            case "unbind": f.ViewModel.AllWorkTaskQueryPreparationRequested -= Handler; break;
            case "native": native = false; break;
        }
        captured.IsPrepared.Should().BeFalse();
        release.SetResult();
        await opening;
        f.ViewModel.Transcript.Should().Contain("unavailable");
    }

    [Fact]
    public async Task AllWorkQueryRejectsOriginalPrivacyCallAndUnavailableNameWithoutFallback()
    {
        var f = await Fixture.CreateInitializedAsync();
        var calls = 0;
        f.ViewModel.AllWorkTaskQueryPreparationRequested += _ => { calls++; return Task.FromResult(false); };
        f.Session.IsUnlocked = false;
        await f.RunAsync(PrepareRetainedTaskQuery);
        f.Session.IsUnlocked = true;
        f.CallState.SetState(CallState.Active);
        await f.Dispatcher.LastInvocation;
        await f.RunAsync(PrepareRetainedTaskQuery);
        calls.Should().Be(0);
        var unavailable = new Fixture();
        unavailable.NamePreferences.Name = "Supported Commands";
        await unavailable.ViewModel.InitializeAsync();
        await unavailable.RunAsync("Kora, " + PrepareRetainedTaskQuery);
        unavailable.ViewModel.Transcript.Should().Contain("current available assistant-name prefix");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllWorkQueryNativeRefusalAndExceptionNeverBecomeSuccess(bool throws)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.AllWorkTaskQueryPreparationRequested += _ => throws
            ? throw new InvalidOperationException("Native view unavailable.")
            : Task.FromResult(false);
        await f.RunAsync(PrepareRetainedTaskQuery);
        f.ViewModel.Transcript.Should().Contain("unavailable").And.NotContain("PREPARED / REVIEW NEEDED");
    }

    [Fact]
    public async Task AllWorkQueryPreservesPendingQuestionAndSecurityPanesAndRemainsAvailableWhileBusy()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        f.Reasoner.Question = new("Which target?", ["One", "Two"]);
        await f.ViewModel.InitializeAsync();
        f.ViewModel.AllWorkTaskQueryPreparationRequested += request =>
        {
            request.Acknowledge(static () => true);
            return Task.FromResult(true);
        };
        await f.RunAsync("choose");
        await f.ViewModel.ActiveReasoningTask!;
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync(PrepareRetainedTaskQuery);
        await f.RunAsync("all work query help");
        await f.RunAsync("prepare all work task latest");
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.CommandText = "unfinished answer";
        await f.RaiseActivatedTranscriptAsync("Kora, " + PrepareRetainedTaskQuery, 1);
        await f.RaiseActivatedTranscriptAsync("Kora, all work query help", 1);
        await f.RaiseActivatedTranscriptAsync("Kora, prepare all work task latest", 1);
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        f.ViewModel.CommandText.Should().Be("unfinished answer");
        await f.ViewModel.CancelModelQuestionAsync();
        await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add,
            BuiltInAction.LockMachine, ModelApprovalScope.Always));
        body = f.ViewModel.ResponseBody;
        await f.RunAsync(PrepareRetainedTaskQuery);
        f.ViewModel.IsGrantChangePending.Should().BeTrue();
        f.ViewModel.ResponseBody.Should().Be(body);
        await f.ViewModel.RejectPendingGrantChangeAsync();
        f.Reasoner.Question = null;
        f.Reasoner.Action = BuiltInAction.LockMachine;
        await f.RunAsync("please lock");
        await f.ViewModel.ActiveReasoningTask!;
        body = f.ViewModel.ResponseBody;
        await f.RunAsync(PrepareRetainedTaskQuery);
        await f.RunAsync("all work query help");
        await f.RunAsync("prepare all work task current");
        f.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        f.ViewModel.ResponseBody.Should().Be(body);
        f.Probe.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = f.ViewModel.DetectMicrophonesAsync();
        f.ViewModel.CommandText = PrepareRetainedTaskQuery;
        f.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue();
        await f.RunAsync(PrepareRetainedTaskQuery);
        f.ViewModel.IsBusy.Should().BeTrue();
        f.Probe.Gate.SetResult();
        await busy;
        f.Session.LockCalls.Should().Be(0);
    }
}
