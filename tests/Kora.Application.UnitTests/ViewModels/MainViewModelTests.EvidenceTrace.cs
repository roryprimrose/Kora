using System.Diagnostics;
using System.Reflection;

using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private const string PreparedEvidenceTrace = "0123456789abcdef0123456789abcdef";
    private const string EvidencePrepare = "prepare evidence trace " + PreparedEvidenceTrace;

    [Theory]
    [InlineData("evidence trace help", "evidence trace")]
    [InlineData("prepare evidence trace time", "prepare evidence trace")]
    public async Task EvidenceTraceGrammarTakesPrecedenceOverClockShapedAssistantAlias(string command, string alias)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync(alias);

        await f.RunAsync(command);

        f.ViewModel.Transcript.Should().Contain(EvidenceTraceCommand.Syntax);
        f.ViewModel.ResponseTitle.Should().NotBe("Host-local date and time.");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task EvidenceTracePreparationRequiresExactNativeAcknowledgmentAndNeverInterpretsTraceAsAuthority()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("set assistant name to Nova");
        using var hostile = new Activity("incoming").AddBaggage("kora.session.id", PreparedEvidenceTrace).Start();
        var requests = new List<EvidenceTracePreparation>();
        var scopes = new List<HostActivity>();
        var acknowledged = false;
        f.ViewModel.EvidenceTracePreparationRequested += request =>
        {
            requests.Add(request);
            request.IsCurrent.Should().BeTrue();
            request.TraceId.Should().Be(PreparedEvidenceTrace);
            request.ToString().Should().Be(nameof(EvidenceTracePreparation));
            var scope = HostActivity.RequireCurrent();
            scope.Activity!.TraceId.ToHexString().Should().NotBe(PreparedEvidenceTrace);
            scope.Activity.ParentId.Should().BeNull();
            scope.Activity.Baggage.Should().BeEmpty();
            scope.Activity.TagObjects.Should().NotContain(pair => Equals(pair.Value, PreparedEvidenceTrace));
            scopes.Add(scope);
            return Task.FromResult(acknowledged);
        };
        await f.RunAsync(EvidencePrepare);
        f.ViewModel.Transcript.Should().Contain("did not confirm");
        acknowledged = true;
        await f.RunAsync(EvidencePrepare);
        f.ViewModel.Transcript.Should().Contain("filter prepared").And.Contain("AND-composed")
            .And.NotContain(PreparedEvidenceTrace);
        await f.RaiseActivatedTranscriptAsync("Nova, " + EvidencePrepare, 1);
        requests.Should().HaveCount(3).And.OnlyContain(request => !request.IsCurrent);
        scopes.Select(scope => scope.Request.Origin).Should().Equal(
            RequestOrigin.LocalUi, RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice);
        scopes.Should().OnlyContain(scope => scope.Outcome == HostOperationOutcome.Completed);
        f.HostStore.Records.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
        Activity.Current.Should().BeSameAs(hostile);
        f.Voice.StartedPhrases.Should().Contain("Nova prepare evidence trace")
            .And.Contain("evidence trace help").And.NotContain("Kora prepare evidence trace");
    }

    [Fact]
    public async Task EvidenceTraceHelpIsConstantAndDoesNotRequireOrCallANativePresenter()
    {
        var f = await Fixture.CreateInitializedAsync();
        var calls = 0;
        f.ViewModel.EvidenceTracePreparationRequested += _ => { calls++; return Task.FromResult(true); };
        await f.RunAsync("evidence trace help");
        f.ViewModel.Transcript.Should().Be(EvidenceTraceCommand.Syntax);
        await f.RaiseActivatedTranscriptAsync("Kora, evidence trace help", 1);
        f.ViewModel.Transcript.Should().Be(EvidenceTraceCommand.Syntax);
        calls.Should().Be(0);
        f.HostStore.Records.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("prepare evidence trace")]
    [InlineData("prepare evidence trace 00000000000000000000000000000000")]
    [InlineData("evidence trace scan")]
    [InlineData("evidence trace help extra")]
    public async Task ReservedEvidenceTraceNeverAnswersPendingQuestionsOrStartsModelWork(string command)
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        f.Reasoner.Question = new("Which target?", ["One", "Two"]);
        await f.ViewModel.InitializeAsync();
        await f.RaiseActivatedTranscriptAsync("Kora, evidence trace help", 1);
        await f.RunAsync("choose");
        await f.ViewModel.ActiveReasoningTask!;
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        var count = f.Reasoner.Requests.Count;
        f.ViewModel.CommandText = "unfinished answer";
        await f.RaiseActivatedTranscriptAsync("Kora, " + command, 1);
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        f.ViewModel.CommandText.Should().Be("unfinished answer");
        f.ViewModel.Transcript.Should().Contain("command not accepted");
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.Reasoner.Requests.Should().HaveCount(count);
        await f.ViewModel.CancelModelQuestionAsync();
        await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
        title = f.ViewModel.ResponseTitle;
        body = f.ViewModel.ResponseBody;
        await f.RunAsync(command);
        f.ViewModel.IsGrantChangePending.Should().BeTrue();
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("edit-back")]
    [InlineData("owner")]
    [InlineData("binding")]
    [InlineData("call")]
    [InlineData("name")]
    [InlineData("privacy")]
    [InlineData("dispose")]
    [InlineData("presenter")]
    public async Task EvidenceTraceQueuedPreparationCannotAcquireNewAdmission(string boundary)
    {
        var f = await Fixture.CreateInitializedAsync();
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        var reached = new TaskCompletionSource<EvidenceTracePreparation>();
        var complete = new TaskCompletionSource<bool>();
        Func<EvidenceTracePreparation, Task<bool>> handler = request =>
        {
            reached.SetResult(request);
            return complete.Task;
        };
        f.ViewModel.EvidenceTracePreparationRequested += handler;
        var preparing = f.RunAsync(EvidencePrepare);
        var request = await reached.Task;
        request.IsCurrent.Should().BeTrue();
        switch (boundary)
        {
            case "input": await f.RunAsync("evidence trace help"); break;
            case "edit-back": f.ViewModel.CommandText = "changed"; f.ViewModel.CommandText = EvidencePrepare; break;
            case "owner": owner++; break;
            case "binding": f.ViewModel.BindCallOwnershipGate(static () => true, () => owner); break;
            case "call": f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; break;
            case "name": await f.RunAsync("set assistant name to Nova"); break;
            case "privacy": f.Session.IsUnlocked = false; break;
            case "dispose": f.ViewModel.Dispose(); break;
            case "presenter": f.ViewModel.EvidenceTracePreparationRequested -= handler; break;
        }
        request.IsCurrent.Should().BeFalse();
        complete.SetResult(true);
        await preparing;
        f.ViewModel.Transcript.Should().NotContain("filter prepared");
        f.HostStore.Records.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task EvidenceTraceRefusesUnboundProtectedCallAndUnactivatedVoiceButRemainsAvailableWhileBusy()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync(EvidencePrepare);
        f.ViewModel.Transcript.Should().Contain("native Evidence binding");
        var calls = 0;
        f.ViewModel.EvidenceTracePreparationRequested += _ => { calls++; return Task.FromResult(true); };
        await f.RaiseActivatedTranscriptAsync(EvidencePrepare, 1);
        f.ViewModel.Transcript.Should().Contain("current available assistant-name");
        f.CallState.SetState(CallState.Active);
        await f.Dispatcher.LastInvocation;
        await f.RunAsync(EvidencePrepare);
        calls.Should().Be(0);
        f.CallState.SetState(CallState.Clear);
        await f.Dispatcher.LastInvocation;
        f.Probe.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = f.ViewModel.DetectMicrophonesAsync();
        f.ViewModel.CommandText = EvidencePrepare;
        f.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue();
        await f.RunAsync(EvidencePrepare);
        calls.Should().Be(1);
        f.Probe.Gate.SetResult();
        await busy;
    }

    [Fact]
    public void EvidenceTraceHandoffRejectsInvalidTraceBeforeAnyNativeWork()
    {
        var create = () => new EvidenceTracePreparation(new string('0', 32), () => true);
        create.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvidenceTracePreparationAndHelpPreserveExactPendingQuestionOrActionApproval(bool approval)
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        if (approval) { f.Reasoner.Action = BuiltInAction.LockMachine; }
        else { f.Reasoner.Question = new("Which target?", ["One", "Two"]); }
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("choose");
        await f.ViewModel.ActiveReasoningTask!;
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        var audit = f.Audit.Events.Count;
        f.ViewModel.EvidenceTracePreparationRequested += _ => Task.FromResult(true);
        await f.RunAsync(EvidencePrepare);
        await f.RunAsync("evidence trace help");
        f.ViewModel.ReportEvidencePresentationUnavailable();
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.IsModelActionApprovalPending.Should().Be(approval);
        f.ViewModel.IsModelQuestionPending.Should().Be(!approval);
        f.Audit.Events.Should().HaveCount(audit);
        f.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("private")]
    [InlineData("owner")]
    public async Task EvidenceTraceOriginalDenialIsNeverUpgraded(string boundary)
    {
        var f = new Fixture();
        if (string.Equals(boundary, "name", StringComparison.Ordinal)) { f.NamePreferences.Name = "\0"; }
        await f.ViewModel.InitializeAsync();
        if (string.Equals(boundary, "private", StringComparison.Ordinal)) { f.ViewModel.CloseForObservedPrivacyEvent("fixture", true); }
        if (string.Equals(boundary, "owner", StringComparison.Ordinal)) { f.ViewModel.BindCallOwnershipGate(static () => false); }
        var calls = 0;
        f.ViewModel.EvidenceTracePreparationRequested += _ => { calls++; return Task.FromResult(true); };
        await f.RunAsync(EvidencePrepare);
        f.ViewModel.Transcript.Should().Contain("unavailable");
        calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("cancel")]
    [InlineData("io")]
    [InlineData("denied")]
    public async Task EvidenceTraceNativeFailuresRemainExplicitAndRetireTheHandoff(string failure)
    {
        var f = await Fixture.CreateInitializedAsync();
        EvidenceTracePreparation? captured = null;
        f.ViewModel.EvidenceTracePreparationRequested += request =>
        {
            captured = request;
            throw failure switch
            {
                "invalid" => new InvalidOperationException(),
                "cancel" => new OperationCanceledException(),
                "io" => new IOException(),
                _ => new UnauthorizedAccessException(),
            };
        };
        await f.RunAsync(EvidencePrepare);
        f.ViewModel.Transcript.Should().Contain("failed or was cancelled");
        captured!.IsCurrent.Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task EvidenceTraceRejectsNonOriginalHostOriginWithoutAcquiringNativeAuthority()
    {
        var f = await Fixture.CreateInitializedAsync();
        var calls = 0;
        f.ViewModel.EvidenceTracePreparationRequested += _ => { calls++; return Task.FromResult(true); };
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Request);
        await f.RunAsync(EvidencePrepare);
        calls.Should().Be(0);
        f.ViewModel.Transcript.Should().Contain("unavailable");
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.ModelSuggestion)]
    [InlineData(SecurityAuditInitiator.LocalUser)]
    [InlineData((SecurityAuditInitiator)999)]
    public async Task EvidenceTraceRequiresOriginalCommandInitiators(SecurityAuditInitiator initiator)
    {
        var f = await Fixture.CreateInitializedAsync();
        var calls = 0;
        f.ViewModel.EvidenceTracePreparationRequested += _ => { calls++; return Task.FromResult(true); };
        var routing = typeof(Kora.Application.ViewModels.MainViewModel).GetMethod(
            "HandleTranscriptAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)routing.Invoke(f.ViewModel, [EvidencePrepare, 1f, initiator, null, null, null])!;
        calls.Should().Be(0);
        f.ViewModel.Transcript.Should().Contain("unavailable");
        f.HostStore.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvidenceTraceFencesVoiceGenerationBeforeAndDuringNativePreparation(bool beforeDispatch)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RaiseActivatedTranscriptAsync("Kora, evidence trace help", 1);
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        var calls = 0;
        f.ViewModel.EvidenceTracePreparationRequested += request =>
        {
            calls++;
            request.IsCurrent.Should().BeTrue();
            f.Voice.AdvanceGeneration();
            request.IsCurrent.Should().BeFalse();
            return Task.FromResult(true);
        };
        if (beforeDispatch) { f.Dispatcher.BeforeInvoke = f.Voice.AdvanceGeneration; }
        await f.Voice.RaiseTranscriptAsync("Kora, " + EvidencePrepare, 1);
        calls.Should().Be(beforeDispatch ? 0 : 1);
        f.ViewModel.Transcript.Should().StartWith("Evidence trace unavailable:");
        f.HostStore.Records.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
