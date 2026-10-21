using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private const string ReplacementInput = "session queue replacement review 12345678-1234-1234-abcd-123456789012 abcdef12-1234-1234-abcd-123456789012";

    [Theory]
    [InlineData(false, true, "Kora")]
    [InlineData(true, true, "Kora")]
    [InlineData(false, false, "Kora")]
    [InlineData(false, true, "What Time")]
    [InlineData(true, true, "What Time")]
    public async Task ExactQueueReplacementUsesOnlyNativeReviewAndTruthfulLinkedPresentation(
        bool voice, bool displayed, string assistantName)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("set assistant name to " + assistantName);
        var presenter = new ReplacementPresenter { Displayed = displayed };
        f.ViewModel.BindSessionQueueReplacementReview(presenter);
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        if (voice) { await f.RaiseActivatedTranscriptAsync(assistantName + ", " + ReplacementInput, 1); }
        else { await f.RunAsync(string.Equals(assistantName, "What Time", StringComparison.Ordinal)
            ? assistantName + ", " + ReplacementInput : ReplacementInput); }
        presenter.Commands.Should().ContainSingle().Which.TaskId.Should().Be(Guid.Parse("abcdef12-1234-1234-abcd-123456789012"));
        presenter.LastAdmission.Should().BeTrue();
        f.ViewModel.ResponseTitle.Should().Be(displayed ? "Queue replacement review opened." : "Queue replacement review not opened.");
        f.ViewModel.ResponseBody.Should().Contain("Separate native confirmation");
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        f.TextToSpeech.SpokenText.Should().BeNull();
        activities.Should().Contain(activity => activity.Links.Any());
        activities.Where(activity => string.Equals(activity.OperationName, "presentation.update", StringComparison.Ordinal))
            .Should().Contain(activity => Equals(activity.GetTagItem("kora.outcome"), displayed ? "Completed" : "Cancelled"));
    }

    [Theory]
    [InlineData("help")]
    [InlineData("malformed")]
    [InlineData("unprefixed")]
    [InlineData("unbound")]
    [InlineData("protected")]
    [InlineData("question")]
    [InlineData("approval")]
    [InlineData("grant")]
    [InlineData("native")]
    public async Task QueueReplacementNeverAnswersPendingDecisionsOrGuessesAnUnavailableTarget(string mode)
    {
        var f = await CreateDispositionReadyFixture();
        var presenter = new ReplacementPresenter { NativeLive = mode is not "native" };
        if (mode is not "unbound") { f.ViewModel.BindSessionQueueReplacementReview(presenter); }
        if (mode is "protected") { f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; }
        if (mode is "grant")
        {
            await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add,
                BuiltInAction.LockMachine, ModelApprovalScope.Always));
        }
        if (mode is "question" or "approval")
        {
            f.Reasoner.Question = mode is "question" ? new("Which?", ["One", "Two"]) : null;
            f.Reasoner.Action = mode is "approval" ? BuiltInAction.LockMachine : null;
            await f.RunAsync("choose");
            await f.ViewModel.ActiveReasoningTask!;
        }
        var input = mode switch
        {
            "help" => "session queue replacement help",
            "malformed" => "session queue replacement review selected latest",
            _ => ReplacementInput,
        };
        await f.RaiseActivatedTranscriptAsync((mode is "unprefixed" ? "" : "Kora, ") + input, 1);
        presenter.Commands.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        if (mode is "question") { f.ViewModel.IsModelQuestionPending.Should().BeTrue(); }
        if (mode is "approval") { f.ViewModel.IsModelActionApprovalPending.Should().BeTrue(); }
        if (mode is "grant") { f.ViewModel.IsGrantChangePending.Should().BeTrue(); }
    }

    [Theory]
    [InlineData("new-input")]
    [InlineData("buffer")]
    [InlineData("call")]
    [InlineData("locked")]
    [InlineData("rebind")]
    [InlineData("rebind-same")]
    [InlineData("owner-binding")]
    [InlineData("dispose")]
    [InlineData("name")]
    [InlineData("consent")]
    public async Task ReplacementOriginalAdmissionCannotSurviveQueuedOrLateChanges(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        ReplacementPresenter? presenter = null;
        presenter = new ReplacementPresenter
        {
            BeforeReturn = async () =>
            {
                switch (change)
                {
                    case "new-input": await f.RunAsync("help"); break;
                    case "buffer": f.ViewModel.CommandText = "different"; break;
                    case "call": f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; break;
                    case "locked": f.Session.IsUnlocked = false; break;
                    case "rebind": f.ViewModel.BindSessionQueueReplacementReview(null); break;
                    case "rebind-same": f.ViewModel.BindSessionQueueReplacementReview(presenter); break;
                    case "owner-binding": f.ViewModel.BindCallOwnershipGate(() => true); break;
                    case "dispose": f.ViewModel.Dispose(); break;
                    case "name":
                        f.ViewModel.AssistantNameInput = "Nova";
                        await f.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
                        break;
                    default: await f.ViewModel.SetVoiceConsentAsync(false); break;
                }
            },
        };
        f.ViewModel.BindSessionQueueReplacementReview(presenter);
        await f.RaiseActivatedTranscriptAsync("Kora, " + ReplacementInput, 1);
        presenter.LastAdmission.Should().BeFalse();
        f.ViewModel.ResponseTitle.Should().NotBe("Queue replacement review opened.");
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("cancel")]
    [InlineData("argument")]
    [InlineData("invalid")]
    [InlineData("unexpected")]
    public async Task ReplacementFailuresDoNotLeakSourceOrInferSuccess(string failure)
    {
        var log = new RetentionPreviewLogger(true);
        var f = new Fixture(logger: log);
        await f.ViewModel.InitializeAsync();
        f.ViewModel.BindSessionQueueReplacementReview(new ReplacementPresenter
        {
            Failure = failure switch
            {
                "io" => new IOException("private source"),
                "access" => new UnauthorizedAccessException("private source"),
                "cancel" => new OperationCanceledException("private source"),
                "argument" => new ArgumentException("private source"),
                "invalid" => new InvalidOperationException("private source"),
                _ => new NotSupportedException("programming error"),
            },
        });
        var act = () => f.RunAsync(ReplacementInput);
        if (failure is "unexpected") { await act.Should().ThrowAsync<NotSupportedException>(); }
        else
        {
            await act();
            f.ViewModel.ResponseTitle.Should().Be("Queue replacement review unavailable.");
            f.ViewModel.ResponseBody.Should().NotContain("private source");
            var entry = log.Events.Should().ContainSingle(item => item.Name == "QueueReplacementReviewFailed").Which;
            entry.Message.Should().NotContain("private source");
            entry.Exception.Should().BeNull();
        }
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ReplacementQueuedLaunchRechecksBindingBeforeOpening()
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new ReplacementPresenter();
        f.ViewModel.BindSessionQueueReplacementReview(presenter);
        f.Dispatcher.BeforeInvoke = () => f.ViewModel.BindSessionQueueReplacementReview(null);
        await f.RunAsync(ReplacementInput);
        presenter.Commands.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ready")]
    [InlineData("unavailable")]
    [InlineData("changed")]
    public async Task ReplacementPreparationRetainsTheOriginalConfirmedQueueSettings(string mode)
    {
        var f = new Fixture(enableQueueConfiguration: true);
        await using var admission = f.OutputAdmission;
        await using var configuration = f.QueueConfiguration!;
        await f.ViewModel.InitializeAsync();
        if (mode is "unavailable") { configuration.HoldUnavailable(); }
        var presenter = new ReplacementPresenter
        {
            BeforeReturn = () =>
            {
                if (mode is "changed") { configuration.HoldUnavailable(); }
                return Task.CompletedTask;
            },
        };
        f.ViewModel.BindSessionQueueReplacementReview(presenter);
        await f.RunAsync(ReplacementInput);
        presenter.Commands.Should().HaveCount(mode is "unavailable" ? 0 : 1);
        presenter.LastAdmission.Should().Be(mode is "ready");
        if (mode is "ready") { f.ViewModel.ResponseTitle.Should().Be("Queue replacement review opened."); }
        else { f.ViewModel.ResponseTitle.Should().NotBe("Queue replacement review opened."); }
        f.QueuePreferences.Writes.Should().Be(0);
        f.ViewModel.Dispose();
    }

    private sealed class ReplacementPresenter : ISessionQueueReplacementReviewPresenter
    {
        internal List<SessionQueueReplacementReviewCommand> Commands { get; } = [];
        internal bool Displayed { get; init; } = true;
        internal bool NativeLive { get; init; } = true;
        internal bool LastAdmission { get; private set; }
        internal Func<Task>? BeforeReturn { get; init; }
        internal Exception? Failure { get; init; }
        public Func<bool> CaptureLifetime() => () => NativeLive;
        public async Task<bool> ReviewReplacementAsync(SessionQueueReplacementReviewCommand command, RequestOrigin origin,
            Func<bool> admitted, CancellationToken token)
        {
            Commands.Add(command);
            if (Failure is not null) { throw Failure; }
            if (BeforeReturn is not null) { await BeforeReturn(); }
            LastAdmission = admitted();
            return Displayed;
        }
    }
}
