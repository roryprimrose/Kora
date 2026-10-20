using AwesomeAssertions;

using Kora.Application.ViewModels;
using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private const string NamePreparationInput = "session name prepare 12345678-1234-1234-abcd-123456789012 \"Café 東京 private\"";

    [Theory]
    [InlineData(Kora.Core.Auditing.SecurityAuditInitiator.LocalUser)]
    [InlineData(Kora.Core.Auditing.SecurityAuditInitiator.ModelSuggestion)]
    [InlineData((Kora.Core.Auditing.SecurityAuditInitiator)999)]
    public async Task NamePreparationDoesNotPromoteNonCommandInitiators(Kora.Core.Auditing.SecurityAuditInitiator initiator)
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new NamePreparationPresenter();
        f.ViewModel.BindSessionNamePreparation(presenter);
        var routing = typeof(MainViewModel).GetMethod("HandleTranscriptAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (Task)routing.Invoke(f.ViewModel, [NamePreparationInput, 1f, initiator, null, null, null])!;
        presenter.Commands.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.Transcript.Should().NotContain("Café");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task NamePreparationIsNativeOnlyAndNeverEchoesNames(bool voice, bool displayed)
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new NamePreparationPresenter { Displayed = displayed };
        f.ViewModel.BindSessionNamePreparation(presenter);
        if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, " + NamePreparationInput, 1); }
        else { await f.RunAsync(NamePreparationInput); }
        presenter.Commands.Should().ContainSingle().Which.Name!.Value.Should().Be("Café 東京 private");
        presenter.LastAdmission.Should().BeTrue();
        presenter.Origins.Should().ContainSingle().Which.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        f.ViewModel.Transcript.Should().StartWith(displayed ? "Session name PREPARED / REVIEW NEEDED" : "Session name preparation not opened.");
        f.ViewModel.ResponseBody.Should().NotContain("Café").And.NotContain("12345678");
        f.ViewModel.Transcript.Should().NotContain("Café").And.NotContain("12345678");
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        f.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData("help")]
    [InlineData("malformed")]
    [InlineData("unbound")]
    [InlineData("unprefixed")]
    [InlineData("protected")]
    public async Task ReservedNamePreparationDoesNotFallback(string mode)
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new NamePreparationPresenter();
        if (mode is not "unbound") { f.ViewModel.BindSessionNamePreparation(presenter); }
        if (mode is "protected") { f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; }
        var input = mode switch
        {
            "help" => "session name help",
            "malformed" => "session name prepare selected \"Café 東京 private\"",
            _ => NamePreparationInput,
        };
        await f.RaiseActivatedTranscriptAsync((mode is "unprefixed" ? "" : "Kora, ") + input, 1);
        presenter.Commands.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        f.ViewModel.Transcript.Should().NotContain("Café");
        f.ViewModel.ResponseBody.Should().NotContain("Café");
        f.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData("question")]
    [InlineData("grant")]
    [InlineData("approval")]
    public async Task MalformedAndValidNameRequestsPreservePendingDecisions(string mode)
    {
        var f = await CreateDispositionReadyFixture();
        var presenter = new NamePreparationPresenter();
        f.ViewModel.BindSessionNamePreparation(presenter);
        if (mode is "grant")
        { await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always)); }
        else
        {
            f.Reasoner.Question = mode is "question" ? new("Which?", ["One", "Two"]) : null;
            f.Reasoner.Action = mode is "approval" ? BuiltInAction.LockMachine : null;
            await f.RunAsync("choose");
            await f.ViewModel.ActiveReasoningTask!;
        }
        foreach (var input in new[] { "session name malformed \"private\"", NamePreparationInput, "session name help" })
        {
            var title = f.ViewModel.ResponseTitle;
            var body = f.ViewModel.ResponseBody;
            await f.RunAsync(input);
            f.ViewModel.IsModelQuestionPending.Should().Be(mode is "question");
            f.ViewModel.IsGrantChangePending.Should().Be(mode is "grant");
            f.ViewModel.IsModelActionApprovalPending.Should().Be(mode is "approval");
            f.ViewModel.ResponseTitle.Should().Be(title);
            f.ViewModel.ResponseBody.Should().Be(body);
        }
        presenter.Commands.Should().BeEmpty();
    }

    [Theory]
    [InlineData("dispose")]
    [InlineData("locked")]
    [InlineData("rebind")]
    [InlineData("call")]
    [InlineData("input")]
    [InlineData("text")]
    [InlineData("name")]
    [InlineData("consent")]
    public async Task OriginalNamePreparationCannotSurviveSourceOrHostChanges(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new NamePreparationPresenter();
        f.ViewModel.BindSessionNamePreparation(presenter);
        presenter.BeforeReturn = async () =>
        {
            switch (change)
            {
                case "dispose": f.ViewModel.Dispose(); break;
                case "locked": f.Session.IsUnlocked = false; break;
                case "rebind": f.ViewModel.BindSessionNamePreparation(null); break;
                case "call": f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; break;
                case "input": await f.RunAsync("help"); break;
                case "text": f.ViewModel.CommandText = "changed"; break;
                case "name":
                    f.ViewModel.AssistantNameInput = "Nova";
                    await f.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
                    break;
                case "consent": await f.ViewModel.SetVoiceConsentAsync(false); break;
            }
        };
        await f.RaiseActivatedTranscriptAsync("Kora, " + NamePreparationInput, 1);
        presenter.LastAdmission.Should().BeFalse();
        f.ViewModel.Transcript.Should().NotStartWith("Session name PREPARED / REVIEW NEEDED");
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("cancel")]
    [InlineData("argument")]
    [InlineData("invalid")]
    public async Task NamePreparationFailureHasOnlySanitizedDiagnostics(string failure)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.BindSessionNamePreparation(new NamePreparationPresenter
        {
            Failure = failure switch
            {
                "io" => new IOException("secret private name"),
                "access" => new UnauthorizedAccessException("secret private name"),
                "cancel" => new OperationCanceledException("secret private name"),
                "argument" => new ArgumentException("secret private name"),
                _ => new InvalidOperationException("secret private name"),
            },
        });
        await f.RunAsync(NamePreparationInput);
        f.ViewModel.Transcript.Should().StartWith("Session name preparation unavailable.");
        f.ViewModel.ResponseBody.Should().NotContain("secret private name");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("input")]
    [InlineData("buffer-return")]
    [InlineData("name")]
    [InlineData("owner")]
    [InlineData("private")]
    public async Task QueuedNamePreparationChecksOriginalInputBeforeNativeDispatch(string loss)
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new NamePreparationPresenter();
        f.ViewModel.BindSessionNamePreparation(presenter);
        var original = f.ViewModel.CommandText;
        f.Dispatcher.BeforeInvoke = () =>
        {
            switch (loss)
            {
                case "input": f.ViewModel.CommandText = "new source"; break;
                case "buffer-return":
                    f.ViewModel.CommandText = "new source";
                    f.ViewModel.CommandText = original;
                    break;
                case "name": f.ViewModel.BindSessionNamePreparation(null); break;
                case "owner": f.ViewModel.BindCallOwnershipGate(static () => false); break;
                case "private": f.ViewModel.CloseForObservedPrivacyEvent("fixture", true); break;
            }
        };
        await f.RunAsync(NamePreparationInput);
        presenter.Commands.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.Transcript.Should().NotContain("Café");
    }

    [Theory]
    [InlineData("name")]
    [InlineData("input")]
    [InlineData("generation")]
    public async Task QueuedVoiceNamePreparationRetainsItsOriginalNameAndInputWithoutEcho(string loss)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RaiseActivatedTranscriptAsync("Kora, session help", 1);
        var presenter = new NamePreparationPresenter();
        f.ViewModel.BindSessionNamePreparation(presenter);
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        f.Dispatcher.BeforeInvoke = () =>
        {
            if (loss is "generation") { f.Voice.AdvanceGeneration(); }
            else if (loss is "input") { f.ViewModel.CommandText = "intervening source"; }
            else
            {
                f.ViewModel.AssistantNameInput = "Nova";
                f.ViewModel.ApplyAssistantNameCommand.ExecuteAsync().IsCompletedSuccessfully.Should().BeTrue();
            }
        };
        await f.Voice.RaiseTranscriptAsync("Kora, " + NamePreparationInput, 1);
        presenter.Commands.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.Transcript.Should().NotContain("Café");
    }

    [Fact]
    public async Task NamePreparationSanitizesStructuredLogsAndRetainsSecurityPaneOnFailure()
    {
        var logger = new NamePreparationLogger();
        var f = new Fixture(logger: logger);
        await f.ViewModel.InitializeAsync();
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        f.ViewModel.BindSessionNamePreparation(new NamePreparationPresenter { Failure = new IOException("Café 東京 private") });
        await f.RunAsync(NamePreparationInput);
        logger.Events.Should().Contain(117);
        string.Join("\n", logger.Messages).Should().NotContain("Café").And.NotContain("12345678");
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
    }

    private sealed class NamePreparationLogger : ILogger<MainViewModel>
    {
        internal List<int> Events { get; } = [];
        internal List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Events.Add(eventId.Id);
            Messages.Add(formatter(state, exception) + exception);
        }
    }

    private sealed class NamePreparationPresenter : ISessionNamePreparationPresenter
    {
        internal List<SessionNamePreparationCommand> Commands { get; } = [];
        internal List<RequestOrigin> Origins { get; } = [];
        internal bool Displayed { get; init; } = true;
        internal bool LastAdmission { get; private set; }
        internal Exception? Failure { get; init; }
        internal Func<Task>? BeforeReturn { get; set; }
        public async Task<bool> PrepareNameAsync(SessionNamePreparationCommand command, RequestOrigin origin,
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
