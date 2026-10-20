using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private const string MoveReviewInput = "session queue move review 12345678-1234-1234-abcd-123456789012 "
        + "12345678-1234-1234-abcd-123456789013 12345678-1234-1234-abcd-123456789014 up";

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task MoveReviewRoutesOnlyToNativePresenterWithOriginalChannelAndNoModelOrSpeech(bool voice, bool displayed)
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new MoveReviewPresenter { Displayed = displayed };
        f.ViewModel.BindSessionQueueMoveReview(presenter);
        await f.RunAsync("set assistant name to Nova");
        if (voice) { await f.RaiseActivatedTranscriptAsync("Nova, " + MoveReviewInput, 1); }
        else { await f.RunAsync(MoveReviewInput); }
        presenter.Commands.Should().ContainSingle().Which.Should().Be(SessionQueueMoveReviewCommand.Parse(MoveReviewInput, "Nova"));
        presenter.Origins.Should().Equal(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        presenter.LastAdmission.Should().BeTrue();
        f.ViewModel.Transcript.Should().StartWith(displayed ? "Queue move review prepared." : "Queue move review not prepared.")
            .And.Contain("separate one-shot native action").And.NotContain("12345678");
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        f.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("unbound")]
    [InlineData("unprefixed")]
    [InlineData("protected")]
    [InlineData("owner")]
    public async Task MoveReviewUnadmittedInputIsReservedAndNeverFallsBack(string mode)
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new MoveReviewPresenter();
        if (mode is not "unbound") { f.ViewModel.BindSessionQueueMoveReview(presenter); }
        if (mode is "protected") { f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; }
        if (mode is "owner") { f.ViewModel.BindCallOwnershipGate(static () => false); }
        var input = mode is "invalid" ? "session queue move confirm selected latest up" : MoveReviewInput;
        await f.RaiseActivatedTranscriptAsync((mode is "unprefixed" ? "" : "Kora, ") + input, 1);
        presenter.Commands.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        f.ViewModel.Transcript.Should().Contain(mode is "invalid" ? "not accepted" : "unavailable");
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("cancel")]
    [InlineData("argument")]
    [InlineData("invalid")]
    [InlineData("unexpected")]
    public async Task MoveReviewFailureIsExplicitAndNeverDisclosesPrivateSource(string mode)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.BindSessionQueueMoveReview(new MoveReviewPresenter
        {
            Failure = mode switch
            {
                "io" => new IOException("private source"),
                "access" => new UnauthorizedAccessException("private source"),
                "cancel" => new OperationCanceledException("private source"),
                "argument" => new ArgumentException("private source"),
                "invalid" => new InvalidOperationException("private source"),
                _ => new NotSupportedException("programming defect"),
            },
        });
        var act = () => f.RunAsync(MoveReviewInput);
        if (mode is "unexpected") { await act.Should().ThrowAsync<NotSupportedException>(); }
        else
        {
            await act();
            f.ViewModel.Transcript.Should().StartWith("Queue move review unavailable.").And.NotContain("private source");
        }
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("question")]
    [InlineData("grant")]
    [InlineData("approval")]
    public async Task PendingDecisionsAreNeitherAnsweredNorReplacedByValidOrMalformedMovePreparation(string mode)
    {
        var f = await CreateDispositionReadyFixture();
        var presenter = new MoveReviewPresenter();
        f.ViewModel.BindSessionQueueMoveReview(presenter);
        if (mode is "grant")
        { await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always)); }
        else
        {
            f.Reasoner.Question = mode is "question" ? new("Which?", ["One", "Two"]) : null;
            f.Reasoner.Action = mode is "approval" ? BuiltInAction.LockMachine : null;
            await f.RunAsync("choose");
            await f.ViewModel.ActiveReasoningTask!;
        }
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync(MoveReviewInput);
        await f.RunAsync("session queue move confirm");
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.IsResponseInteractionPending.Should().BeTrue();
        presenter.Commands.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData("dispose")]
    [InlineData("private")]
    [InlineData("rebind")]
    [InlineData("owner")]
    [InlineData("owner-bind")]
    [InlineData("call")]
    [InlineData("input")]
    [InlineData("edit")]
    [InlineData("edit-restore")]
    [InlineData("name")]
    [InlineData("question")]
    [InlineData("grant")]
    [InlineData("approval")]
    [InlineData("cancel-failure")]
    public async Task DeferredMoveReviewCallbackCannotPublishAfterOriginalAuthorityOrInputChanges(string mode)
    {
        var f = await CreateDispositionReadyFixture();
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        var presenter = new MoveReviewPresenter();
        f.ViewModel.BindSessionQueueMoveReview(presenter);
        presenter.BeforeReturn = async () =>
        {
            switch (mode)
            {
                case "dispose": f.ViewModel.Dispose(); break;
                case "private": f.Session.IsUnlocked = false; break;
                case "rebind": f.ViewModel.BindSessionQueueMoveReview(null); break;
                case "owner": owner++; break;
                case "owner-bind": f.ViewModel.BindCallOwnershipGate(static () => true, () => owner); break;
                case "call": f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; break;
                case "input": await f.RunAsync("help"); break;
                case "edit": f.ViewModel.CommandText = "changed"; break;
                case "edit-restore":
                    var originalText = f.ViewModel.CommandText;
                    f.ViewModel.CommandText = "changed"; f.ViewModel.CommandText = originalText; break;
                case "name": await f.RunAsync("set assistant name to Nova"); break;
                case "grant":
                    await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always)); break;
                case "cancel-failure": f.ViewModel.BindSessionQueueMoveReview(null); throw new OperationCanceledException();
                default:
                    f.Reasoner.Question = mode is "question" ? new("Which?", ["One", "Two"]) : null;
                    f.Reasoner.Action = mode is "approval" ? BuiltInAction.LockMachine : null;
                    await f.RunAsync("choose");
                    await f.ViewModel.ActiveReasoningTask!;
                    break;
            }
        };
        await f.RaiseActivatedTranscriptAsync("Kora, " + MoveReviewInput, 1);
        presenter.LastAdmission.Should().BeFalse();
        f.ViewModel.Transcript.Should().NotStartWith("Queue move review prepared.");
    }

    [Theory]
    [InlineData("name")]
    [InlineData("consent")]
    public async Task ActivatedMoveReviewRetiresWhenOriginalNameOrVoiceConsentChanges(string mode)
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new MoveReviewPresenter
        {
            BeforeReturn = async () =>
            {
                if (mode is "name")
                { f.ViewModel.AssistantNameInput = "Nova"; await f.ViewModel.ApplyAssistantNameCommand.ExecuteAsync(); }
                else { await f.ViewModel.SetVoiceConsentAsync(false); }
            },
        };
        f.ViewModel.BindSessionQueueMoveReview(presenter);
        await f.RaiseActivatedTranscriptAsync("Kora, " + MoveReviewInput, 1);
        presenter.Origins.Should().Equal(RequestOrigin.ActivatedVoice);
        presenter.LastAdmission.Should().BeFalse();
    }

    [Fact]
    public async Task BusyPreparationIsAvailableAndHelpShowsSeparateNativeConfirmation()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.BindSessionQueueMoveReview(new MoveReviewPresenter());
        f.Probe.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = f.ViewModel.DetectMicrophonesAsync();
        f.ViewModel.CommandText = MoveReviewInput;
        f.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue();
        await f.RunAsync(MoveReviewInput);
        f.ViewModel.Transcript.Should().StartWith("Queue move review prepared.");
        f.Probe.Gate.SetResult();
        await busy;
        await f.RunAsync("help");
        f.ViewModel.ResponseBody.Should().Contain(SessionQueueMoveReviewCommand.Syntax);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task QueuedMovePreparationRevalidatesBeforeInvokingNativePresenter()
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new MoveReviewPresenter();
        f.ViewModel.BindSessionQueueMoveReview(presenter);
        f.Dispatcher.BeforeInvoke = () => f.ViewModel.BindSessionQueueMoveReview(null);
        await f.RunAsync(MoveReviewInput);
        presenter.Commands.Should().BeEmpty();
        f.ViewModel.Transcript.Should().NotStartWith("Queue move review prepared.");
    }

    [Fact]
    public async Task QueuedMovePreparationCannotReplaceANewerNativeNavigationOrComposerBuffer()
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new MoveReviewPresenter();
        f.ViewModel.BindSessionQueueMoveReview(presenter);
        f.Dispatcher.BeforeInvoke = () => presenter.SourceCurrent = false;
        await f.RunAsync(MoveReviewInput);
        presenter.Commands.Should().BeEmpty();
        f.ViewModel.Transcript.Should().Contain("unavailable");
    }

    private sealed class MoveReviewPresenter : ISessionQueueMoveReviewPresenter
    {
        internal List<SessionQueueMoveReviewCommand> Commands { get; } = [];
        internal List<RequestOrigin> Origins { get; } = [];
        internal bool Displayed { get; init; } = true;
        internal bool LastAdmission { get; private set; }
        internal Exception? Failure { get; init; }
        internal Func<Task>? BeforeReturn { get; set; }
        internal bool SourceCurrent { get; set; } = true;
        public Func<bool> CaptureNavigationEligibility() => () => SourceCurrent;

        public async Task<bool> ReviewQueueMoveAsync(SessionQueueMoveReviewCommand command, RequestOrigin origin,
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
