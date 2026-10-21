using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private const string DiagnosticsInput = "session diagnostics plan 12345678-1234-1234-abcd-123456789012 version,runtimes";
    private static SessionDiagnosticsPlanCommand DiagnosticsCommand => SessionDiagnosticsPlanCommand.Parse(DiagnosticsInput, "Kora")!;

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task ExactDiagnosticsPreparationUsesOnlyBoundPresenterWithoutModelSpeechOrEnqueue(bool voice, bool displayed)
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new DiagnosticsPresenter { Displayed = displayed };
        f.ViewModel.BindSessionDiagnosticsPlan(presenter);
        if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, " + DiagnosticsInput, 1); }
        else { await f.RunAsync(DiagnosticsInput); }
        var command = presenter.Commands.Should().ContainSingle().Subject;
        command.SessionId.Should().Be(DiagnosticsCommand.SessionId);
        command.Profiles.Should().Equal(SessionQueueReadProfile.Version, SessionQueueReadProfile.Runtimes);
        presenter.Origins.Should().ContainSingle().Which.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        f.ViewModel.ResponseTitle.Should().Be(displayed ? "Session diagnostics plan prepared." : "Session diagnostics plan not opened.");
        f.ViewModel.ResponseBody.Should().Contain("separate native confirmation").And.NotContain(command.SessionId.ToString("D"));
        f.HostStore.Records.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
        f.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("help")]
    [InlineData("unbound")]
    [InlineData("unprefixed")]
    [InlineData("protected")]
    public async Task DiagnosticsReservedInputNeverFallsBackOrInfersATarget(string mode)
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new DiagnosticsPresenter();
        if (mode is not "unbound") { f.ViewModel.BindSessionDiagnosticsPlan(presenter); }
        if (mode is "protected") { f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; }
        var input = mode switch
        {
            "invalid" => "session diagnostics plan selected version",
            "help" => "session diagnostics plan help",
            _ => DiagnosticsInput,
        };
        await f.RaiseActivatedTranscriptAsync((mode is "unprefixed" ? "" : "Kora, ") + input, 1);
        presenter.Commands.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        f.ViewModel.ResponseTitle.Should().Contain(mode switch
        {
            "invalid" => "not accepted",
            "help" => "help",
            "unbound" => "unavailable",
            _ => "denied",
        });
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.LocalUser)]
    [InlineData(SecurityAuditInitiator.ModelSuggestion)]
    [InlineData(SecurityAuditInitiator.VoiceCommand)]
    public async Task DiagnosticsDirectEntryRequiresOriginalEnabledChannel(SecurityAuditInitiator initiator)
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new DiagnosticsPresenter();
        f.ViewModel.BindSessionDiagnosticsPlan(presenter);
        await f.ViewModel.ExecuteDiagnosticsPlanAsync(DiagnosticsCommand, initiator, true);
        presenter.Commands.Should().HaveCount(initiator == SecurityAuditInitiator.LocalUser ? 1 : 0);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("invalid")]
    [InlineData("access")]
    [InlineData("cancel")]
    [InlineData("argument")]
    [InlineData("unexpected")]
    public async Task DiagnosticsFailuresAreExplicitAndNeverDisclosePrivateErrorText(string kind)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.BindSessionDiagnosticsPlan(new DiagnosticsPresenter
        {
            Failure = kind switch
            {
                "io" => new IOException("private queue"),
                "invalid" => new InvalidOperationException("private queue"),
                "access" => new UnauthorizedAccessException("private queue"),
                "cancel" => new OperationCanceledException("private queue"),
                "argument" => new ArgumentException("private queue"),
                _ => new NotSupportedException("programming error"),
            },
        });
        var run = () => f.RunAsync(DiagnosticsInput);
        if (kind is "unexpected") { await run.Should().ThrowAsync<NotSupportedException>(); }
        else
        {
            await run();
            f.ViewModel.ResponseTitle.Should().Be("Session diagnostics plan unavailable.");
            f.ViewModel.ResponseBody.Should().NotContain("private queue");
        }
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("dispose")]
    [InlineData("locked")]
    [InlineData("rebind")]
    [InlineData("call")]
    [InlineData("input")]
    [InlineData("surface-input")]
    [InlineData("question")]
    [InlineData("grant")]
    [InlineData("approval")]
    [InlineData("failure")]
    public async Task LateDiagnosticsPreparationCannotSurviveOriginalInputAdmissionOrPendingDecisionChanges(string mode)
    {
        var f = await CreateDispositionReadyFixture();
        var presenter = new DiagnosticsPresenter();
        f.ViewModel.BindSessionDiagnosticsPlan(presenter);
        presenter.BeforeReturn = async () =>
        {
            switch (mode)
            {
                case "dispose": f.ViewModel.Dispose(); break;
                case "locked": f.Session.IsUnlocked = false; break;
                case "rebind": f.ViewModel.BindSessionDiagnosticsPlan(null); break;
                case "call": f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; break;
                case "input": await f.RunAsync("help"); break;
                case "surface-input": await f.RunAsync("open sessions"); break;
                case "failure":
                    f.ViewModel.BindSessionDiagnosticsPlan(null);
                    throw new IOException("private details");
                case "grant":
                    await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
                    break;
                default:
                    f.Reasoner.Question = mode is "question" ? new("Which?", ["One", "Two"]) : null;
                    f.Reasoner.Action = mode is "approval" ? BuiltInAction.LockMachine : null;
                    await f.RunAsync("please choose");
                    await f.ViewModel.ActiveReasoningTask!;
                    break;
            }
        };
        await f.ViewModel.ExecuteDiagnosticsPlanAsync(DiagnosticsCommand, SecurityAuditInitiator.TypedCommand, false);
        presenter.LastAdmission.Should().BeFalse();
        f.ViewModel.ResponseTitle.Should().NotBe("Session diagnostics plan prepared.");
        if (mode is "question") { f.ViewModel.IsModelQuestionPending.Should().BeTrue(); }
        if (mode is "approval") { f.ViewModel.IsModelActionApprovalPending.Should().BeTrue(); }
        if (mode is "grant") { f.ViewModel.IsGrantChangePending.Should().BeTrue(); }
    }

    [Theory]
    [InlineData("name")]
    [InlineData("consent")]
    public async Task OriginalActivatedDiagnosticsCannotSurviveNameOrConsentChange(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RaiseActivatedTranscriptAsync("Kora, session help", 1);
        var presenter = new DiagnosticsPresenter
        {
            BeforeReturn = async () =>
            {
                if (change is "name")
                {
                    f.ViewModel.AssistantNameInput = "Nova";
                    await f.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
                }
                else { await f.ViewModel.SetVoiceConsentAsync(false); }
            },
        };
        f.ViewModel.BindSessionDiagnosticsPlan(presenter);
        await f.ViewModel.ExecuteDiagnosticsPlanAsync(DiagnosticsCommand, SecurityAuditInitiator.VoiceCommand, true);
        presenter.Commands.Should().ContainSingle();
        presenter.LastAdmission.Should().BeFalse();
        f.ViewModel.ResponseTitle.Should().NotBe("Session diagnostics plan prepared.");
    }

    [Theory]
    [InlineData("question")]
    [InlineData("grant")]
    [InlineData("approval")]
    public async Task ExistingQuestionOrApprovalIsNeverConsumedAsDiagnosticsInput(string mode)
    {
        var f = await CreateDispositionReadyFixture();
        var presenter = new DiagnosticsPresenter();
        f.ViewModel.BindSessionDiagnosticsPlan(presenter);
        if (mode is "grant")
        { await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always)); }
        else
        {
            f.Reasoner.Question = mode is "question" ? new("Which?", ["One", "Two"]) : null;
            f.Reasoner.Action = mode is "approval" ? BuiltInAction.LockMachine : null;
            await f.RunAsync("choose");
            await f.ViewModel.ActiveReasoningTask!;
        }
        await f.RunAsync(DiagnosticsInput);
        f.ViewModel.ResponseTitle.Should().Be("Session diagnostics plan blocked.");
        presenter.Commands.Should().BeEmpty();
        if (mode is "question") { f.ViewModel.IsModelQuestionPending.Should().BeTrue(); }
        if (mode is "approval") { f.ViewModel.IsModelActionApprovalPending.Should().BeTrue(); }
        if (mode is "grant") { f.ViewModel.IsGrantChangePending.Should().BeTrue(); }
    }

    [Fact]
    public async Task DisposedDiagnosticsDoesNotOpenAndGeneralHelpDisclosesBoundary()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("help");
        f.ViewModel.ResponseBody.Should().Contain(SessionDiagnosticsPlanCommand.Syntax);
        var presenter = new DiagnosticsPresenter();
        f.ViewModel.BindSessionDiagnosticsPlan(presenter);
        f.ViewModel.Dispose();
        await f.ViewModel.ExecuteDiagnosticsPlanAsync(DiagnosticsCommand, SecurityAuditInitiator.TypedCommand, false);
        presenter.Commands.Should().BeEmpty();
    }

    private sealed class DiagnosticsPresenter : ISessionDiagnosticsPlanPresenter
    {
        internal List<SessionDiagnosticsPlanCommand> Commands { get; } = [];
        internal List<RequestOrigin> Origins { get; } = [];
        internal bool Displayed { get; init; } = true;
        internal bool LastAdmission { get; private set; }
        internal Exception? Failure { get; init; }
        internal Func<Task>? BeforeReturn { get; set; }
        public async Task<bool> PrepareDiagnosticsPlanAsync(SessionDiagnosticsPlanCommand command, RequestOrigin origin,
            Func<bool> admitted, CancellationToken token)
        {
            Commands.Add(command);
            Origins.Add(origin);
            if (Failure is not null) { throw Failure; }
            if (BeforeReturn is not null) { await BeforeReturn(); }
            LastAdmission = admitted();
            return Displayed;
        }
    }
}
