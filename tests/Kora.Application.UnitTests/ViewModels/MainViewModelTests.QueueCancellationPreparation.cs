using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Hosting;
using Kora.Core.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task QueuePreparationFailureLogUsesGeneratedStructuredEventAndLiveHostCorrelation()
    {
        var log = new QueuePreparationLog();
        var f = new Fixture(logger: log);
        await f.ViewModel.InitializeAsync();
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        log.Entries.Clear();
        f.ViewModel.BindQueueCancellationReview(new QueuePreparationPresenter
            { BeforeReturn = () => throw new IOException("private source") });
        await f.RunAsync(QueuePreparationInput);
        var failure = log.Entries.Single(entry => entry.Event.Id == 118);
        failure.Event.Name.Should().Be("QueueCancellationPreparationFailed");
        failure.Fields["FailureKind"].Should().Be("IOException");
        failure.Fields["{OriginalFormat}"].Should().Be("Queue cancellation preparation failed with {FailureKind}; request a fresh exact native review.");
        failure.Trace.Should().NotBe(default(ActivityTraceId));
        failure.Request.Should().NotBeNull();
        stopped.Should().Contain(activity => activity.TraceId == failure.Trace && activity.Duration > TimeSpan.Zero
            && activity.Status == ActivityStatusCode.Error
            && Equals(activity.GetTagItem("kora.outcome"), HostOperationOutcome.Failed.ToString()));
        failure.Fields.Values.Should().NotContain("private source");
    }

    private const string QueuePreparationInput = "session queue cancellation review 12345678-1234-1234-abcd-123456789012 23456789-1234-1234-abcd-123456789012";
    private static readonly SessionQueueCancellationReviewCommand QueuePreparationCommand =
        SessionQueueCancellationReviewCommand.Parse(QueuePreparationInput, "Kora")!;

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task QueuePreparationRoutesOnlyToExactNativePresenterWithoutModelOrWrites(bool voice, bool displayed)
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new QueuePreparationPresenter { Displayed = displayed };
        f.ViewModel.BindQueueCancellationReview(presenter);
        if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, " + QueuePreparationInput, 1); }
        else { await f.RunAsync(QueuePreparationInput); }
        presenter.Commands.Should().ContainSingle().Which.Should().BeEquivalentTo(QueuePreparationCommand);
        presenter.Origins.Should().Equal(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        presenter.LastAdmission.Should().BeTrue();
        f.ViewModel.ResponseTitle.Should().Be(displayed ? "Queue cancellation review prepared." : "Queue cancellation review not opened.");
        f.ViewModel.ResponseBody.Should().Contain("one-shot native confirmation").And.NotContain(QueuePreparationCommand.SessionId.ToString("D"));
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        f.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("help")]
    [InlineData("unbound")]
    [InlineData("unprefixed")]
    [InlineData("protected")]
    public async Task QueuePreparationIsReservedAndExplicitlyUnavailableWithoutFallback(string mode)
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new QueuePreparationPresenter();
        if (mode is not "unbound") { f.ViewModel.BindQueueCancellationReview(presenter); }
        if (mode is "protected")
        {
            f.CallState.SetState(CallState.Active);
            await f.Dispatcher.LastInvocation;
        }
        var input = mode switch
        {
            "invalid" => "session queue cancellation confirm current all",
            "help" => "session queue cancellation help",
            _ => QueuePreparationInput,
        };
        await f.RaiseActivatedTranscriptAsync((mode is "unprefixed" ? "" : "Kora, ") + input, 1);
        presenter.Commands.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.ResponseTitle.Should().Contain(mode switch
        {
            "invalid" => "not accepted", "help" => "help", "unbound" => "unavailable", _ => "denied",
        });
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.ModelSuggestion)]
    [InlineData(SecurityAuditInitiator.LocalUser)]
    [InlineData(SecurityAuditInitiator.VoiceCommand)]
    public async Task QueuePreparationRequiresOriginalEnabledInput(SecurityAuditInitiator initiator)
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new QueuePreparationPresenter();
        f.ViewModel.BindQueueCancellationReview(presenter);
        await f.ViewModel.ExecuteQueueCancellationPreparationAsync(QueuePreparationCommand, initiator, true);
        presenter.Commands.Should().HaveCount(initiator == SecurityAuditInitiator.LocalUser ? 1 : 0);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("cancel")]
    [InlineData("argument")]
    [InlineData("invalid")]
    [InlineData("unexpected")]
    [InlineData("revoked")]
    public async Task QueuePreparationFailureIsExplicitAndDoesNotLeakPrivateDetails(string failure)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.BindQueueCancellationReview(new QueuePreparationPresenter
        {
            BeforeReturn = () =>
            {
                if (failure is "revoked") { f.ViewModel.BindQueueCancellationReview(null); }
                throw failure switch
                {
                    "io" or "revoked" => new IOException("private inventory"),
                    "access" => new UnauthorizedAccessException("private inventory"),
                    "cancel" => new OperationCanceledException("private inventory"),
                    "argument" => new ArgumentException("private inventory"),
                    "invalid" => new InvalidOperationException("private inventory"),
                    _ => new NotSupportedException("programming error"),
                };
            },
        });
        var act = () => f.RunAsync(QueuePreparationInput);
        if (failure is "unexpected") { await act.Should().ThrowAsync<NotSupportedException>(); }
        else
        {
            await act();
            if (failure is "revoked") { f.ViewModel.ResponseTitle.Should().NotBe("Queue cancellation preparation unavailable."); }
            else { f.ViewModel.ResponseTitle.Should().Be("Queue cancellation preparation unavailable."); }
            f.ViewModel.ResponseBody.Should().NotContain("private inventory");
        }
    }

    [Theory]
    [InlineData("dispose")]
    [InlineData("locked")]
    [InlineData("rebind")]
    [InlineData("call")]
    [InlineData("question")]
    [InlineData("grant")]
    [InlineData("approval")]
    [InlineData("input")]
    [InlineData("consent")]
    [InlineData("name")]
    [InlineData("owner")]
    [InlineData("owner-revision")]
    [InlineData("binding")]
    [InlineData("privacy")]
    [InlineData("voice-generation")]
    [InlineData("early-input")]
    public async Task QueuePreparationLateCallbacksLoseOriginalAdmissionWithoutTakingOverDecisions(string mode)
    {
        var f = await CreateDispositionReadyFixture();
        await f.RaiseActivatedTranscriptAsync("Kora, session help", 1);
        var owner = 1L;
        f.ViewModel.BindCallOwnershipGate(() => true, () => owner);
        var presenter = new QueuePreparationPresenter
        {
            BeforeReturn = async () =>
            {
                switch (mode)
                {
                    case "dispose": f.ViewModel.Dispose(); break;
                    case "locked": f.Session.IsUnlocked = false; break;
                    case "rebind": f.ViewModel.BindQueueCancellationReview(null); break;
                    case "call": f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; break;
                    case "input": await f.RunAsync("help"); break;
                    case "early-input": await f.RunAsync("open sessions"); break;
                    case "owner": f.ViewModel.BindCallOwnershipGate(() => false); break;
                    case "owner-revision": owner++; break;
                    case "binding": f.ViewModel.BindCallOwnershipGate(() => true, () => owner); break;
                    case "privacy":
                        f.PrivacyObservation.Current = f.PrivacyObservation.Current with
                            { TopologyRevision = f.PrivacyObservation.Current.TopologyRevision + 1 };
                        break;
                    case "voice-generation": f.Voice.AdvanceGeneration(); break;
                    case "consent": await f.ViewModel.SetVoiceConsentAsync(false); break;
                    case "name":
                        f.ViewModel.AssistantNameInput = "Nova";
                        await f.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
                        break;
                    case "grant":
                        await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
                        break;
                    default:
                        f.Reasoner.Question = mode is "question" ? new("Which?", ["One", "Two"]) : null;
                        f.Reasoner.Action = mode is "approval" ? BuiltInAction.LockMachine : null;
                        await f.RunAsync("choose");
                        await f.ViewModel.ActiveReasoningTask!;
                        break;
                }
            },
        };
        f.ViewModel.BindQueueCancellationReview(presenter);
        await f.ViewModel.ExecuteQueueCancellationPreparationAsync(QueuePreparationCommand, SecurityAuditInitiator.VoiceCommand, true);
        presenter.LastAdmission.Should().BeFalse();
        f.ViewModel.ResponseTitle.Should().NotBe("Queue cancellation review prepared.");
        if (mode is "question") { f.ViewModel.IsModelQuestionPending.Should().BeTrue(); }
        if (mode is "grant") { f.ViewModel.IsGrantChangePending.Should().BeTrue(); }
        if (mode is "approval") { f.ViewModel.IsModelActionApprovalPending.Should().BeTrue(); }
    }

    [Theory]
    [InlineData("question")]
    [InlineData("grant")]
    [InlineData("approval")]
    public async Task QueuePreparationPreservesAlreadyPendingDecisions(string mode)
    {
        var f = await CreateDispositionReadyFixture();
        var presenter = new QueuePreparationPresenter();
        f.ViewModel.BindQueueCancellationReview(presenter);
        if (mode is "grant")
        { await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always)); }
        else
        {
            f.Reasoner.Question = mode is "question" ? new("Which?", ["One", "Two"]) : null;
            f.Reasoner.Action = mode is "approval" ? BuiltInAction.LockMachine : null;
            await f.RunAsync("choose");
            await f.ViewModel.ActiveReasoningTask!;
        }
        await f.RunAsync(QueuePreparationInput);
        presenter.Commands.Should().BeEmpty();
        f.ViewModel.ResponseTitle.Should().Be("Queue cancellation preparation blocked.");
    }

    [Fact]
    public async Task DisposedPreparationCannotOpenAndBusyExplicitHelpRemainsDiscoverable()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.Probe.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = f.ViewModel.DetectMicrophonesAsync();
        f.ViewModel.CommandText = QueuePreparationInput;
        f.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue();
        f.Probe.Gate.SetResult();
        await busy;
        await f.RunAsync("help");
        f.ViewModel.ResponseBody.Should().Contain(SessionQueueCancellationReviewCommand.Syntax);
        f.ViewModel.Dispose();
        await f.ViewModel.ExecuteQueueCancellationPreparationAsync(QueuePreparationCommand, SecurityAuditInitiator.TypedCommand, false);
    }

    private sealed class QueuePreparationPresenter : ISessionQueueCancellationReviewPresenter
    {
        internal List<SessionQueueCancellationReviewCommand> Commands { get; } = [];
        internal List<RequestOrigin> Origins { get; } = [];
        internal bool Displayed { get; init; } = true;
        internal bool LastAdmission { get; private set; }
        internal Func<Task>? BeforeReturn { get; init; }
        public async Task<bool> PrepareQueueCancellationAsync(SessionQueueCancellationReviewCommand command, RequestOrigin origin,
            Func<bool> admitted, CancellationToken token)
        {
            Commands.Add(command);
            Origins.Add(origin);
            if (BeforeReturn is not null) { await BeforeReturn(); }
            LastAdmission = admitted();
            return Displayed;
        }
    }

    private sealed class QueuePreparationLog : ILogger<MainViewModel>
    {
        internal List<(EventId Event, Dictionary<string, object?> Fields, ActivityTraceId Trace, HostRequest? Request)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> fields)
            { Entries.Add((eventId, fields.ToDictionary(), Activity.Current?.TraceId ?? default, HostActivity.Current?.Request)); }
        }
    }
}
