using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Presentation;
using Kora.Application.UnitTests.Presentation;
using Kora.Core.Hosting;
using Kora.Application.Voice;
using Kora.Core.Diagnostics;

using Microsoft.Extensions.Logging.Abstractions;

using DetailsOfferTestSource = Kora.Application.UnitTests.Presentation.DetailsOfferServiceTests.DetailsOfferTestSource;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("native")]
    [InlineData("typed")]
    [InlineData("voice")]
    [InlineData("no")]
    [InlineData("mismatch")]
    [InlineData("retire")]
    [InlineData("revalidate")]
    [InlineData("queued")]
    [InlineData("failure")]
    [InlineData("oom")]
    [InlineData("privacy")]
    [InlineData("rebind")]
    public async Task ExactDetailsPresentationSharesNativeTypedAndDeliberateVoiceAnswerAuthority(string route)
    {
        var f = await Fixture.CreateInitializedAsync();
        using var source = new DetailsOfferTestSource();
        var offers = new DetailsOfferService(source.Store, source.Time);
        f.ViewModel.BindDetailsOfferPriority(static () => true);
        var revealed = 0;
        f.ViewModel.CurrentDetailsOffer.Should().BeNull();
        f.ViewModel.DetailsQuestionText.Should().BeEmpty();
        f.ViewModel.HasDetailsQuestion.Should().BeFalse();
        await f.ViewModel.AnswerDetailsOfferAsync(true);
        await f.ViewModel.CaptureDetailsOfferAnswer(true)();
        f.ViewModel.PresentDetailsOffer(source.Request);
        revealed.Should().Be(0, "an unbound presenter is unavailable");
        f.ViewModel.BindDetailsOffers(offers, () => () => source.Live, (_, live) =>
        {
            live().Should().BeTrue();
            revealed++;
#pragma warning disable CA2201 // Inject the fatal boundary deterministically without inducing real memory pressure.
            return route is "oom" ? Task.FromException<bool>(new OutOfMemoryException("Synthetic severe native failure."))
                : Task.FromResult(route is not "failure");
#pragma warning restore CA2201
        });
        if (route is "queued") { f.Dispatcher.BeforeInvoke = () => source.Live = false; }
        f.ViewModel.PresentDetailsOffer(source.Request);
        if (route is not "queued")
        {
            await f.ViewModel.DetailsOfferPresented;
            f.ViewModel.HasSessionOperationReview.Should().BeTrue();
            f.ViewModel.IsResponseInteractionPending.Should().BeTrue();
            f.ViewModel.CommandInputPlaceholder.Should().Contain("exact Details question");
            f.ViewModel.CommandSubmitLabel.Should().Be("Answer");
            f.ViewModel.DetailsQuestionText.Should().Contain("exact completed answer");
            if (route is "native") { await f.ViewModel.CaptureDetailsOfferAnswer(true)(); }
            if (route is "typed") { await f.RunAsync("show it"); }
            if (route is "no") { await f.RunAsync("no"); }
            if (route is "failure" or "oom") { await f.ViewModel.AnswerDetailsOfferAsync(true); }
            if (route is "voice") { await f.RaiseActivatedTranscriptAsync("Kora, yes", 1); }
            if (route is "mismatch")
            {
                await f.RunAsync("change the saved preference");
                f.ViewModel.Transcript.Should().Contain("No other request");
                source.Store.Completed.Should().BeEmpty();
                await f.ViewModel.AnswerDetailsOfferAsync(false);
            }
            if (route is "retire") { f.ViewModel.RetireDetailsOffer(); }
            if (route is "revalidate") { source.Live = false; f.ViewModel.RevalidateDetailsOffer(); }
            if (route is "privacy")
            {
                f.ViewModel.CloseForObservedPrivacyEvent("Synthetic offer privacy withdrawal", hidePresentation: true);
                await f.ViewModel.PrivacyClosureTask;
            }
            if (route is "rebind")
            { f.ViewModel.BindDetailsOffers(offers, () => () => false, (_, _) => Task.FromResult(false)); }
        }
        if (route is "oom")
        { await f.ViewModel.DetailsOfferOperation.Invoking(value => value).Should().ThrowAsync<OutOfMemoryException>(); }
        else { await f.ViewModel.DetailsOfferOperation; }
        f.ViewModel.HasDetailsQuestion.Should().BeFalse();
        f.ViewModel.HasSessionOperationReview.Should().BeFalse();
        revealed.Should().Be(route is "native" or "typed" or "voice" or "failure" or "oom" ? 1 : 0);
        await offers.DisposeAsync();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestLocalNativeChoiceNeverWritesAPreferenceOrCreatesAuthorityByItself()
    {
        var f = await Fixture.CreateInitializedAsync();
        using var source = new DetailsOfferTestSource();
        await using var offers = new DetailsOfferService(source.Store, source.Time);
        f.ViewModel.OfferDetailsOnce = true;
        f.ViewModel.OfferDetailsOnce.Should().BeFalse();
        var store = BindRequestCommands(f);
        f.ViewModel.BindDetailsOffers(offers, () => () => true, (_, _) => Task.FromResult(true));
        f.ViewModel.PublishForegroundConversationTarget(new ForegroundConversationTarget(store.Session, () => true));
        await f.RunAsync("ask this conversation original exact input");
        f.ViewModel.OfferDetailsOnce.Should().BeFalse();
        f.ViewModel.OfferDetailsOnce = true;
        f.ViewModel.OfferDetailsOnce.Should().BeTrue();
        f.ViewModel.OfferDetailsOnce = false;
        f.ViewModel.OfferDetailsOnce.Should().BeFalse();
        await f.ViewModel.CancelForegroundRequestAsync();
        source.Store.Question.Should().BeNull();
        store.RequestRetentions.Should().Be(0);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task OptedForegroundRequestIssuesOfferOnlyAfterTheCanonicalHostReallyRetainsAnAnswer(bool available, bool rebind)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.BindCallOwnershipGate(static () => true);
        f.ViewModel.BindVoiceOwnershipGate(static () => true);
        f.ViewModel.BindDetailsOfferPriority(static () => true);
        using var source = new DetailsOfferTestSource();
        await using var offers = new DetailsOfferService(source.Store, source.Time);
        await using var realTurn = new WakeConversation(f, static () => true);
        realTurn.Adapter.ResponseBody = "Brief response:\nBrief.\n\nDetailed response:\n# Details\n\nExact original answer.";
        realTurn.Adapter.Release.TrySetResult();
        var workspace = new SessionWorkspaceService(realTurn.Store, new(f.HostStore), realTurn.Store,
            NullLogger<SessionWorkspaceService>.Instance);
        await using var conversation = new SessionConversationService(realTurn.Store, realTurn.Store, workspace,
            realTurn.Store, realTurn.Turns, new WakeClock(), NullLogger<SessionConversationService>.Instance, execution: realTurn.Turns);
        var requests = new SessionRequestCommandService(workspace, conversation, realTurn.Store, new WakeClock());
        if (rebind) { realTurn.Adapter.BeforeReply = () => f.ViewModel.BindSessionCommands(workspace); }
        f.ViewModel.BindSessionCommands(workspace, requests);
        var reveals = 0;
        var retired = 0;
        DetailsOfferRequest? acceptedRequest = null;
        if (available)
        {
            f.ViewModel.BindDetailsOffers(offers, () => () => true, async (request, live) =>
            {
                live().Should().BeTrue();
                request.Receipt.Message.Should().Be(realTurn.Store.RequestMessage);
                var content = await offers.ReadDetailAsync(request, workspace, live, TestContext.Current.CancellationToken);
                content.Source.Should().Be(realTurn.Adapter.ResponseBody);
                request.PresentationRequest!.Origin.Should().Be(RequestOrigin.HostSystem);
                acceptedRequest = request;
                reveals++;
                return true;
            }, _ => retired++);
        }
        f.ViewModel.PublishForegroundConversationTarget(new(realTurn.Store.Session, static () => true));
        await f.RunAsync("ask this conversation with details offer exact original input");
        f.ViewModel.OfferDetailsOnce.Should().BeTrue();
        await f.ViewModel.ConfirmForegroundRequestAsync();
        if (rebind)
        {
            realTurn.Store.RequestMessage!.AssistantResponse.Should().BeNull();
            source.Store.Question.Should().BeNull();
            realTurn.Adapter.Calls.Should().Be(1);
            f.Reasoner.Requests.Should().BeEmpty();
            return;
        }
        realTurn.Store.RequestMessage!.AssistantResponse!.Body.Should().Be(realTurn.Adapter.ResponseBody);
        realTurn.Adapter.Calls.Should().Be(1);
        if (available)
        {
            await f.ViewModel.DetailsOfferPresented;
            f.ViewModel.CurrentDetailsOffer!.DetailsOffer!.OriginalRequest.Should().Be(realTurn.Store.RequestMessage.Request);
            await f.RunAsync("yes");
            await f.ViewModel.DetailsOfferOperation;
            var decisionCount = source.Store.Serial!.Commits.Count;
            using (var passive = HostActivity.BeginRoot(acceptedRequest!.PresentationRequest!,
                HostActivityLayer.Application, HostOperation.Presentation))
            {
                (await offers.ReadDetailAsync(acceptedRequest, workspace, static () => true, TestContext.Current.CancellationToken))
                    .Source.Should().Be(realTurn.Adapter.ResponseBody);
            }
            source.Store.Serial.Commits.Should().HaveCount(decisionCount, "passive reading cannot append to the sealed presentation task");
            using (var native = HostActivity.BeginRoot(new(new(Guid.NewGuid()), realTurn.Store.Session.Authority.SessionId,
                new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Presentation))
            {
                await workspace.Invoking(value => value.ReadConversationDetailAsync(realTurn.Store.Session,
                    realTurn.Store.RequestMessage!, static () => true, TestContext.Current.CancellationToken,
                    observationId: Guid.NewGuid())).Should().ThrowAsync<InvalidOperationException>();
            }
        }
        reveals.Should().Be(available ? 1 : 0);
        retired.Should().Be(available ? 1 : 0);
        f.Reasoner.Requests.Should().BeEmpty();
        realTurn.Turns.IsQuiescent.Should().BeTrue();
        requests.TakeDetailsOffer(new(realTurn.Store.RequestMessage, realTurn.Store.TaskResult!.Task)).Should().BeNull();
        requests.Invoking(value => value.SetDetailsOfferChoice(true)).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VoiceAnswersNeedBothTheCapturedQuestionAndCurrentNameActivation(bool captureBeforeQuestion)
    {
        var f = await Fixture.CreateInitializedAsync();
        using var source = new DetailsOfferTestSource();
        await using var offers = new DetailsOfferService(source.Store, source.Time);
        f.ViewModel.BindDetailsOfferPriority(static () => true);
        f.ViewModel.BindDetailsOffers(offers, () => () => true, (_, _) => Task.FromResult(true));
        if (captureBeforeQuestion)
        {
            await f.ViewModel.SetVoiceConsentAsync(true);
            await f.ViewModel.ToggleListeningCommand.ExecuteAsync();
            await f.ViewModel.BeginPushToTalkAsync();
            await f.ViewModel.EndPushToTalkAsync();
        }
        f.ViewModel.PresentDetailsOffer(source.Request);
        await f.ViewModel.DetailsOfferPresented;
        if (captureBeforeQuestion) { await f.Voice.RaiseTranscriptAsync("Kora, yes", 1); }
        else { await f.RaiseActivatedTranscriptAsync("yes", 1); }
        if (!captureBeforeQuestion)
        { f.Voice.StartedPhrases.Should().Contain("Kora not now").And.Contain("show it"); }
        f.ViewModel.Transcript.Should().Contain("not admitted for the exact Details question");
        source.Store.Completed.Should().BeEmpty();
        await f.ViewModel.AnswerDetailsOfferAsync(false);
        await f.ViewModel.DetailsOfferOperation;
        source.Store.Completed.Should().Equal(false);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("stop listening")]
    [InlineData("stop speaking")]
    public async Task SafetyCommandsKeepTheirPrecedenceOverAnOptionalDetailsQuestion(string command)
    {
        var f = await Fixture.CreateInitializedAsync();
        using var source = new DetailsOfferTestSource();
        await using var offers = new DetailsOfferService(source.Store, source.Time);
        f.ViewModel.BindDetailsOfferPriority(static () => true);
        f.ViewModel.BindDetailsOffers(offers, () => () => true, (_, _) => Task.FromResult(true));
        f.ViewModel.PresentDetailsOffer(source.Request);
        await f.ViewModel.DetailsOfferPresented;
        await f.RunAsync(command);
        f.ViewModel.RetireDetailsOffer();
        await f.ViewModel.DetailsOfferOperation;
        source.Store.Completed.Should().Equal(false);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Kora", "yes", true)]
    [InlineData("Stop", "yes", true)]
    [InlineData("Stop", "listening", false)]
    public async Task ExistingQualifiedWakeActivationCapturesTheExactQuestionWithoutOpeningAReplyTurn(
        string name, string transcript, bool accepted)
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var f = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        f.NamePreferences.Name = name;
        f.ViewModel.BindCallOwnershipGate(static () => true);
        using var source = new DetailsOfferTestSource();
        await using var offers = new DetailsOfferService(source.Store, source.Time);
        f.ViewModel.BindDetailsOfferPriority(static () => true);
        f.ViewModel.BindDetailsOffers(offers, () => () => true, (_, _) => Task.FromResult(true));
        await f.ViewModel.InitializeAsync();
        var cycle = await stage.NextAsync();
        try
        {
            f.ViewModel.PresentDetailsOffer(source.Request);
            await f.ViewModel.DetailsOfferPresented;
            cycle.Wake.SetResult(new(cycle.Binding.Generation, 12, true));
            await cycle.CaptureStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            cycle.Result.SetResult(new(transcript, 1, cycle.Binding.Generation));
            await Task.WhenAny(voice.Completion, stage.NextAsync());
            if (!accepted) { f.ViewModel.RetireDetailsOffer(); }
            await f.ViewModel.DetailsOfferOperation;
            source.Store.Completed.Should().Equal(accepted);
            f.Voice.StartCalls.Should().Be(0, "the offer never starts a manual or prefix-free capture");
            f.Reasoner.Requests.Should().BeEmpty();
        }
        finally
        {
            cycle.ReleaseClose.TrySetResult();
            await voice.StopAsync(TestContext.Current.CancellationToken);
            await voice.Completion;
            await f.ViewModel.DisposeAsync();
        }
    }

    [Fact]
    public async Task WithdrawalClearsNativeQuestionBeforeItsOwnedTerminalAuditFinishes()
    {
        var f = await Fixture.CreateInitializedAsync();
        using var source = new DetailsOfferTestSource();
        await using var offers = new DetailsOfferService(source.Store, source.Time);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Store.BeforeCompletion = async () => { entered.TrySetResult(); await release.Task.WaitAsync(TestContext.Current.CancellationToken); };
        f.ViewModel.RevalidateDetailsOffer();
        f.ViewModel.BindDetailsOfferPriority(static () => true);
        f.ViewModel.BindDetailsOffers(offers, () => () => true, (_, _) => Task.FromResult(true));
        f.ViewModel.PresentDetailsOffer(source.Request);
        await f.ViewModel.DetailsOfferPresented;
        f.ViewModel.RevalidateDetailsOffer();
        f.ViewModel.HasDetailsQuestion.Should().BeTrue();
        source.Live = false;
        f.ViewModel.RevalidateDetailsOffer();
        f.ViewModel.CurrentDetailsOffer.Should().BeNull();
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        f.ViewModel.DetailsOfferOperation.IsCompleted.Should().BeFalse();
        release.SetResult();
        await f.ViewModel.DetailsOfferOperation;
        source.Store.Completed.Should().Equal(false);
    }

    [Fact]
    public async Task ReadOnlyOfferReaderRejectsMissingForeignUnansweredAndRetiredControlWithoutNativeRelabelling()
    {
        var f = await Fixture.CreateInitializedAsync();
        var store = BindRequestCommands(f);
        var workspace = new SessionWorkspaceService(store, new(f.HostStore), store,
            NullLogger<SessionWorkspaceService>.Instance);
        using var source = new DetailsOfferTestSource();
        using var other = new DetailsOfferTestSource();
        await using var offers = new DetailsOfferService(source.Store, source.Time);
        await using var foreign = new DetailsOfferService(other.Store, other.Time);
        var token = TestContext.Current.CancellationToken;
        await offers.Invoking(value => value.ReadDetailAsync(source.Request, workspace, static () => true, token))
            .Should().ThrowAsync<InvalidOperationException>();
        var running = offers.RunAsync(source.Request, static () => true, (_, _) => Task.CompletedTask, _ => Task.FromResult(true), token);
        var foreignRunning = foreign.RunAsync(other.Request, static () => true, (_, _) => Task.CompletedTask, _ => Task.FromResult(true), token);
        using (var wrong = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Presentation))
        {
            await offers.Invoking(value => value.ReadDetailAsync(source.Request, workspace, static () => true, token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        using (var own = HostActivity.BeginRoot(source.Request.PresentationRequest!, HostActivityLayer.Application, HostOperation.Presentation))
        {
            await offers.Invoking(value => value.ReadDetailAsync(source.Request, workspace, static () => true, token))
                .Should().ThrowAsync<InvalidOperationException>("a pending question is not an accepted Yes");
        }
        using (var otherHost = HostActivity.BeginRoot(other.Request.PresentationRequest!, HostActivityLayer.Application, HostOperation.Presentation))
        {
            await offers.Invoking(value => value.ReadDetailAsync(other.Request, workspace, static () => true, token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        source.Live = false;
        using (var revoked = HostActivity.BeginRoot(source.Request.PresentationRequest!, HostActivityLayer.Application, HostOperation.Presentation))
        {
            await offers.Invoking(value => value.ReadDetailAsync(source.Request, workspace, static () => true, token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        offers.Withdraw();
        await running;
        using (var retired = HostActivity.BeginRoot(source.Request.PresentationRequest!, HostActivityLayer.Application, HostOperation.Presentation))
        {
            await offers.Invoking(value => value.ReadDetailAsync(source.Request, workspace, static () => true, token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        foreign.Withdraw();
        await foreignRunning;
        source.Store.Completed.Should().Equal(false);
        other.Store.Completed.Should().Equal(false);
        f.Reasoner.Requests.Should().BeEmpty();
    }
}