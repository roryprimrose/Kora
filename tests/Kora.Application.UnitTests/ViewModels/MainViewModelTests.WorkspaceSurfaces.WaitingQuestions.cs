using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public void WaitingOverviewGuideIsAvailableInTheNativeDocumentationWithoutChangingItsStartPage()
    {
        var documentation = new Kora.Application.Documentation.EmbeddedUserDocumentationProvider();
        documentation.GetStartPage().Id.Should().Be("readme");
        documentation.GetPages().Single(page => string.Equals(page.Id, "waiting-questions", StringComparison.Ordinal))
            .Markdown.Should().Contain("show sessions needing an answer")
            .And.Contain("**Next** remains available even when a page has zero")
            .And.Contain("physical").And.Contain("does not answer questions");
    }

    [Fact]
    public async Task WaitingQuestionReadKeepsOriginalTypedAndCurrentNameVoiceRequestUntilTypedCompletion()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("set assistant name to Nova");
        var requests = new List<WorkspaceSurfaceNavigation>();
        var scopes = new List<HostActivity>();
        f.ViewModel.WorkspaceSurfaceRequested += request =>
        {
            request.Target.Should().Be(WorkspaceSurfaceTarget.WaitingQuestions);
            request.IsAdmitted().Should().BeTrue();
            request.CancellationToken.IsCancellationRequested.Should().BeFalse();
            request.Request.Should().BeSameAs(HostActivity.RequireCurrent().Request);
            request.Origin.Should().Be(request.Request!.Origin);
            scopes.Add(HostActivity.RequireCurrent());
            requests.Add(request);
            request.Outcome = WorkspaceSurfaceNavigationOutcome.Completed;
            return Task.CompletedTask;
        };
        await f.RunAsync("show sessions needing an answer");
        await f.RaiseActivatedTranscriptAsync("Nova, show sessions needing an answer", 1);
        requests.Select(request => request.Origin).Should().Equal(RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice);
        requests[0].IsAdmitted().Should().BeFalse();
        scopes.Should().OnlyContain(scope => scope.Outcome == HostOperationOutcome.Completed);
        f.Voice.StartedPhrases.Should().Contain("Nova show sessions needing an answer");
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        f.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(WorkspaceSurfaceNavigationOutcome.Unavailable, HostOperationOutcome.Failed, "unavailable")]
    [InlineData(WorkspaceSurfaceNavigationOutcome.Cancelled, HostOperationOutcome.Cancelled, "cancelled")]
    public async Task NavigationOrReadFailureCannotBecomeCompletedObservation(
        WorkspaceSurfaceNavigationOutcome outcome, HostOperationOutcome terminal, string message)
    {
        var f = await Fixture.CreateInitializedAsync();
        HostActivity? scope = null;
        f.ViewModel.WorkspaceSurfaceRequested += request =>
        {
            request.Outcome = outcome;
            scope = HostActivity.RequireCurrent();
            return Task.CompletedTask;
        };
        await f.RunAsync("show sessions needing an answer");
        scope!.Outcome.Should().Be(terminal);
        f.ViewModel.ResponseBody.Should().Contain("Waiting questions " + message);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task HandlerThatOnlyPreparesFieldsHasNoReadSuccess()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.WorkspaceSurfaceRequested += _ => Task.CompletedTask;
        await f.RunAsync("show sessions needing an answer");
        f.ViewModel.ResponseBody.Should().Contain("no fresh bounded observation completed");
    }

    [Theory]
    [InlineData("input")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("privacy")]
    [InlineData("name")]
    public async Task WaitingReadRetiresOriginalCurrencyWhileAwaitingActualPresenter(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        var entered = new TaskCompletionSource<WorkspaceSurfaceNavigation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        HostActivity? scope = null;
        f.ViewModel.WorkspaceSurfaceRequested += async request =>
        {
            scope = HostActivity.RequireCurrent();
            entered.SetResult(request);
            await release.Task;
            request.Outcome = WorkspaceSurfaceNavigationOutcome.Completed;
        };
        var command = f.RunAsync("show sessions needing an answer");
        var navigation = await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        switch (change)
        {
            case "input": f.ViewModel.CommandText = "replacement"; break;
            case "owner": owner++; f.ViewModel.BindCallOwnershipGate(static () => true, () => owner); break;
            case "call": f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; break;
            case "privacy":
                f.Session.IsUnlocked = false;
                f.ViewModel.CloseForObservedPrivacyEvent("Locked", true, Kora.Core.Platform.WindowsSessionState.Locked);
                break;
            case "name": await f.RunAsync("set assistant name to Nova"); break;
        }
        navigation.IsAdmitted().Should().BeFalse();
        command.IsCompleted.Should().BeFalse("withdrawal cannot claim actual reader completion");
        release.SetResult();
        await command;
        scope!.Outcome.Should().Be(HostOperationOutcome.Cancelled);
        f.ViewModel.ResponseBody.Should().Contain("Waiting questions cancelled");
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task MalformedWaitingGrammarDoesNotAnswerAnExistingQuestion()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        f.Reasoner.Question = new("Which target?", ["One", "Two"]);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("choose");
        await f.ViewModel.ActiveReasoningTask!;
        var calls = 0;
        f.ViewModel.WorkspaceSurfaceRequested += _ => { calls++; return Task.CompletedTask; };
        await f.RaiseActivatedTranscriptAsync("Kora, show sessions needing an answer extra", 1);
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        f.ViewModel.ResponseBody.Should().Contain("Navigation command not accepted");
        calls.Should().Be(0);
        await f.ViewModel.CancelModelQuestionAsync();
    }

    [Fact]
    public async Task UnexpectedPresenterFailureRemainsObservedAndTerminatesTheOriginalRequestAsFailed()
    {
        var f = await Fixture.CreateInitializedAsync();
        HostActivity? scope = null;
        f.ViewModel.WorkspaceSurfaceRequested += _ =>
        {
            scope = HostActivity.RequireCurrent();
            throw new InvalidOperationException("Presenter failure.");
        };
        var run = () => f.RunAsync("show sessions needing an answer");
        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("Presenter failure.");
        scope!.Outcome.Should().Be(HostOperationOutcome.Failed);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("name")]
    [InlineData("binding")]
    [InlineData("input")]
    public async Task WaitingVoiceCannotBorrowReboundOriginalAuthorityBeforeItsFirstUiDispatch(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        var waitingReads = 0;
        f.ViewModel.WorkspaceSurfaceRequested += request =>
        {
            if (request.Target == WorkspaceSurfaceTarget.WaitingQuestions) { waitingReads++; }
            return Task.CompletedTask;
        };
        await f.RaiseActivatedTranscriptAsync("Kora, open all work", 1);
        Task? intervening = null;
        f.Dispatcher.BeforeInvoke = () =>
        {
            if (change is "binding") { f.ViewModel.BindCallOwnershipGate(static () => true); }
            if (change is "name") { intervening = f.RunAsync("set assistant name to Nova"); }
            if (change is "input") { intervening = f.RunAsync("open all work"); }
        };
        await f.Voice.RaiseTranscriptAsync("Kora, " + "show sessions needing an answer", 1);
        if (intervening is not null) { await intervening; }
        waitingReads.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
