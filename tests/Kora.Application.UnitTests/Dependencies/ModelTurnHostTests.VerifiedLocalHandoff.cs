using System.Security.Cryptography;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Interaction;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Providers;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(ModelHandoffDecision.Approve, true)]
    [InlineData(ModelHandoffDecision.Approve, false)]
    [InlineData(ModelHandoffDecision.Decline, false)]
    [InlineData(ModelHandoffDecision.Cancel, false)]
    public async Task VerifiedLocalOperationUsesActualRuntimeJoinAndNativeReviewBeforeOneIndependentHostedSend(
        ModelHandoffDecision decision, bool approveEgress)
    {
        using var f = new Fixture();
        var (host, workflow, operation, runtime, presented) = f.VerifiedOperation();
        var originalSession = f.Session;
        var running = operation.RunAsync(new(f.Session, null), "exact local original", static () => true, Token);
        await presented.Task.WaitAsync(Token);
        var source = new ModelHandoffPresentation(workflow);
        var offer = (await source.ReadPending(RequestOrigin.LocalUi, static () => true, Token)).Should().ContainSingle().Subject;
        f.Time.ExpectedDeadline = HostedTurnAuthorization.MaximumDuration;
        offer.LocalUnavailable!.IsCurrent.Should().BeTrue();
        offer.HostedProfile.Should().Be(Profile(ProviderService.Copilot));
        offer.Destination.Provider.Should().Be(ModelProviderIdentity.Copilot);
        offer.LocalUnavailable.Result.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        f.Adapter.Calls.Should().Be(1);
        f.HandoffCommits.Should().Be(0);
        runtime.Transmissions.Should().Be(0);
        await using var review = source.Open(offer, RequestOrigin.LocalUi, static () => true)!;
        await review.Refresh(Token);
        review.Preview.Should().Contain("exact local original");
        f.HandoffReviews.Offered += exact =>
        {
            runtime.Transmissions.Should().Be(0);
            using var presentation = exact.Presentation();
            f.HandoffReviews.Confirm(exact, approveEgress, RequestOrigin.LocalUi).Should().BeTrue();
        };
        await review.Decide(offer, decision, RequestOrigin.LocalUi, Token);
        var completed = await running;
        completed.Message.Request.SessionId.Should().Be(originalSession.SessionId);
        host.IsQuiescent.Should().BeTrue();
        if (decision == ModelHandoffDecision.Approve)
        {
            f.HandoffCommits.Should().Be(1);
            completed.Message.Handoff!.ExpiresAt.Should().Be(offer.Context.ExpiresAt);
            completed.Message.Outcome.Should().Be(approveEgress ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.DeniedEgress);
            completed.Message.AssistantResponse?.Body.Should().Be(approveEgress ? "actual-boundary-answer" : null);
            completed.Message.Request.Should().NotBe(offer.Context.Request);
        }
        else
        {
            f.HandoffCommits.Should().Be(0);
            completed.Message.Request.Should().Be(offer.Context.Request);
            completed.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
            completed.Message.AssistantResponse.Should().BeNull();
        }
        runtime.Transmissions.Should().Be(decision == ModelHandoffDecision.Approve && approveEgress ? 1 : 0);
        review.Completion.Should().Be(new ModelHandoffCompletion(completed.Message.Outcome!.Value,
            completed.Message.Reason!.Value, completed.Message.HasAssistantResponse));
        f.Messages.Should().NotContain(item => item.Contains("exact local original", StringComparison.Ordinal)
            || item.Contains("actual-boundary-answer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARealLocalFailureHasNoObservationUntilItsExactIssuedLeaseJoins()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        var context = f.Context();
        var turn = (await f.PolicyHost.AdmitAsync(policy, ModelTurnChoice.Local, context, null, Token)).Turn!;
        var eligible = true;
        await f.PolicyHost.ReserveConversationResourcesAsync(turn, () => eligible, Token);
        f.PolicyHost.ObserveJoinedLocalUnavailable(turn).Should().BeNull();
        f.Adapter.Reply = new(ModelTurnOutcome.Unavailable, policy.Local);
        (await f.PolicyHost.RunAsync(turn, Token)).Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        await f.PolicyHost.JoinConversationResourcesAsync(turn, Token);
        var observation = f.PolicyHost.ObserveJoinedLocalUnavailable(turn)!;
        observation.IsCurrent.Should().BeTrue();
        observation.Context.Should().BeSameAs(context);
        observation.Result.Should().BeSameAs(turn.TerminalResult);
        f.PolicyHost.ObserveJoinedLocalUnavailable(turn).Should().BeSameAs(observation);
        eligible = false;
        f.PolicyHost.ObserveJoinedLocalUnavailable(turn).Should().BeNull();
        eligible = true;
        f.ControlRevision++;
        f.PolicyHost.ObserveJoinedLocalUnavailable(turn).Should().BeNull();
        observation.IsCurrent.Should().BeFalse();
    }

    [Theory]
    [InlineData("request")]
    [InlineData("policy")]
    [InlineData("input")]
    [InlineData("created")]
    [InlineData("expiry")]
    [InlineData("tools")]
    [InlineData("constraints")]
    [InlineData("evidence")]
    public async Task IssuedActualLocalObservationCannotAdmitChangedOriginalContext(string change)
    {
        using var f = new Fixture();
        var (_, workflow, operation, _, presented) = f.VerifiedOperation();
        var running = operation.RunAsync(new(f.Session, null), "exact local original", static () => true, Token);
        await presented.Task.WaitAsync(Token);
        var source = new ModelHandoffPresentation(workflow);
        var offer = (await source.ReadPending(RequestOrigin.LocalUi, static () => true, Token)).Single();
        var original = offer.Context;
        var request = change is "request" ? HostRequest.Create(RequestOrigin.LocalUi) : original.Request;
        var context = new ModelContextEnvelope(request,
            change is "policy" ? "changed policy" : original.SystemPolicy,
            change is "input" ? "changed original" : original.UserRequest,
            change is "evidence" ? [new(new(Guid.NewGuid()), request, new(1), "unreviewed", ModelEvidenceDisclosure.HostedEligible)] : [],
            change is "created" ? original.CreatedAt.AddTicks(-1) : original.CreatedAt,
            change is "expiry" ? original.ExpiresAt.AddTicks(1) : original.ExpiresAt,
            change is "tools" ? ModelToolMode.ReadOnly : original.ToolMode,
            change is "constraints" ? new(1, [], 0, 1, 32768, 4096, 1000) : original.Constraints);
        offer.LocalUnavailable!.Admits(context).Should().BeFalse();
        await using var review = source.Open(offer, RequestOrigin.LocalUi, static () => true)!;
        await review.Refresh(Token);
        await review.Decide(offer, ModelHandoffDecision.Decline, RequestOrigin.LocalUi, Token);
        (await running).Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        f.HandoffCommits.Should().Be(0);
    }

    private sealed partial class Fixture
    {
        internal HostedTransmissionReviewService HandoffReviews { get; private set; } = null!;
        internal int HandoffCommits { get; private set; }
        internal ConversationHandoffCommit? LastHandoffCommit { get; private set; }
        internal Action? BeforeHandoffCommit { get; set; }
        internal Action? AfterHandoffCommit { get; set; }
        internal Func<ConversationHandoffCommit, Task>? AfterHandoffCommitAsync { get; set; }
        internal Kora.Application.Hosting.SessionConversationService VerifiedConversation { get; private set; } = null!;
        private readonly Dictionary<HostId<TaskIdentity>, HostTaskObservation> handoffSources = [];
        internal void DetachExpectedFailedHost(ModelTurnHost host) => ownedHosts.Remove(host).Should().BeTrue();

        internal (ModelTurnHost Host, ModelProviderHandoffWorkflow Workflow, VerifiedLocalHandoffOperation Operation,
            HostedRuntime Runtime, TaskCompletionSource Presented) VerifiedOperation(
                bool profileConfiguration = true, bool sessionStore = true, bool composeReviews = true,
                IModelProviderRegistrationSource? registrationSource = null)
        {
            ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
            ProfilePreferences.Value = new(ProviderService.Local, [Profile(), Profile(ProviderService.Copilot)]);
            ProviderPreferences.Mode = ModelProviderMode.LocalFirst;
            Adapter.Reply = new(ModelTurnOutcome.Unavailable, ModelProviderSelection.OllamaCandidate);
            var runtime = new HostedRuntime();
            HandoffReviews = new(this, Time);
            var copilot = ModelProviderSelection.OllamaCandidate with { Provider = ModelProviderIdentity.Copilot };
            var host = new ModelTurnHost(this, this, this, registry, this, this, Time,
                [new(ModelProviderSelection.OllamaCandidate, Gates(ModelProviderSelection.OllamaCandidate), Time.Now.AddMinutes(5), Adapter),
                    new(copilot, Gates(copilot), Time.Now.AddMinutes(5), new CopilotModelTurnAdapter(runtime, HandoffReviews))],
                ProviderConfiguration, profileConfiguration ? ProfileConfiguration : null, sessionStore ? this : null,
                registrationSource, composeReviews ? HandoffReviews : null);
            ownedHosts.Add(host);
            var workflow = new ModelProviderHandoffWorkflow(host, new(Questions, Time), this, this, new(Questions, Time), this);
            var service = ConversationService(host, execution: host);
            VerifiedConversation = service;
            var operation = new VerifiedLocalHandoffOperation(workflow, host, service, this, this, Time);
            var presented = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            operation.ReviewRequested += () => presented.TrySetResult();
            return (host, workflow, operation, runtime, presented);
        }

        public async ValueTask<HostQuestionRecord> CreateVerifiedLocalHandoffAsync(LocalUnavailableObservation observation,
            ModelContextEnvelope context, ProviderProfileTuple profile, ModelProviderSelection destination,
            SessionProviderProfile providerState, Func<bool> admitted, CancellationToken token)
        {
            admitted().Should().BeTrue();
            observation.IsCurrent.Should().BeTrue();
            observation.Admits(context).Should().BeTrue();
            Questions.Change(snapshot => snapshot with { Intent = Task!.Task, Session = Session });
            return (await new HostQuestionService(Questions, Time).CreateAsync(context.Request,
                new("Review exact original local context and Copilot destination.", QuestionKind.SingleChoice,
                    [new("approve", "Approve exact envelope"), new("decline", "Decline")],
                    purpose: "provider-handoff", sourceId: "host"), context.ExpiresAt, token)).Question!;
        }

        public async ValueTask<ConversationHandoffCommit> CommitVerifiedLocalHandoffAsync(LocalUnavailableObservation observation,
            HostQuestionRecord review, ModelContextEnvelope context, ModelContextEnvelope successor,
            ProviderProfileTuple profile, ModelProviderSelection destination, SessionProviderProfile providerState,
            Func<bool> admitted, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            BeforeHandoffCommit?.Invoke();
            admitted().Should().BeTrue();
            observation.IsCurrent.Should().BeTrue();
            observation.Admits(context).Should().BeTrue();
            Questions.Snapshot.Questions.Single(item => item.Key == review.Key.Next()).Status.Should().Be(QuestionStatus.Answered);
            var message = Retained! with { Revision = 3, Outcome = ModelTurnOutcome.Unavailable, Reason = observation.Result.Reason };
            var terminal = Task! with { Task = Task.Task.Next(HostTaskState.Unavailable), CurrentSource = false };
            HistoryMessages[message.Request.RequestId] = message;
            handoffSources[terminal.Task.Request.TaskId] = terminal;
            var source = new ConversationSubmissionReceipt(message, terminal.Task);
            Request = successor.Request;
            Task = new(new(Request, new(1), HostTaskState.IntentRecorded), Session.Generation, "host", true, null);
            Retained = new(Request, Session.Generation, 2, 1, Time.Now, successor.UserRequest, null, null)
            {
                Handoff = new(observation.Id, message.Execution!, observation.TaskRevision, review.Key,
                    context.CreatedAt, context.ExpiresAt, Convert.ToHexStringLower(SHA256.HashData(successor.Serialize())), profile, destination),
            };
            HandoffCommits++;
            LastHandoffCommit = new(source, new(Retained, Task.Task));
            AfterHandoffCommit?.Invoke();
            if (AfterHandoffCommitAsync is { } after) { await after(LastHandoffCommit); }
            return LastHandoffCommit;
        }
    }
}
