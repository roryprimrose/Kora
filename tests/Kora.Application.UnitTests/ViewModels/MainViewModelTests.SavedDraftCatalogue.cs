using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private const string CataloguePrepare = "prepare saved draft catalogue abcdef12-3456-7890-abcd-ef1234567890";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedDraftPreparationRetainsOriginalRouteWithClockShapedAssistantName(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync("What Time");
        var calls = 0;
        f.ViewModel.SavedDraftCataloguePreparationRequested += request =>
        {
            request.IsAdmitted().Should().BeTrue();
            calls++;
            return Task.FromResult(true);
        };

        if (voice) { await f.RaiseActivatedTranscriptAsync("What Time, " + CataloguePrepare, 1); }
        else { await f.RunAsync("What Time " + CataloguePrepare); }

        calls.Should().Be(1);
        f.ViewModel.Transcript.Should().Contain("no I/O yet");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CataloguePreparationAndHelpNeverReadOrDispatchAndRequireNativeAcknowledgement()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync(CataloguePrepare);
        f.ViewModel.Transcript.Should().Contain("not bound");
        var calls = new List<SavedDraftCataloguePreparation>();
        HostActivity? root = null;
        var acknowledge = false;
        f.ViewModel.SavedDraftCataloguePreparationRequested += request =>
        {
            root = HostActivity.RequireCurrent();
            request.IsAdmitted().Should().BeTrue();
            request.SessionId.Should().Be(Guid.Parse("abcdef12-3456-7890-abcd-ef1234567890"));
            calls.Add(request);
            return Task.FromResult(acknowledge);
        };
        await f.RunAsync(CataloguePrepare);
        f.ViewModel.Transcript.Should().Contain("not acknowledged");
        acknowledge = true;
        await f.RunAsync(CataloguePrepare);
        f.ViewModel.Transcript.Should().Contain("no I/O yet").And.Contain("AND-composed");
        root!.Outcome.Should().Be(HostOperationOutcome.Completed);
        root.Request.Origin.Should().Be(RequestOrigin.LocalUi);
        await f.RunAsync("saved draft catalogue help");
        f.ViewModel.Transcript.Should().Be(SavedDraftCatalogueCommand.Help);
        await f.RunAsync("saved draft catalogue help body");
        f.ViewModel.Transcript.Should().Contain("not accepted");
        await f.RunAsync("prepare saved draft catalogue current");
        f.ViewModel.Transcript.Should().Contain("not accepted");
        calls.Should().HaveCount(2);
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task CataloguePreparationUsesCurrentNameVoiceAndDoesNotAnswerPendingQuestion()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        f.Reasoner.Question = new("Which target?", ["One", "Two"]);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("set assistant name to Nova");
        var calls = 0;
        f.ViewModel.SavedDraftCataloguePreparationRequested += request =>
        {
            calls++;
            request.IsAdmitted().Should().BeTrue();
            HostActivity.Current!.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
            return Task.FromResult(true);
        };
        await f.RunAsync("choose");
        await f.ViewModel.ActiveReasoningTask!;
        f.ViewModel.CommandText = "unfinished answer";
        await f.RaiseActivatedTranscriptAsync("Nova, " + CataloguePrepare, 1);
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        f.ViewModel.CommandText.Should().Be("unfinished answer");
        await f.RaiseActivatedTranscriptAsync("Nova, prepare saved draft catalogue latest", 1);
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        await f.RaiseActivatedTranscriptAsync(CataloguePrepare, 1);
        f.ViewModel.Transcript.Should().Contain("current available assistant-name");
        calls.Should().Be(1);
        f.Voice.StartedPhrases.Should().Contain("Nova " + SavedDraftCatalogueCommand.PreparePhrase)
            .And.Contain(SavedDraftCatalogueCommand.HelpPhrase);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("edit-aba")]
    [InlineData("owner")]
    [InlineData("binding")]
    [InlineData("call")]
    [InlineData("name-aba")]
    [InlineData("privacy")]
    [InlineData("dispose")]
    [InlineData("unbind")]
    public async Task CataloguePreparationOriginalAdmissionSurvivesUntilNativeAcknowledgement(string boundary)
    {
        var f = await Fixture.CreateInitializedAsync();
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        SavedDraftCataloguePreparation? captured = null;
        f.ViewModel.SavedDraftCataloguePreparationRequested += async request =>
        {
            captured = request;
            request.IsAdmitted().Should().BeTrue();
            switch (boundary)
            {
                case "input": await f.RunAsync("saved draft catalogue help"); break;
                case "edit-aba":
                    f.ViewModel.CommandText = "different";
                    f.ViewModel.CommandText = CataloguePrepare;
                    break;
                case "owner": owner++; break;
                case "binding": f.ViewModel.BindCallOwnershipGate(static () => true, () => owner); break;
                case "call": f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; break;
                case "name-aba":
                    await f.RunAsync("set assistant name to Nova");
                    await f.RunAsync("set assistant name to Kora");
                    break;
                case "privacy": f.Session.IsUnlocked = false; break;
                case "dispose": f.ViewModel.Dispose(); break;
                case "unbind": f.ViewModel.SavedDraftCataloguePreparationRequested += _ => Task.FromResult(true); break;
            }
            request.IsAdmitted().Should().BeFalse();
            return true;
        };
        await f.RunAsync(CataloguePrepare);
        captured.Should().NotBeNull();
        f.ViewModel.Transcript.Should().Contain("not acknowledged");
        f.HostStore.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData("private")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("name")]
    public async Task CataloguePreparationRefusesOriginalUnavailableHost(string boundary)
    {
        var f = new Fixture();
        if (string.Equals(boundary, "name", StringComparison.Ordinal)) { f.NamePreferences.Name = "Supported Commands"; }
        await f.ViewModel.InitializeAsync();
        var calls = 0;
        f.ViewModel.SavedDraftCataloguePreparationRequested += _ => { calls++; return Task.FromResult(true); };
        switch (boundary)
        {
            case "private": f.Session.IsUnlocked = false; break;
            case "owner": f.ViewModel.BindCallOwnershipGate(static () => false); break;
            case "call": f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; break;
        }
        await f.RunAsync(CataloguePrepare);
        calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.Transcript.Should().Contain("unavailable");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CataloguePreparationReportsNativeFailureAndIsAvailableWhileBusy(bool cancel)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.Probe.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = f.ViewModel.DetectMicrophonesAsync();
        f.ViewModel.SavedDraftCataloguePreparationRequested += _ => cancel
            ? throw new OperationCanceledException("Native lifetime retired.")
            : throw new InvalidOperationException("Native focus failed.");
        f.ViewModel.CommandText = CataloguePrepare;
        f.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue();
        await f.RunAsync(CataloguePrepare);
        f.ViewModel.Transcript.Should().Contain("native preparation failed");
        f.ViewModel.IsBusy.Should().BeTrue();
        f.Probe.Gate.SetResult();
        await busy;
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.ModelSuggestion)]
    [InlineData(SecurityAuditInitiator.LocalUser)]
    [InlineData((SecurityAuditInitiator)999)]
    public async Task SavedDraftCataloguePreparationRejectsNonCommandInitiators(SecurityAuditInitiator initiator)
    {
        var f = await Fixture.CreateInitializedAsync();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var routing = typeof(Kora.Application.ViewModels.MainViewModel).GetMethod(
            "HandleTranscriptAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (Task)routing.Invoke(f.ViewModel, [CataloguePrepare, 1f, initiator, null, null, null])!;
        f.ViewModel.Transcript.Should().Contain("original typed input");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    public async Task CataloguePreparationRejectsExternalOrigins(RequestOrigin origin)
    {
        var f = await Fixture.CreateInitializedAsync();
        await HostRequestRunner.RunAsync(origin, () => f.RunAsync(CataloguePrepare));
        f.ViewModel.Transcript.Should().Contain("original typed input");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CataloguePreparationRetainsPendingGrantAndApprovalResponseEvenOnMalformedOrUnavailableRoutes()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        f.Reasoner.Action = BuiltInAction.LockMachine;
        await f.ViewModel.InitializeAsync();
        f.ViewModel.SavedDraftCataloguePreparationRequested += _ => Task.FromResult(true);
        await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
        var body = f.ViewModel.ResponseBody;
        var title = f.ViewModel.ResponseTitle;
        await f.RunAsync(CataloguePrepare);
        await f.RunAsync("saved draft catalogue help");
        await f.RunAsync("prepare saved draft catalogue latest");
        f.ViewModel.IsGrantChangePending.Should().BeTrue();
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        await f.ViewModel.RejectPendingGrantChangeAsync();
        await f.RunAsync("please lock");
        await f.ViewModel.ActiveReasoningTask!;
        body = f.ViewModel.ResponseBody;
        title = f.ViewModel.ResponseTitle;
        await f.RunAsync(CataloguePrepare);
        await f.RunAsync("saved draft catalogue help extra");
        f.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("name")]
    public async Task CataloguePreparationRejectsVoiceAdmissionRetiredBeforeNativeAcknowledgement(string boundary)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.SavedDraftCataloguePreparationRequested += async request =>
        {
            request.IsAdmitted().Should().BeTrue();
            switch (boundary)
            {
                case "generation": f.Voice.AdvanceGeneration(); break;
                case "privacy": f.ViewModel.CloseForObservedPrivacyEvent("fixture", true); break;
                case "call": f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; break;
                case "name": f.ViewModel.AssistantNameInput = "Nova"; await f.ViewModel.ApplyAssistantNameCommand.ExecuteAsync(); break;
            }
            request.IsAdmitted().Should().BeFalse();
            return true;
        };
        await f.RaiseActivatedTranscriptAsync("Kora, " + CataloguePrepare, 1);
        f.ViewModel.Transcript.Should().Contain("not acknowledged");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("argument")]
    [InlineData("unexpected")]
    public async Task CataloguePreparationClassifiesNativeFailuresWithoutSuccessFallback(string failure)
    {
        var f = await Fixture.CreateInitializedAsync();
        Exception exception = failure switch
        {
            "io" => new IOException("native"),
            "access" => new UnauthorizedAccessException("native"),
            "argument" => new ArgumentException("native"),
            _ => new NotSupportedException("unexpected"),
        };
        f.ViewModel.SavedDraftCataloguePreparationRequested += _ => throw exception;
        if (failure is "unexpected")
        {
            var action = () => f.RunAsync(CataloguePrepare);
            await action.Should().ThrowAsync<NotSupportedException>();
        }
        else
        {
            await f.RunAsync(CataloguePrepare);
            f.ViewModel.Transcript.Should().Contain("native preparation failed");
        }
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
