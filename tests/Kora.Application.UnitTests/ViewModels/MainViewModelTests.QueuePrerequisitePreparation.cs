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
    private const string PrerequisiteInput = "session queue prerequisite prepare 12345678-1234-1234-abcd-123456789012 none";
    private static readonly SessionQueuePrerequisitePrepareCommand PrerequisiteCommand = new(
        Guid.Parse("12345678-1234-1234-abcd-123456789012"), null);

    [Theory]
    [InlineData(false, true, "Kora")]
    [InlineData(true, true, "Kora")]
    [InlineData(false, false, "Kora")]
    [InlineData(false, true, "What Time")]
    [InlineData(true, true, "What Time")]
    public async Task PrerequisitePreparationUsesOnlyTheNativeConsumer(bool voice, bool displayed, string assistantName)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("set assistant name to " + assistantName);
        var presenter = new PrerequisitePresenter { Displayed = displayed };
        f.ViewModel.BindQueuePrerequisitePreparation(presenter);
        if (voice) { await f.RaiseActivatedTranscriptAsync(assistantName + ", " + PrerequisiteInput, 1); }
        else { await f.RunAsync(string.Equals(assistantName, "What Time", StringComparison.Ordinal)
            ? assistantName + ", " + PrerequisiteInput : PrerequisiteInput); }
        presenter.Commands.Should().ContainSingle().Which.Should().Be(PrerequisiteCommand);
        presenter.Origins.Should().ContainSingle().Which.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        presenter.LastAdmission.Should().BeTrue();
        f.ViewModel.ResponseTitle.Should().Be(displayed ? "Queue prerequisite preparation opened." : "Queue prerequisite preparation not opened.");
        f.ViewModel.ResponseBody.Should().Contain("PREPARATION").And.NotContain(PrerequisiteCommand.SessionId.ToString("D"));
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        f.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData("help")]
    [InlineData("invalid")]
    [InlineData("unbound")]
    [InlineData("protected")]
    [InlineData("unprefixed")]
    public async Task HelpAndDeniedOrMalformedInputNeverReadMetadataOrReachTheModel(string mode)
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new PrerequisitePresenter();
        if (mode is not "unbound") { f.ViewModel.BindQueuePrerequisitePreparation(presenter); }
        if (mode is "protected") { f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; }
        var input = mode switch
        {
            "help" => "session queue prerequisite help",
            "invalid" => "session queue prerequisite prepare latest none",
            _ => PrerequisiteInput,
        };
        await f.RaiseActivatedTranscriptAsync((mode is "unprefixed" ? "" : "Kora, ") + input, 1);
        presenter.Commands.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.ModelSuggestion)]
    [InlineData(SecurityAuditInitiator.LocalUser)]
    [InlineData(SecurityAuditInitiator.VoiceCommand)]
    public async Task PreparationRequiresAnOriginalEnabledChannel(SecurityAuditInitiator initiator)
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new PrerequisitePresenter();
        f.ViewModel.BindQueuePrerequisitePreparation(presenter);
        await f.ViewModel.ExecuteQueuePrerequisitePreparationAsync(PrerequisiteCommand, initiator, true);
        presenter.Commands.Should().HaveCount(initiator == SecurityAuditInitiator.LocalUser ? 1 : 0);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("cancel")]
    [InlineData("argument")]
    [InlineData("invalid")]
    public async Task PresenterErrorsAreExplicitWithoutPrivateDetails(string failure)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.BindQueuePrerequisitePreparation(new PrerequisitePresenter
        {
            Failure = failure switch
            {
                "io" => new IOException("private source"),
                "access" => new UnauthorizedAccessException("private source"),
                "cancel" => new OperationCanceledException("private source"),
                "argument" => new ArgumentException("private source"),
                _ => new InvalidOperationException("private source"),
            },
        });
        await f.RunAsync(PrerequisiteInput);
        f.ViewModel.ResponseTitle.Should().Be("Queue prerequisite preparation unavailable.");
        f.ViewModel.ResponseBody.Should().NotContain("private source");
    }

    [Theory]
    [InlineData("dispose")]
    [InlineData("rebind")]
    [InlineData("input")]
    [InlineData("new-input")]
    [InlineData("call")]
    [InlineData("lock")]
    [InlineData("name")]
    [InlineData("consent")]
    [InlineData("approval")]
    [InlineData("question")]
    [InlineData("grant")]
    public async Task OriginalAdmissionIsRetiredAcrossDeferredNativeWork(string change)
    {
        var f = await CreateDispositionReadyFixture();
        await f.RaiseActivatedTranscriptAsync("Kora, session help", 1);
        var presenter = new PrerequisitePresenter
        {
            BeforeReturn = async () =>
            {
                switch (change)
                {
                    case "dispose": f.ViewModel.Dispose(); break;
                    case "rebind": f.ViewModel.BindQueuePrerequisitePreparation(null); break;
                    case "input": f.ViewModel.CommandText = "changed"; break;
                    case "new-input": await f.RunAsync("help"); break;
                    case "call": f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; break;
                    case "lock": f.Session.IsUnlocked = false; break;
                    case "name":
                        f.ViewModel.AssistantNameInput = "Nova";
                        await f.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
                        break;
                    case "consent": await f.ViewModel.SetVoiceConsentAsync(false); break;
                    case "grant":
                        await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
                        break;
                    default:
                        f.Reasoner.Question = change is "question" ? new("Which?", ["One", "Two"]) : null;
                        f.Reasoner.Action = change is "approval" ? BuiltInAction.LockMachine : null;
                        await f.RunAsync("choose"); await f.ViewModel.ActiveReasoningTask!;
                        break;
                }
            },
        };
        f.ViewModel.BindQueuePrerequisitePreparation(presenter);
        await f.ViewModel.ExecuteQueuePrerequisitePreparationAsync(PrerequisiteCommand, SecurityAuditInitiator.VoiceCommand, true);
        presenter.LastAdmission.Should().BeFalse();
        f.ViewModel.ResponseTitle.Should().NotBe("Queue prerequisite preparation opened.");
    }

    [Theory]
    [InlineData("question")]
    [InlineData("grant")]
    [InlineData("approval")]
    public async Task PreparationDoesNotReplaceAnswerOrConsumePendingSecurityPanes(string mode)
    {
        var f = await CreateDispositionReadyFixture();
        var presenter = new PrerequisitePresenter();
        f.ViewModel.BindQueuePrerequisitePreparation(presenter);
        if (mode is "grant")
        { await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always)); }
        else
        {
            f.Reasoner.Question = mode is "question" ? new("Which?", ["One", "Two"]) : null;
            f.Reasoner.Action = mode is "approval" ? BuiltInAction.LockMachine : null;
            await f.RunAsync("choose"); await f.ViewModel.ActiveReasoningTask!;
        }
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync(PrerequisiteInput);
        presenter.Commands.Should().BeEmpty();
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
    }

    [Fact]
    public async Task QueuedDisposedOriginalAndRevokedFailureCannotOpenOrPublish()
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new PrerequisitePresenter();
        f.ViewModel.BindQueuePrerequisitePreparation(presenter);
        f.Dispatcher.BeforeInvoke = () => f.ViewModel.BindQueuePrerequisitePreparation(null);
        await f.ViewModel.ExecuteQueuePrerequisitePreparationAsync(PrerequisiteCommand, SecurityAuditInitiator.TypedCommand, false);
        presenter.Commands.Should().BeEmpty();
        f.Dispatcher.BeforeInvoke = null;
        presenter.BeforeReturn = () =>
        {
            f.ViewModel.BindQueuePrerequisitePreparation(null);
            throw new IOException("private source");
        };
        f.ViewModel.BindQueuePrerequisitePreparation(presenter);
        await f.ViewModel.ExecuteQueuePrerequisitePreparationAsync(PrerequisiteCommand, SecurityAuditInitiator.TypedCommand, false);
        f.ViewModel.ResponseTitle.Should().NotBe("Queue prerequisite preparation unavailable.");
        f.ViewModel.Dispose();
        await f.ViewModel.ExecuteQueuePrerequisitePreparationAsync(PrerequisiteCommand, SecurityAuditInitiator.TypedCommand, false);
    }

    private sealed class PrerequisitePresenter : ISessionQueuePrerequisitePreparePresenter
    {
        internal List<SessionQueuePrerequisitePrepareCommand> Commands { get; } = [];
        internal List<RequestOrigin> Origins { get; } = [];
        internal bool Displayed { get; init; } = true;
        internal bool LastAdmission { get; private set; }
        internal Exception? Failure { get; init; }
        internal Func<Task>? BeforeReturn { get; set; }
        public async Task<bool> PrepareQueuePrerequisiteAsync(SessionQueuePrerequisitePrepareCommand command, RequestOrigin origin,
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
