using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Authorization;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private const string ListPreparationId = "abcdef01-2345-6789-abcd-ef0123456789";

    [Theory]
    [InlineData("prepare session list time", "prepare session list")]
    [InlineData("session list help", "session list")]
    public async Task SessionListPreparationRetainsPrecedenceOverClockShapedAssistantAlias(string command, string alias)
    {
        var f = new Fixture();
        await f.ViewModel.SetAssistantNameAsync(alias);

        await f.RunAsync(command);

        f.ViewModel.ResponseBody.Should().Be(SessionListPreparationCommand.Syntax);
        f.ViewModel.ResponseTitle.Should().NotBe("Host-local date and time.");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactListPreparationIsScopedQueryDataAndNeverExecutesWorkspaceOrModelWork(bool voice)
    {
        var logger = new MemoryPrivacyLogger();
        var f = new Fixture(logger: logger);
        if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, prepare", 1); }
        SessionListPreparation? retained = null;
        var calls = 0;
        f.ViewModel.SessionListPreparationRequested += request =>
        {
            calls++;
            retained = request;
            request.SessionId.Value.Should().Be(Guid.Parse(ListPreparationId));
            request.IsAdmitted().Should().BeTrue();
            HostActivity.RequireCurrent().Request.Origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            return Task.FromResult(true);
        };
        var text = "prepare session list " + ListPreparationId;
        if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, " + text, 1); }
        else { await f.RunAsync(text); }
        calls.Should().Be(1);
        retained!.IsAdmitted().Should().BeFalse();
        f.ViewModel.ResponseTitle.Should().Contain("PREPARED");
        f.ViewModel.ResponseBody.Should().Contain("AND").And.Contain("No Search has run").And.NotContain(ListPreparationId);
        f.ViewModel.Transcript.Should().NotContain(ListPreparationId);
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        f.ClipboardReader.Calls.Should().Be(0);
        logger.Messages.Should().NotContain(message => message.Contains(ListPreparationId, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("prepare session list current")]
    [InlineData("prepare session list")]
    [InlineData("session list help")]
    [InlineData("session list help tail")]
    public async Task ReservedListPreparationAndHelpDoNotInvokeAnyNativeReaderOrModel(string input)
    {
        var f = new Fixture();
        f.ViewModel.SessionListPreparationRequested += _ => throw new InvalidOperationException("Must not open");
        await f.RunAsync(input);
        f.ViewModel.ResponseBody.Should().Be(SessionListPreparationCommand.Syntax);
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData("unbound")]
    [InlineData("unacknowledged")]
    [InlineData("exception")]
    [InlineData("cancel")]
    [InlineData("system")]
    [InlineData("voice-unprefixed")]
    [InlineData("voice-owner")]
    [InlineData("lock")]
    [InlineData("protected")]
    public async Task UnknownOrRevokedOriginalAdmissionCannotPretendToPrepare(string state)
    {
        var f = new Fixture();
        if (state.StartsWith("voice", StringComparison.Ordinal))
        { await f.RaiseActivatedTranscriptAsync("Kora, prepare", 1); }
        if (state is "voice-owner") { f.ViewModel.BindVoiceOwnershipGate(() => false); }
        if (state is "lock") { f.Session.IsUnlocked = false; }
        if (state is "protected")
        { await f.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi, f.ViewModel.CallPolicyRevision, TestContext.Current.CancellationToken); }
        if (state is not "unbound")
        {
            f.ViewModel.SessionListPreparationRequested += _ => state switch
            {
                "exception" => throw new InvalidOperationException("Native unavailable"),
                "cancel" => throw new OperationCanceledException("Native cancelled"),
                _ => Task.FromResult(false),
            };
        }
        var input = "prepare session list " + ListPreparationId;
        if (state is "system")
        {
            using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request);
            await f.RunAsync(input);
        }
        else if (state.StartsWith("voice", StringComparison.Ordinal))
        { await f.RaiseActivatedTranscriptAsync((state is "voice-owner" ? "Kora, " : string.Empty) + input, 1); }
        else { await f.RunAsync(input); }
        f.ViewModel.ResponseTitle.Should().NotContain("PREPARED");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("owner")]
    [InlineData("owner-binding")]
    [InlineData("name")]
    [InlineData("name-cycle")]
    [InlineData("input")]
    [InlineData("text")]
    [InlineData("lock")]
    [InlineData("call")]
    [InlineData("dispose")]
    [InlineData("cancel")]
    public async Task NativeAcknowledgementMustStillBelongToTheOriginalInput(string change)
    {
        var f = new Fixture();
        SessionListPreparation? retained = null;
        f.ViewModel.SessionListPreparationRequested += async request =>
        {
            retained = request;
            switch (change)
            {
                case "privacy": f.PrivacyObservation.Current = f.PrivacyObservation.Current with
                    { TopologyRevision = f.PrivacyObservation.Current.TopologyRevision + 1 }; break;
                case "owner": f.ViewModel.BindCallOwnershipGate(() => true, () => 99); break;
                case "owner-binding": f.ViewModel.BindCallOwnershipGate(() => true); break;
                case "name": await f.ViewModel.SetAssistantNameAsync("Nova"); break;
                case "name-cycle": await f.ViewModel.SetAssistantNameAsync("Nova"); await f.ViewModel.SetAssistantNameAsync("Kora"); break;
                case "input": await f.RunAsync("session list help"); break;
                case "text": f.ViewModel.CommandText = "new input"; break;
                case "lock": f.Session.IsUnlocked = false; break;
                case "call": await f.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi,
                    f.ViewModel.CallPolicyRevision, TestContext.Current.CancellationToken); break;
                case "cancel": await f.ViewModel.CancelCurrentTaskAsync(); break;
                default: f.ViewModel.Dispose(); break;
            }
            request.IsAdmitted().Should().BeFalse();
            return true;
        };
        await f.RunAsync("prepare session list " + ListPreparationId);
        retained!.IsAdmitted().Should().BeFalse();
        f.ViewModel.ResponseTitle.Should().NotContain("PREPARED");
    }

    [Theory]
    [InlineData("question")]
    [InlineData("approval")]
    [InlineData("grant")]
    [InlineData("editor")]
    [InlineData("actions")]
    public async Task ListPreparationAndMalformedHelpPreservePendingQuestionsApprovalsAndSecurityPresentation(string state)
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        if (state is "question") { f.Reasoner.Question = new("Which target?", ["One", "Two"]); }
        if (state is "approval") { f.Reasoner.Action = BuiltInAction.LockMachine; }
        await f.ViewModel.InitializeAsync();
        if (state is "question" or "approval")
        {
            await f.RunAsync("help with this machine");
            await f.ViewModel.ActiveReasoningTask!;
        }
        if (state is "grant")
        { await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Session)); }
        if (state is "editor") { SetPresentationProperty(f.ViewModel, nameof(MainViewModel.IsGrantEditorVisible), true); }
        if (state is "actions")
        {
            SetPresentationProperty<IReadOnlyList<ResponseAction>>(f.ViewModel, nameof(MainViewModel.ResponseActions),
                [new(ResponseActionKind.OpenVoiceSettings, "Existing native review")]);
        }
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        var actions = f.ViewModel.ResponseActions;
        var calls = f.Reasoner.Requests.Count;
        f.ViewModel.SessionListPreparationRequested += _ => throw new InvalidOperationException("No preparation during pending review");
        await f.RunAsync("prepare session list " + ListPreparationId);
        await f.RunAsync("session list help extra");
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.ResponseActions.Should().BeSameAs(actions);
        f.ViewModel.IsModelQuestionPending.Should().Be(state is "question");
        f.ViewModel.IsModelActionApprovalPending.Should().Be(state is "approval");
        f.ViewModel.IsGrantChangePending.Should().Be(state is "grant");
        f.ViewModel.Transcript.Should().Contain("finish the existing");
        f.Reasoner.Requests.Should().HaveCount(calls);
        f.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExactListPreparationWhileBusyRemainsPresentationOnlyAndUsesCurrentNameGrammar()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova");
        f.Probe.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = f.ViewModel.DetectMicrophonesAsync();
        var calls = 0;
        f.ViewModel.SessionListPreparationRequested += _ => { calls++; return Task.FromResult(true); };
        f.ViewModel.CommandText = "prepare session list " + ListPreparationId;
        f.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue();
        await f.ViewModel.RunTypedCommand.ExecuteAsync();
        calls.Should().Be(1);
        f.ViewModel.IsBusy.Should().BeTrue();
        f.Probe.Gate.SetResult();
        await busy;
        await f.RaiseActivatedTranscriptAsync("Nova, session list help", 1);
        f.Voice.StartedPhrases.Should().Contain("Nova prepare session list").And.Contain("Nova session list help")
            .And.NotContain("Kora prepare session list");
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }
}
