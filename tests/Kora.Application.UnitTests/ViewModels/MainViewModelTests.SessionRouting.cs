using AwesomeAssertions;

using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Presentation;
using Kora.Core.Storage;
using Kora.Core.Voice;
using Kora.Core.Platform;
using Kora.Application.Hosting.SessionRouting;
using Kora.Core.Diagnostics;
using Kora.Core.Conversation.SessionRouting;
using Microsoft.Extensions.Logging.Abstractions;
using static Kora.Application.Hosting.SessionRouting.SessionRoutingVoiceReply;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task OriginalRoutingHostOwnsIndependentVoiceQuestionsAndRejectsPrematureRelease()
    {
        var f = await RoutingReplyHostAsync();
        var sample = RoutingVoiceReview("unused", RequestOrigin.LocalUi);
        f.ViewModel.CreateSessionRoutingVoiceReply(sample, sample.Candidates, null, static () => true).Should().BeNull();
        f.ViewModel.BindSessionRouting(async (body, origin, admitted) =>
        {
            var review = RoutingVoiceReview(body, origin);
            await f.ViewModel.BeginPushToTalkAsync();
            f.Voice.StartCalls.Should().Be(0);
            FluentActions.Invoking(() => f.ViewModel.BindSessionRoutingCaptureRelease(
                Kora.Application.Voice.HandsFreeVoiceContinuation.CaptureRelease.Preserved)).Should().Throw<InvalidOperationException>();
            var first = f.ViewModel.CreateSessionRoutingVoiceReply(review, review.Candidates, null, admitted)!;
            FluentActions.Invoking(() => f.ViewModel.CreateSessionRoutingVoiceReply(review, review.Candidates, null, admitted))
                .Should().Throw<InvalidOperationException>();
            var choice = first.StartAsync();
            f.Voice.StartedPhrases.Should().Equal("1", "New", "Cancel");
            FluentActions.Invoking(() => f.ViewModel.ReleaseSessionRoutingVoiceReply(first)).Should().Throw<InvalidOperationException>();
            f.Voice.RaiseTranscript("1", 1);
            (await choice)!.Destination.Should().BeSameAs(review.Candidates[0]);
            await first.DisposeAsync();
            f.ViewModel.ReleaseSessionRoutingVoiceReply(first);
            f.ViewModel.ReleaseSessionRoutingVoiceReply(first);
            var confirmation = f.ViewModel.CreateSessionRoutingVoiceReply(review, review.Candidates,
                new(review, review.Candidates[0].Entry), admitted)!;
            var confirmed = confirmation.StartAsync();
            f.Voice.RaiseTranscript("Submit original request", 1);
            (await confirmed)!.Choice.Should().Be(SessionRoutingVoiceChoice.Submit);
            await confirmation.DisposeAsync();
            f.ViewModel.ReleaseSessionRoutingVoiceReply(confirmation);
            return null;
        });
        await f.RunAsync("Explain the original routing request");
        f.Reasoner.Requests.Should().BeEmpty();
        f.Session.LockCalls.Should().Be(0);
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("voice-owner")]
    [InlineData("name")]
    [InlineData("question")]
    [InlineData("disable")]
    [InlineData("dispose")]
    [InlineData("privacy")]
    public async Task RoutingVoiceOriginalBoundariesRetireAndJoinWithoutGlobalInput(string boundary)
    {
        var f = await RoutingReplyHostAsync();
        f.ViewModel.BindSessionRouting(async (body, origin, admitted) =>
        {
            var review = RoutingVoiceReview(body, origin);
            var exact = true;
            var reply = f.ViewModel.CreateSessionRoutingVoiceReply(review, review.Candidates, null, () => exact && admitted())!;
            var answer = reply.StartAsync();
            var generation = f.Voice.Generation;
            switch (boundary)
            {
                case "owner": f.ViewModel.BindCallOwnershipGate(static () => true); break;
                case "voice-owner": f.ViewModel.BindVoiceOwnershipGate(static () => true); break;
                case "name": await f.ViewModel.SetAssistantNameAsync("Different"); break;
                case "question": exact = false; reply.Revalidate(); break;
                case "disable": await f.ViewModel.DisableListeningFromTrayAsync(); break;
                case "dispose": await f.ViewModel.DisposeAsync(); break;
                case "privacy": PublishVoiceSession(f, WindowsSessionState.Locked); break;
            }
            reply.Revalidate();
            f.Voice.RaiseTranscriptForGeneration(generation);
            (await answer).Should().BeNull();
            await reply.DisposeAsync();
            f.ViewModel.ReleaseSessionRoutingVoiceReply(reply);
            return null;
        });
        await f.RunAsync("Explain held original routing");
        f.Reasoner.Requests.Should().BeEmpty();
        f.Session.LockCalls.Should().Be(0);
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        if (boundary is not "dispose") { await f.ViewModel.DisposeAsync(); }
    }

    [Fact]
    public async Task RoutingVoiceAsyncDisposalJoinsNoncooperativeOpeningAndDelayedPhysicalStop()
    {
        var f = await RoutingReplyHostAsync();
        f.Voice.IgnoreStartCancellation = true;
        f.Voice.StartGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Voice.StopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var opening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.ViewModel.BindSessionRouting(async (body, origin, admitted) =>
        {
            var review = RoutingVoiceReview(body, origin);
            var reply = f.ViewModel.CreateSessionRoutingVoiceReply(review, review.Candidates, null, admitted)!;
            var answer = reply.StartAsync();
            opening.SetResult();
            (await answer).Should().BeNull();
            f.ViewModel.ReleaseSessionRoutingVoiceReply(reply);
            return null;
        });
        var operation = f.RunAsync("Explain an original with held capture");
        await opening.Task;
        var disposal = f.ViewModel.DisposeAsync().AsTask();
        disposal.IsCompleted.Should().BeFalse();
        operation.IsCompleted.Should().BeFalse();
        f.Voice.StartGate.SetResult();
        disposal.IsCompleted.Should().BeFalse("opening completion is not physical stop completion");
        f.Voice.StopGate.SetResult();
        await disposal;
        await operation;
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    private static SessionRoutingReview RoutingVoiceReview(string body, RequestOrigin origin)
    {
        var original = HostActivity.Current?.Request ?? HostRequest.Create(origin);
        var id = HostRequest.Create(origin).SessionId;
        var row = new SessionRouteDescriptor(new(new(id, new(1), true), new(id, new(1), new("Presented"))), Guid.NewGuid());
        var snapshot = new SessionRouteSnapshot(1, [row]);
        return new(body, origin, snapshot, SessionRouteMatch.Clear, snapshot.Descriptors,
            DateTimeOffset.MaxValue, () => HostActivity.BeginRoot(original, HostActivityLayer.Application, HostOperation.Request));
    }

    private static async Task<Fixture> RoutingReplyHostAsync()
    {
        var f = await ReplyHostAsync();
        f.ViewModel.BindSessionRoutingCaptureRelease(Kora.Application.Voice.HandsFreeVoiceContinuation.CaptureRelease.Preserved);
        return f;
    }

    [Fact]
    public async Task RoutingReadinessRechecksOriginalAuthorityAfterQuestionEligibility()
    {
        var f = await RoutingReplyHostAsync();
        var armed = false;
        var reads = 0;
        f.ViewModel.BindCallOwnershipGate(() => !armed || ++reads < 2);
        f.ViewModel.BindSessionRouting(async (body, origin, _) =>
        {
            var review = RoutingVoiceReview(body, origin);
            await using var reply = f.ViewModel.CreateSessionRoutingVoiceReply(review, review.Candidates, null, static () => true)!;
            armed = true;
            reply.CanAnswerByVoice.Should().BeFalse();
            (await reply.StartAsync()).Should().BeNull();
            f.ViewModel.ReleaseSessionRoutingVoiceReply(reply);
            return null;
        });
        await f.RunAsync("Explain the original admitted routing request");
        f.Voice.StartCalls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        await f.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task OriginalRoutingOpeningRejectsTheFinalPrePresentationAuthorityChange()
    {
        var baseline = new Fixture();
        var baselineReads = 0;
        baseline.ViewModel.BindCallOwnershipGate(() => { baselineReads++; return true; });
        var openingRead = 0;
        baseline.ViewModel.BindSessionRouting((_, _, _) => { openingRead = baselineReads; return Task.FromResult<SessionRoutingReceipt?>(null); });
        baselineReads = 0;
        await baseline.RunAsync("Explain the original routing request");
        openingRead.Should().BeGreaterThan(0);
        await baseline.ViewModel.DisposeAsync();

        var f = new Fixture();
        var reads = 0;
        var presentations = 0;
        f.ViewModel.BindCallOwnershipGate(() => ++reads < openingRead);
        f.ViewModel.BindSessionRouting((_, _, _) => { presentations++; return Task.FromResult<SessionRoutingReceipt?>(null); });
        reads = 0;
        await f.RunAsync("Explain the original routing request");
        presentations.Should().Be(0);
        f.ViewModel.ResponseTitle.Should().Be("Session routing stopped.");
        f.Reasoner.Requests.Should().BeEmpty();
        f.Voice.StartCalls.Should().Be(0);
        await f.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task RoutingCaptureReleaseBindingForwardsToTheLegacyHostedVoiceField()
    {
        var f = new Fixture();
        await using var capture = new Kora.Application.UnitTests.Interaction.QuestionReplyFixture();
        await using var handsFree = new Kora.Application.Voice.HandsFreeVoiceRecognitionService(
            capture.Capture, new RoutingUnavailableAudioStage(),
            NullLogger<Kora.Application.Voice.HandsFreeVoiceRecognitionService>.Instance);
        try
        {
            // The legacy fixture fixes this constructor dependency; native composition tests cover actual DI.
            Field(f, "voiceRecognition", handsFree);
            f.ViewModel.BindSessionRoutingCaptureRelease(Kora.Application.Voice.HandsFreeVoiceContinuation.CaptureRelease.Preserved);
            handsFree.RoutingCaptureRelease.Should().BeSameAs(Kora.Application.Voice.HandsFreeVoiceContinuation.CaptureRelease.Preserved);
            handsFree.IsCaptureQuiescent.Should().BeTrue();
            handsFree.IsAmbientListeningAvailable.Should().BeFalse();
        }
        finally { Field(f, "voiceRecognition", f.Voice); }
        await f.ViewModel.DisposeAsync();
    }

    private sealed class RoutingUnavailableAudioStage : IHandsFreeAudioStage
    {
        public bool IsAvailable => false;
        public Task<IHandsFreeAudioSession> OpenAsync(HandsFreeAudioBinding binding, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Unavailable routing wake stage cannot open audio.");
    }

    [Theory]
    [InlineData("clarification")]
    [InlineData("editor")]
    [InlineData("question-speech")]
    [InlineData("question")]
    [InlineData("prompt")]
    [InlineData("inactive-prompt")]
    [InlineData("ptt")]
    [InlineData("stop-request")]
    [InlineData("stop-release")]
    public async Task RoutingCaptureRefusesEveryExistingHeldInputOwner(string hold)
    {
        var f = await RoutingReplyHostAsync();
        await using var other = new Kora.Application.UnitTests.Interaction.QuestionReplyFixture();
        await using var prompt = new Kora.Application.Definitions.PromptInputVoiceController(other.Capture, other.Dispatcher,
            () => other.Admission, (_, _) => Task.FromResult(true),
            NullLogger<Kora.Application.Definitions.PromptInputVoiceController>.Instance, other.Time);
        var source = Kora.Core.Artifacts.ArtifactFileDocument.Parse(Kora.Core.Artifacts.ArtifactFileDocument.Encode(
            "---\nkora-format: 2\nname: example\ndescription: Test\n---\n${input:topic}", false),
            Kora.Core.Artifacts.ArtifactKind.Prompt, "kora-profile", "example").Definition with { OwnedInstructionRevision = "exact" };
        f.ArtifactCatalogue.Publish([source]);
        using var draft = new Kora.Application.Definitions.PromptParameterDraft(source, f.ArtifactCatalogue, static () => true);
        var stop = typeof(Kora.Application.ViewModels.MainViewModel).GetField("listeningStop",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(f.ViewModel)!;
        var stopping = stop.GetType().GetField("stopping", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        f.ViewModel.BindSessionRouting(async (body, origin, admitted) =>
        {
            var review = RoutingVoiceReview(body, origin);
            var editor = new Kora.Application.Definitions.CustomizationVoiceRequest(EditorVoiceTarget(), "edit this",
                HostRequest.Create(RequestOrigin.ActivatedVoice), static () => true, () => f.Voice.Generation);
            Kora.Application.Definitions.CustomizationVoiceClarification? clarification = null;
            try
            {
                // Legacy framework hold fields have no setter; this matrix supplements the public joined-owner regressions.
                if (hold is "clarification")
                { clarification = f.ViewModel.CreateCustomizationClarification(editor, static () => true, NullLogger.Instance, TestContext.Current.CancellationToken); }
                if (hold is "editor") { Field(f, "activeCustomizationVoice", editor); }
                if (hold is "question-speech") { Field(f, "questionSpeech", true); }
                if (hold is "ptt") { Field(f, "pushToTalkHeld", 1); }
                if (hold is "stop-request") { Field(f, "listeningStopRequests", 1); }
                if (hold is "stop-release") { stopping.SetValue(stop, 1); }
                if (hold is "question")
                {
                    var question = await other.Host.QuestionAsync();
                    await other.Replies.OpenAsync(question, static () => true, _ => { }, false, TestContext.Current.CancellationToken);
                    other.Replies.IsActive.Should().BeTrue();
                    Field(f, "questionVoiceReplies", other.Replies);
                }
                if (hold is "prompt" or "inactive-prompt")
                {
                    if (hold is "prompt")
                    {
                        await prompt.Begin(draft, "topic", static () => true, draft.SetValue, TestContext.Current.CancellationToken);
                        prompt.IsActive.Should().BeTrue();
                    }
                    else { prompt.IsActive.Should().BeFalse(); }
                    Field(f, "promptInputVoice", prompt);
                }
                await using var reply = f.ViewModel.CreateSessionRoutingVoiceReply(review, review.Candidates, null, admitted)!;
                f.Voice.CaptureReviewedTerminalCallback()();
                f.Voice.PublishCaptureState(new(f.Voice.Generation, false));
                f.Voice.PublishCompletion(new(f.Voice.Generation, VoiceRecognitionCompletionReason.NoSpeechRecognized));
                reply.CanAnswerByVoice.Should().Be(hold is "inactive-prompt");
                if (hold is "inactive-prompt") { await reply.DisposeAsync(); }
                else { (await reply.StartAsync()).Should().BeNull(); }
                f.ViewModel.ReleaseSessionRoutingVoiceReply(reply);
                f.Voice.StartCalls.Should().Be(0);
                f.Reasoner.Requests.Should().BeEmpty();
                return null;
            }
            finally
            {
                Field(f, "activeCustomizationVoice", null);
                Field(f, "questionSpeech", false);
                Field(f, "pushToTalkHeld", 0);
                Field(f, "listeningStopRequests", 0);
                Field(f, "questionVoiceReplies", null);
                Field(f, "promptInputVoice", null);
                stopping.SetValue(stop, 0);
                if (clarification is not null)
                {
                    await clarification.DisposeAsync();
                    f.ViewModel.ReleaseCustomizationClarification(clarification);
                }
            }
        });
        await f.RunAsync("Explain an original while another host input owner is held");
        f.Session.LockCalls.Should().Be(0);
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        await f.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task UntargetedOriginalRequestsUseRoutingButExactNewNameAndBuiltinsDoNot()
    {
        var f = await Fixture.CreateInitializedAsync();
        var bodies = new List<string>();
        f.ViewModel.BindSessionRouting((body, origin, admitted) =>
        {
            origin.Should().Be(RequestOrigin.LocalUi);
            admitted().Should().BeTrue();
            bodies.Add(body);
            return Task.FromResult<Kora.Application.Hosting.SessionRouting.SessionRoutingReceipt?>(null);
        });
        await f.RunAsync("  Preserve this exact request  ");
        bodies.Should().Equal("  Preserve this exact request  ");
        await f.RunAsync("open new request");
        await f.RunAsync("session request review \"Alpha\" -- exact addressed body");
        await f.RunAsync("what version are you running");
        bodies.Should().ContainSingle();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ActivatedVoiceKeepsOriginalOriginAndNeverCleansesThroughNativeRouting()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova");
        var origins = new List<RequestOrigin>();
        f.ViewModel.BindSessionRouting((body, origin, admitted) =>
        {
            admitted().Should().BeTrue();
            body.Should().Be("compare these plans");
            origins.Add(origin);
            return Task.FromResult<Kora.Application.Hosting.SessionRouting.SessionRoutingReceipt?>(null);
        });
        await f.RaiseActivatedTranscriptAsync("Nova, compare these plans", 1);
        origins.Should().Equal(RequestOrigin.ActivatedVoice);
        await f.RaiseActivatedTranscriptAsync("compare these plans", 1);
        origins.Should().ContainSingle();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task RoutingFailureDoesNotFallBackToLegacyModelOrSelectAnotherConversation()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.BindSessionRouting((_, _, _) => throw new InvalidOperationException("stale exact descriptor"));
        await f.RunAsync("compare these plans");
        f.ViewModel.ResponseTitle.Should().Be("Session routing stopped.");
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulRoutingReportsTheActualDestinationAndUnavailableHostOutcome(bool unnamed)
    {
        var f = await Fixture.CreateInitializedAsync();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var target = new SessionWorkspaceEntry(new(request.SessionId, new(1), true), new(request.SessionId, new(1), new("Reviewed destination")));
        if (unnamed) { target = target with { Metadata = null }; }
        var message = new ConversationMessage(request, new(1), 1, 2, DateTimeOffset.UnixEpoch, "original",
            ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel);
        f.ViewModel.BindSessionRouting((_, _, _) => Task.FromResult<SessionRoutingReceipt?>(
            new(target, new(message, new(request, new(2), message.ExpectedTaskState)))));
        await f.RunAsync("original");
        f.ViewModel.ResponseTitle.Should().Be(unnamed ? "Request destination: Unnamed conversation." : "Request destination: Reviewed destination.");
        f.ViewModel.ResponseBody.Should().Contain("HOST Unavailable / UnregisteredModel");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task EmptyActivatedRequestCannotOpenRoutingOrInvokeInference()
    {
        var f = await Fixture.CreateInitializedAsync();
        var calls = 0;
        f.ViewModel.BindSessionRouting((_, _, _) => { calls++; return Task.FromResult<SessionRoutingReceipt?>(null); });
        await f.RunAsync(f.ViewModel.AssistantName);
        f.ViewModel.ResponseTitle.Should().Be("No request was heard.");
        calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task QualifiedRoutedAnswerUsesTheSharedBriefAndDetailsPresentation(bool details, bool linkOnly)
    {
        await using var settings = new DetailPresentationConfigurationServiceTests.Fixture();
        if (linkOnly) { settings.Preferences.Value = DetailPresentationMode.LinkOnly; }
        var f = new Fixture(detailPresentationConfiguration: settings.Service);
        using var main = f.ViewModel;
        await main.InitializeAsync();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var target = new SessionWorkspaceEntry(new(request.SessionId, new(1), true), new(request.SessionId, new(1), new("Reviewed destination")));
        var body = details ? "Brief response:\nExact brief.\n\nDetailed response:\nMore exact retained answer details." : "Exact short answer.";
        var response = new ConversationResponse(new(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate), body);
        response.Validate();
        var message = new ConversationMessage(request, new(1), 1, 2, DateTimeOffset.UnixEpoch, "original",
            ModelTurnOutcome.Succeeded, ModelTurnReason.None) { AssistantResponse = response };
        f.ViewModel.BindSessionRouting((_, _, _) => Task.FromResult<SessionRoutingReceipt?>(
            new(target, new(message, new(request, new(3), message.ExpectedTaskState)))));
        await f.RunAsync("original");
        f.ViewModel.ResponseBody.Should().Contain(details ? "Exact brief." : body);
        var offers = details && !linkOnly;
        f.ViewModel.IsDetailsOfferPending.Should().Be(offers);
        f.ViewModel.ResponseBody.Split(AnswerPresentation.Offer, StringSplitOptions.None)
            .Should().HaveCount(offers ? 2 : 1);
        var presenter = new ResponsePresenter(main);
        main.BindCurrentResponsePresentation(presenter);
        await main.OpenCurrentResponseDetailsAsync();
        presenter.Content!.Source.Should().Be(body);
        settings.Preferences.Writes.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UncertainRoutingPreservesExactCreationAndSubmissionInspectionIdentity(bool fresh)
    {
        var f = await Fixture.CreateInitializedAsync();
        var creation = HostRequest.Create(RequestOrigin.LocalUi);
        var submitted = HostRequest.Create(RequestOrigin.LocalUi);
        var failure = new ConversationSubmissionUnconfirmedException(submitted, new IOException("unconfirmed"));
        f.ViewModel.BindSessionRouting((_, _, _) => throw (fresh ? new SessionRoutingUnconfirmedException(creation, failure) : failure));
        await f.RunAsync("original");
        f.ViewModel.ResponseTitle.Should().Be("Session routing stopped.");
        f.ViewModel.ResponseBody.Should().Contain(submitted.RequestId.Value.ToString("D"));
        if (fresh) { f.ViewModel.ResponseBody.Should().Contain(creation.RequestId.Value.ToString("D")); }
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("name")]
    public async Task OriginalRoutingSourceIsNotRenewedAcrossAwaitedNativeReview(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.BindSessionRouting(async (_, _, admitted) =>
        {
            admitted().Should().BeTrue();
            if (change is "edit") { f.ViewModel.CommandText = "different input"; }
            else { await f.ViewModel.SetAssistantNameAsync("Nova"); }
            admitted().Should().BeFalse();
            return null;
        });
        await f.RunAsync("original");
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task PendingQuestionOwnsInputBeforeGeneralRouting()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "Ready");
        f.Reasoner.Question = new("Which choice?", ["One", "Two"]);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("please choose this work");
        await f.ViewModel.ActiveReasoningTask!;
        var calls = 0;
        f.ViewModel.BindSessionRouting((_, _, _) => { calls++; return Task.FromResult<SessionRoutingReceipt?>(null); });
        await f.RunAsync("Kora, option one");
        calls.Should().Be(0);
    }
}
