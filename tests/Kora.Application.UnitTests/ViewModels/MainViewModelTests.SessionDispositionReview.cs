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
    private static readonly SessionDispositionReviewCommand DispositionTarget = new(
        Guid.Parse("12345678-1234-1234-abcd-123456789012"), 7, 3);
    private const string DispositionInput = "review session disposition 12345678-1234-1234-abcd-123456789012 7 3";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompoundReviewKeepsOriginalTypedOrCurrentNameVoiceOriginWithoutModelFallback(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new DispositionPresenter();
        f.ViewModel.BindSessionDispositionReview(presenter);
        const string input = "review session stop-and-delete 12345678-1234-1234-abcd-123456789012 7 3";
        if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, " + input, 1); }
        else { await f.RunAsync(input); }
        presenter.Commands.Should().ContainSingle().Which.Should().Be(DispositionTarget with { StopRunningWork = true });
        presenter.Origins.Should().ContainSingle().Which.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        f.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task ExactDispositionReviewUsesOnlyBoundNativePresenterAndNeverModelOrSpeech(bool voice, bool displayed)
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new DispositionPresenter { Displayed = displayed };
        f.ViewModel.BindSessionDispositionReview(presenter);
        if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, " + DispositionInput, 1); }
        else { await f.RunAsync(DispositionInput); }
        presenter.Commands.Should().ContainSingle().Which.Should().Be(DispositionTarget);
        presenter.Origins.Should().ContainSingle().Which.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        f.ViewModel.ResponseTitle.Should().Be(displayed ? "Session disposition preview opened." : "Session disposition preview not opened.");
        f.ViewModel.ResponseBody.Should().Contain("separate deliberate native action").And.NotContain(DispositionTarget.SessionId.ToString("D"));
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        f.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("confirm")]
    [InlineData("unprefixed")]
    [InlineData("protected")]
    [InlineData("unbound")]
    public async Task UnadmittedDispositionInputIsReservedBeforeModelAndNeverFallsBack(string mode)
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new DispositionPresenter();
        if (mode is not "unbound") { f.ViewModel.BindSessionDispositionReview(presenter); }
        if (mode is "protected")
        {
            f.CallState.SetState(CallState.Active);
            await f.Dispatcher.LastInvocation;
        }
        var input = mode switch
        {
            "invalid" => "review session disposition selected 1 0",
            "confirm" => "confirm session disposition 12345678-1234-1234-abcd-123456789012 7 3",
            _ => DispositionInput,
        };
        await f.RaiseActivatedTranscriptAsync((mode is "unprefixed" ? "" : "Kora, ") + input, 1);
        presenter.Commands.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        f.ViewModel.ResponseTitle.Should().Contain(mode is "unbound" ? "unavailable" : mode is "invalid" or "confirm" ? "not accepted" : "denied");
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.ModelSuggestion)]
    [InlineData(SecurityAuditInitiator.LocalUser)]
    [InlineData(SecurityAuditInitiator.VoiceCommand)]
    public async Task DispositionEntryRequiresOriginalEnabledChannel(SecurityAuditInitiator initiator)
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new DispositionPresenter();
        f.ViewModel.BindSessionDispositionReview(presenter);
        await f.ViewModel.ExecuteSessionDispositionReviewAsync(DispositionTarget, initiator, true);
        presenter.Commands.Should().HaveCount(initiator == SecurityAuditInitiator.LocalUser ? 1 : 0);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("cancel")]
    [InlineData("argument")]
    [InlineData("invalid")]
    [InlineData("unexpected")]
    public async Task DispositionPresenterFailureIsExplicitAndPrivateDetailsNeverReachOutput(string failure)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.BindSessionDispositionReview(new DispositionPresenter
        {
            Failure = failure switch
            {
                "io" => new IOException("private inventory"),
                "access" => new UnauthorizedAccessException("private inventory"),
                "cancel" => new OperationCanceledException("private inventory"),
                "argument" => new ArgumentException("private inventory"),
                "invalid" => new InvalidOperationException("private inventory"),
                _ => new NotSupportedException("programming error"),
            },
        });
        var act = () => f.RunAsync(DispositionInput);
        if (failure is "unexpected") { await act.Should().ThrowAsync<NotSupportedException>(); }
        else
        {
            await act();
            f.ViewModel.ResponseTitle.Should().Be("Session disposition review unavailable.");
            f.ViewModel.ResponseBody.Should().NotContain("private inventory");
        }
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("dispose")]
    [InlineData("locked")]
    [InlineData("rebind")]
    [InlineData("call")]
    [InlineData("question")]
    [InlineData("grant")]
    [InlineData("approval")]
    public async Task QueuedOrLateDispositionCallbacksLoseAdmissionWithoutPublishingOrChangingPendingDecisions(string mode)
    {
        var f = await CreateDispositionReadyFixture();
        var presenter = new DispositionPresenter();
        f.ViewModel.BindSessionDispositionReview(presenter);
        presenter.BeforeReturn = async () =>
        {
            switch (mode)
            {
                case "dispose": f.ViewModel.Dispose(); break;
                case "locked": f.Session.IsUnlocked = false; break;
                case "rebind": f.ViewModel.BindSessionDispositionReview(null); break;
                case "call": f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; break;
                case "grant":
                    await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.EnableHostedModels, ModelApprovalScope.Always));
                    break;
                default:
                    f.Reasoner.Question = mode is "question" ? new("Which?", ["One", "Two"]) : null;
                    f.Reasoner.Action = mode is "approval" ? BuiltInAction.EnableHostedModels : null;
                    await f.RunAsync("please choose");
                    await f.ViewModel.ActiveReasoningTask!;
                    break;
            }
        };
        await f.ViewModel.ExecuteSessionDispositionReviewAsync(DispositionTarget, SecurityAuditInitiator.TypedCommand, false);
        presenter.LastAdmission.Should().BeFalse();
        f.ViewModel.ResponseTitle.Should().NotBe("Session disposition preview opened.");
        if (mode is "question") { f.ViewModel.IsModelQuestionPending.Should().BeTrue(); }
        if (mode is "grant") { f.ViewModel.IsGrantChangePending.Should().BeTrue(); }
        if (mode is "approval") { f.ViewModel.IsModelActionApprovalPending.Should().BeTrue(); }
    }

    [Theory]
    [InlineData("question")]
    [InlineData("grant")]
    [InlineData("approval")]
    public async Task ExistingPendingDecisionsBlockDispositionWithoutConsumingOrAnsweringThem(string mode)
    {
        var f = await CreateDispositionReadyFixture();
        var presenter = new DispositionPresenter();
        f.ViewModel.BindSessionDispositionReview(presenter);
        if (mode is "grant")
        { await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.EnableHostedModels, ModelApprovalScope.Always)); }
        else
        {
            f.Reasoner.Question = mode is "question" ? new("Which?", ["One", "Two"]) : null;
            f.Reasoner.Action = mode is "approval" ? BuiltInAction.EnableHostedModels : null;
            await f.RunAsync("choose");
            await f.ViewModel.ActiveReasoningTask!;
        }
        await f.RunAsync(DispositionInput);
        f.ViewModel.ResponseTitle.Should().Be("Session disposition review blocked.");
        presenter.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task DisposedDispositionEntryDoesNotOpenOrPublish()
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new DispositionPresenter();
        f.ViewModel.BindSessionDispositionReview(presenter);
        f.ViewModel.Dispose();
        await f.ViewModel.ExecuteSessionDispositionReviewAsync(DispositionTarget, SecurityAuditInitiator.TypedCommand, false);
        presenter.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task RevokedDispositionFailureDoesNotPublishAfterPresenterReplacement()
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new DispositionPresenter
        {
            BeforeReturn = () =>
            {
                f.ViewModel.BindSessionDispositionReview(null);
                throw new IOException("private inventory must not escape");
            },
        };
        f.ViewModel.BindSessionDispositionReview(presenter);
        await f.RunAsync(DispositionInput);
        f.ViewModel.ResponseTitle.Should().NotBe("Session disposition review unavailable.");
        f.ViewModel.ResponseBody.Should().NotContain("private inventory");
    }

    [Fact]
    public async Task QueuedDispositionLaunchRevalidatesBeforeNativeDispatch()
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new DispositionPresenter();
        f.ViewModel.BindSessionDispositionReview(presenter);
        f.Dispatcher.BeforeInvoke = () => f.ViewModel.BindSessionDispositionReview(null);
        await f.ViewModel.ExecuteSessionDispositionReviewAsync(DispositionTarget, SecurityAuditInitiator.TypedCommand, false);
        presenter.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task NewOriginalInputRetiresAnInFlightDispositionLaunchWithoutOverwritingTheNewResponse()
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new DispositionPresenter
        {
            BeforeReturn = () => f.RunAsync("help"),
        };
        f.ViewModel.BindSessionDispositionReview(presenter);
        await f.ViewModel.ExecuteSessionDispositionReviewAsync(DispositionTarget, SecurityAuditInitiator.TypedCommand, false);
        presenter.LastAdmission.Should().BeFalse();
        f.ViewModel.ResponseTitle.Should().Be("Built-in commands are ready.");
    }

    [Theory]
    [InlineData("name")]
    [InlineData("consent")]
    public async Task OriginalActivatedDispositionReviewCannotSurviveNameOrConsentChange(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RaiseActivatedTranscriptAsync("Kora, session help", 1);
        var presenter = new DispositionPresenter
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
        f.ViewModel.BindSessionDispositionReview(presenter);
        await f.ViewModel.ExecuteSessionDispositionReviewAsync(DispositionTarget, SecurityAuditInitiator.VoiceCommand, true);
        presenter.Origins.Should().ContainSingle().Which.Should().Be(RequestOrigin.ActivatedVoice);
        presenter.LastAdmission.Should().BeFalse();
        f.ViewModel.ResponseTitle.Should().NotBe("Session disposition preview opened.");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task BusyDispositionReviewRemainsAvailableWithoutTakingOverOtherWorkAndHelpDisclosesNativeBoundary()
    {
        var f = await Fixture.CreateInitializedAsync();
        var presenter = new DispositionPresenter();
        f.ViewModel.BindSessionDispositionReview(presenter);
        f.Probe.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = f.ViewModel.DetectMicrophonesAsync();
        f.ViewModel.CommandText = DispositionInput;
        f.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue();
        await f.RunAsync(DispositionInput);
        presenter.Commands.Should().ContainSingle();
        f.Probe.Gate.SetResult();
        await busy;
        await f.RunAsync("help");
        f.ViewModel.ResponseBody.Should().Contain(SessionDispositionReviewCommand.Syntax).And.Contain("separate native action");
    }

    private sealed class DispositionPresenter : ISessionDispositionReviewPresenter
    {
        internal List<SessionDispositionReviewCommand> Commands { get; } = [];
        internal List<RequestOrigin> Origins { get; } = [];
        internal bool Displayed { get; init; } = true;
        internal bool LastAdmission { get; private set; }
        internal Exception? Failure { get; init; }
        internal Func<Task>? BeforeReturn { get; set; }
        public async Task<bool> ReviewDispositionAsync(SessionDispositionReviewCommand command, RequestOrigin origin,
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

    private static async Task<Fixture> CreateDispositionReadyFixture()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        await fixture.ViewModel.InitializeAsync();
        return fixture;
    }
}
