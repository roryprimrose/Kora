using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task WorkspaceNavigationHasContentFreeHostOwnedRequestRootsAndTruthfulTermination()
    {
        var f = await Fixture.CreateInitializedAsync();
        var scopes = new List<Kora.Core.Diagnostics.HostActivity>();
        using var hostile = new System.Diagnostics.Activity("untrusted incoming")
            .AddBaggage("kora.session.id", Guid.NewGuid().ToString("D")).Start();
        f.ViewModel.WorkspaceSurfaceRequested += _ =>
        {
            var scope = Kora.Core.Diagnostics.HostActivity.RequireCurrent();
            scope.Activity!.ParentId.Should().BeNull();
            scope.Activity.Baggage.Should().BeEmpty();
            scope.Activity.TagObjects.Select(pair => pair.Key).Should().OnlyContain(key => key.StartsWith("kora.", StringComparison.Ordinal));
            scopes.Add(scope);
            return Task.CompletedTask;
        };
        await f.RunAsync("open all work");
        await f.RunAsync("open compact conversation");
        scopes.Should().HaveCount(2).And.OnlyContain(scope => scope.Outcome == Kora.Core.Diagnostics.HostOperationOutcome.Completed);
        scopes[0].Request.Should().NotBe(scopes[1].Request);
        System.Diagnostics.Activity.Current.Should().BeSameAs(hostile);
    }

    [Theory]
    [InlineData("open all work", WorkspaceSurfaceTarget.AllWork)]
    [InlineData("open compact conversation", WorkspaceSurfaceTarget.CompactConversation)]
    [InlineData("show me the current sessions", WorkspaceSurfaceTarget.Sessions)]
    [InlineData("select the third session", WorkspaceSurfaceTarget.SelectSession)]
    public async Task WorkspaceNavigationUsesTypedAndCurrentNameVoiceWithoutReasoning(string phrase, WorkspaceSurfaceTarget target)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("set assistant name to Nova");
        var observed = new List<(WorkspaceSurfaceTarget Target, RequestOrigin? Origin)>();
        f.ViewModel.WorkspaceSurfaceRequested += request =>
        {
            request.IsAdmitted().Should().BeTrue();
            request.SessionPosition.Should().Be(target == WorkspaceSurfaceTarget.SelectSession ? 3 : null);
            observed.Add((request.Target, Kora.Core.Diagnostics.HostActivity.Current?.Request.Origin));
            return Task.CompletedTask;
        };
        await f.RunAsync(phrase);
        await f.RaiseActivatedTranscriptAsync("Nova, " + phrase, 1);
        observed.Should().Equal((target, RequestOrigin.LocalUi), (target, RequestOrigin.ActivatedVoice));
        f.Voice.StartedPhrases.Should().Contain(phrase).And.Contain("Nova " + phrase)
            .And.NotContain("Kora " + phrase);
        await f.RaiseActivatedTranscriptAsync(phrase, 1);
        observed.Should().HaveCount(2);
        f.ViewModel.ResponseBody.Should().Contain("current available assistant-name prefix");
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task WorkspaceNavigationHasExplicitUnavailableAndMalformedBoundariesAndRoutesSessionsThroughAdmission()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("open all work");
        f.ViewModel.ResponseBody.Should().Contain("native surface route");
        var calls = 0;
        f.ViewModel.WorkspaceSurfaceRequested += _ => { calls++; return Task.CompletedTask; };
        await f.RunAsync("open saved conversation drafts read selected");
        f.ViewModel.ResponseBody.Should().Contain("Navigation command not accepted");
        var sessions = 0;
        f.ViewModel.SessionsRequested += (_, _) => sessions++;
        await f.RunAsync("open sessions");
        sessions.Should().Be(0);
        calls.Should().Be(1);
        f.Reasoner.Requests.Should().BeEmpty();
        f.Session.IsUnlocked = false;
        await f.RunAsync("open compact conversation");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task WorkspaceNavigationIsAvailableWhileBusyWithoutDispatchingWork()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.Probe.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = f.ViewModel.DetectMicrophonesAsync();
        var calls = 0;
        f.ViewModel.WorkspaceSurfaceRequested += _ => { calls++; return Task.CompletedTask; };
        f.ViewModel.CommandText = "open all work";
        f.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue();
        await f.RunAsync("open all work");
        calls.Should().Be(1);
        f.ViewModel.IsBusy.Should().BeTrue();
        f.Probe.Gate.SetResult();
        await busy;
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task WorkspaceNavigationNeverAnswersOrRetargetsPendingDecisions()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        f.Reasoner.Question = new("Which target?", ["One", "Two"]);
        await f.ViewModel.InitializeAsync();
        var calls = 0;
        f.ViewModel.WorkspaceSurfaceRequested += _ => { calls++; return Task.CompletedTask; };
        await f.RunAsync("choose");
        await f.ViewModel.ActiveReasoningTask!;
        f.ViewModel.CommandText = "unfinished answer";
        await f.RaiseActivatedTranscriptAsync("Kora, show sessions", 1);
        await f.RaiseActivatedTranscriptAsync("Kora, select the third session", 1);
        await f.RaiseActivatedTranscriptAsync("Kora, open compact conversation", 1);
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        f.ViewModel.CommandText.Should().Be("unfinished answer");
        await f.RaiseActivatedTranscriptAsync("Kora, open all work extra", 1);
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        f.ViewModel.CommandText.Should().Be("unfinished answer");
        await f.ViewModel.CancelModelQuestionAsync();
        await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
        await f.RunAsync("open compact conversation");
        f.ViewModel.IsGrantChangePending.Should().BeTrue();
        await f.ViewModel.RejectPendingGrantChangeAsync();
        f.Reasoner.Question = null;
        f.Reasoner.Action = BuiltInAction.LockMachine;
        await f.RunAsync("please lock");
        await f.ViewModel.ActiveReasoningTask!;
        await f.RunAsync("open all work");
        f.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        calls.Should().Be(5);
        f.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("edit")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("name")]
    [InlineData("privacy")]
    [InlineData("dispose")]
    public async Task WorkspaceNavigationAdmissionCannotBeReusedAfterEpochChange(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        WorkspaceSurfaceNavigation? captured = null;
        f.ViewModel.WorkspaceSurfaceRequested += request => { captured = request; return Task.CompletedTask; };
        await f.RunAsync("open compact conversation");
        captured!.IsAdmitted().Should().BeTrue();
        var original = captured;
        switch (change)
        {
            case "input": await f.RunAsync("open all work"); break;
            case "edit": f.ViewModel.CommandText = "different input"; break;
            case "owner": owner++; break;
            case "call": f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; break;
            case "name": await f.RunAsync("set assistant name to Nova"); break;
            case "privacy": f.Session.IsUnlocked = false; break;
            case "dispose": f.ViewModel.Dispose(); break;
        }
        original.IsAdmitted().Should().BeFalse();
    }

    [Fact]
    public async Task WorkspaceNavigationRejectsProtectedCallAndUnavailablePrefixWithoutFallback()
    {
        var f = new Fixture();
        f.NamePreferences.Name = "Supported Commands";
        await f.ViewModel.InitializeAsync();
        var calls = 0;
        f.ViewModel.WorkspaceSurfaceRequested += _ => { calls++; return Task.CompletedTask; };
        await f.RunAsync("Kora, open all work");
        f.ViewModel.ResponseBody.Should().Contain("current available assistant-name prefix");
        await f.RunAsync("open all work");
        calls.Should().Be(1);
        f.CallState.SetState(CallState.Active);
        await f.Dispatcher.LastInvocation;
        await f.RunAsync("open compact conversation");
        calls.Should().Be(1);
        f.ViewModel.ResponseBody.Should().Contain("Navigation unavailable");
    }
}
