using System.Diagnostics;
using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Dependencies;
using Kora.Application.Hosting.SessionRouting;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Hosting;

[Collection("Host tracing")]
public sealed class SessionConversationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
    };
    public SessionConversationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("success")]
    [InlineData("drift")]
    [InlineData("audit")]
    [InlineData("ordinary")]
    public async Task PermissionBoundEntryRetainsThroughOrdinaryLifecycleOrFailsWithoutRetention(string boundary)
    {
        var f = new PermissionFixture();
        await using var conversation = f.Conversation();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request);
        f.Drift = boundary is "drift";
        f.AuditFailure = boundary is "audit";
        if (boundary is "ordinary")
        { await conversation.SubmitAsync(f.Entry, "original", static () => true, Token, origin: RequestOrigin.ActivatedVoice); }
        else
        {
            var operation = () => conversation.SubmitPermissionBoundAsync(f.Entry, "original", f.Policy,
                static () => true, Token);
            if (boundary is "drift") { await operation.Should().ThrowAsync<ISessionConversationStore.PermissionChangedException>(); }
            else if (boundary is "audit") { await operation.Should().ThrowAsync<ConversationSubmissionUnconfirmedException>(); }
            else { await operation(); }
        }
        f.Retains.Should().Be(boundary is "success" or "ordinary" ? 1 : 0);
        f.Admissions.Should().Be(f.Retains);
        if (boundary is "success")
        {
            f.Approval!.Request.Should().Be(f.Message!.Request);
            f.Approval.Policy.Should().Be(f.Policy);
            f.Approval.Effect.Should().Be(Kora.Core.Authorization.HostOperationEffect.BoundedWrite);
            f.Message.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        }
        else { f.Approval.Should().BeNull(); }
    }

    [Theory]
    [InlineData("typed")]
    [InlineData("review-all")]
    [InlineData("foreign")]
    public async Task AutomaticSubmitRejectsWrongChannelOrDestinationPolicy(string boundary)
    {
        var f = new PermissionFixture();
        await using var conversation = f.Conversation();
        using var root = HostActivity.BeginRoot(HostRequest.Create(boundary is "typed" ? RequestOrigin.LocalUi : RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request);
        var policy = boundary is "review-all" ? f.Policy with { Level = SessionPermissionLevel.ReviewAll }
            : boundary is "foreign" ? f.Policy with { SessionId = new(Guid.NewGuid()) } : f.Policy;
        await FluentActions.Awaiting(() => conversation.SubmitPermissionBoundAsync(f.Entry, "original", policy,
            static () => true, Token)).Should().ThrowAsync<Exception>();
        f.Retains.Should().Be(0);
    }

    [Theory]
    [InlineData("foreground")]
    [InlineData("last-selected")]
    [InlineData("none")]
    [InlineData("typed")]
    [InlineData("review-all")]
    [InlineData("drift")]
    public async Task RoutingAutomaticReadbackRequiresForegroundAndReturnsToReviewOnPolicyDrift(string boundary)
    {
        var f = new PermissionFixture();
        await using var conversation = f.Conversation();
        var routing = new SessionRoutingService(f.Workspace, conversation, f, TimeProvider.System);
        using var root = HostActivity.BeginRoot(HostRequest.Create(boundary is "typed" ? RequestOrigin.LocalUi : RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request);
        var target = new ForegroundConversationTarget(f.Entry, static () => true);
        var review = await routing.PrepareAsync("original", root.Request.Origin, static () => true, Token,
            boundary is "foreground" or "drift" or "review-all" or "typed" ? target : null,
            boundary is "last-selected" ? target : null);
        if (boundary is "review-all") { f.Policy = f.Policy with { Level = SessionPermissionLevel.ReviewAll }; }
        var policy = await routing.ReadReadbackPermissionAsync(review, Token);
        if (boundary is "foreground" or "drift")
        {
            policy.Should().Be(f.Policy);
            f.Drift = boundary is "drift";
            var receipt = await routing.SubmitReadbackAsync(review, policy!, Token);
            if (boundary is "foreground") { receipt!.Submission.Message.Body.Should().Be("original"); }
            else
            {
                receipt.Should().BeNull();
                f.Retains.Should().Be(0);
                routing.IsCurrent(review).Should().BeTrue();
                var selection = await routing.SelectAsync(review, f.Entry.Authority.SessionId, Token);
                await routing.SubmitAsync(selection, Token);
            }
        }
        else
        {
            policy.Should().BeNull();
            if (boundary is not "review-all")
            {
                await FluentActions.Awaiting(() => routing.SubmitReadbackAsync(review, f.Policy, Token))
                    .Should().ThrowAsync<InvalidOperationException>();
            }
        }
    }

    [Theory]
    [InlineData("named")]
    [InlineData("foreground")]
    [InlineData("drift")]
    [InlineData("typed")]
    [InlineData("explicit-id")]
    public async Task NamedOrForegroundOriginalReviewCanUseAtomicReadbackAndDriftRestoresNormalConfirmation(string boundary)
    {
        var f = new PermissionFixture();
        await using var conversation = f.Conversation();
        var requests = new SessionRequestCommandService(f.Workspace, conversation, f, TimeProvider.System);
        var origin = boundary is "typed" ? RequestOrigin.LocalUi : RequestOrigin.ActivatedVoice;
        using var root = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        var command = new SessionCommand(SessionCommandOperation.RequestReview,
            boundary is "foreground" or "named" or "drift" ? null : f.Entry.Authority.SessionId.Value)
        {
            RequestBody = "original", Generation = 1, MetadataRevision = 1,
            RequestForegroundTarget = boundary is "foreground",
            RequestTargetName = boundary is "named" or "drift" ? new SessionName("Build") : null
        };
        var review = (await requests.ExecuteAsync(command, origin, static () => true, Token,
            new(f.Entry, static () => true))).RequestReview!;
        var policy = await requests.ReadReadbackPermissionAsync(review, Token);
        if (boundary is "typed" or "explicit-id") { policy.Should().BeNull(); return; }
        policy.Should().Be(f.Policy);
        f.Drift = boundary is "drift";
        var confirm = new SessionCommand(SessionCommandOperation.RequestConfirm, f.Entry.Authority.SessionId.Value)
        { RequestReviewId = review.Id };
        var result = await requests.ExecuteAsync(confirm, origin, static () => true, Token, readbackPermission: policy);
        if (boundary is "drift")
        {
            result.RequestReview.Should().BeSameAs(review);
            result.RequestSubmission.Should().BeNull();
            f.Retains.Should().Be(0);
            (await requests.ExecuteAsync(confirm, origin, static () => true, Token)).RequestSubmission.Should().NotBeNull();
        }
        else { result.RequestSubmission!.Message.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice); }
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("source")]
    [InlineData("control")]
    [InlineData("expiry")]
    [InlineData("drift-pending")]
    [InlineData("drift-source")]
    public async Task PermissionBoundNamedReadbackRejectsWithdrawnPendingAuthority(string boundary)
    {
        var f = new PermissionFixture();
        var live = true;
        var now = DateTimeOffset.UtcNow;
        await using var conversation = f.Conversation();
        var requests = new SessionRequestCommandService(f.Workspace, conversation, f, new ReadbackTime(() => now));
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request);
        var review = (await requests.ExecuteAsync(new(SessionCommandOperation.RequestReview)
        { RequestBody = "original", RequestTargetName = new("Build") }, RequestOrigin.ActivatedVoice,
            () => live, Token)).RequestReview!;
        void Withdraw()
        {
            if (boundary is "pending" or "drift-pending") { requests.Retire(); }
            if (boundary is "source" or "drift-source") { live = false; }
            if (boundary is "control") { f.ControlVersion++; }
            if (boundary is "expiry") { now = review.ExpiresAt; }
        }
        if (boundary.StartsWith("drift-", StringComparison.Ordinal))
        {
            f.BeforeBound = Withdraw;
            f.Drift = true;
            await FluentActions.Awaiting(() => requests.ExecuteAsync(new(SessionCommandOperation.RequestConfirm,
                f.Entry.Authority.SessionId.Value) { RequestReviewId = review.Id }, RequestOrigin.ActivatedVoice,
                () => live, Token, readbackPermission: f.Policy)).Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            f.AfterPermissionRead = Withdraw;
            await FluentActions.Awaiting(() => requests.ReadReadbackPermissionAsync(review, Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        f.Retains.Should().Be(0);
        f.Admissions.Should().Be(0);
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("typed")]
    [InlineData("missing")]
    [InlineData("explicit-id")]
    public async Task PermissionBoundConfirmationCannotPromoteOtherCommandsOrSources(string boundary)
    {
        var f = new PermissionFixture();
        var origin = boundary is "typed" ? RequestOrigin.LocalUi : RequestOrigin.ActivatedVoice;
        await using var conversation = f.Conversation();
        var requests = new SessionRequestCommandService(f.Workspace, conversation, f, TimeProvider.System);
        using var root = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        if (boundary is not "missing")
        {
            await requests.ExecuteAsync(new(SessionCommandOperation.RequestReview,
                boundary is "explicit-id" ? f.Entry.Authority.SessionId.Value : null)
            {
                RequestBody = "original", RequestTargetName = boundary is "explicit-id" ? null : new("Build"),
                Generation = 1, MetadataRevision = 1
            }, origin, static () => true, Token);
        }
        await FluentActions.Awaiting(() => requests.ExecuteAsync(new(boundary is "operation"
            ? SessionCommandOperation.RequestCancel : SessionCommandOperation.RequestConfirm, f.Entry.Authority.SessionId.Value),
            origin, static () => true, Token, readbackPermission: f.Policy)).Should().ThrowAsync<InvalidOperationException>();
        f.Retains.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PermissionBoundRoutingRejectsRetirementDuringPolicyReadOrDriftFallback(bool duringCommit)
    {
        var f = new PermissionFixture();
        await using var conversation = f.Conversation();
        var routing = new SessionRoutingService(f.Workspace, conversation, f, TimeProvider.System);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request);
        var review = await routing.PrepareAsync("original", RequestOrigin.ActivatedVoice, static () => true,
            Token, new(f.Entry, static () => true));
        if (duringCommit)
        {
            f.BeforeBound = routing.Retire;
            f.Drift = true;
            await FluentActions.Awaiting(() => routing.SubmitReadbackAsync(review, f.Policy, Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            f.AfterPermissionRead = routing.Retire;
            await FluentActions.Awaiting(() => routing.ReadReadbackPermissionAsync(review, Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        f.Retains.Should().Be(0);
        f.Admissions.Should().Be(0);
    }

    private sealed class ReadbackTime(Func<DateTimeOffset> now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PermissionBoundSubmissionUsesExistingFifoAndCancellationDoesNotReplayAcceptedText(bool queued)
    {
        var f = new PermissionFixture
        {
            AdmissionGate = queued ? null : new(TaskCreationOptions.RunContinuationsAsynchronously),
            ExecutionGate = queued ? new(TaskCreationOptions.RunContinuationsAsynchronously) : null
        };
        await using var conversation = f.Conversation();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request);
        var first = conversation.SubmitPermissionBoundAsync(f.Entry, "first", f.Policy, static () => true, caller.Token);
        var entered = queued ? f.ExecutionEntered.Task : f.AdmissionEntered.Task;
        await Task.WhenAny(entered, first).WaitAsync(TimeSpan.FromSeconds(15), Token);
        if (first.IsCompleted) { await first; }
        await entered.WaitAsync(TimeSpan.FromSeconds(15), Token);
        if (queued)
        {
            var next = await conversation.SubmitPermissionBoundAsync(f.Entry, "next", f.Policy, static () => true, Token);
            next.Message.Body.Should().Be("next");
            next.Message.Outcome.Should().BeNull();
            f.Executions.Should().Be(1, "the original provider operation owns FIFO dispatch");
        }
        caller.Cancel();
        await FluentActions.Awaiting(() => first).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        await conversation.DisposeAsync();
        f.Retains.Should().Be(queued ? 2 : 1);
        f.Executions.Should().Be(queued ? 1 : 0);
    }

    private sealed class PermissionFixture : AudioControlTestStore, ISessionWorkspaceStore, ISessionWorkspaceAccess, ISessionConversationStore,
        ISessionPermissionStore, IModelTurnAdmission, IModelTurnExecution
    {
        internal SessionWorkspaceEntry Entry { get; }
        internal SessionPermissionPolicy Policy { get; set; }
        internal SessionWorkspaceService Workspace { get; }
        internal ConversationMessage? Message { get; private set; }
        internal AutomaticApprovalAudit? Approval { get; private set; }
        internal bool Drift { get; set; }
        internal bool AuditFailure { get; set; }
        internal int Retains { get; private set; }
        internal int Admissions { get; private set; }
        internal TaskCompletionSource? AdmissionGate { get; set; }
        internal TaskCompletionSource AdmissionEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource? ExecutionGate { get; set; }
        internal TaskCompletionSource ExecutionEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Executions { get; private set; }
        private readonly Dictionary<HostId<RequestIdentity>, ConversationMessage> messages = [];
        public bool CanInspect => true;
        public bool CanControl => true;
        internal long ControlVersion { get; set; } = 1;
        internal Action? AfterPermissionRead { get; set; }
        internal Action? BeforeBound { get; set; }
        public long ControlRevision => ControlVersion;
        public bool IsQuiescent => true;
        internal PermissionFixture()
        {
            var id = new HostId<SessionIdentity>(Guid.NewGuid());
            Entry = new(new(id, new(1), true), new(id, new(1), new("Build")));
            Policy = new(id, new(1), new(1), SessionPermissionLevel.ApproveAll);
            Workspace = new(this, new(this), this, NullLogger<SessionWorkspaceService>.Instance);
        }
        internal SessionConversationService Conversation() =>
            new(this, this, Workspace, this, this, TimeProvider.System, NullLogger<SessionConversationService>.Instance,
                execution: this);
        public override ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken token) =>
            ValueTask.FromResult(Entry);
        public override ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task,
            CancellationToken token) => ValueTask.FromResult<HostTaskObservation?>(
                new(Tasks.Last(item => item.Request.TaskId == task), new(1), "host", true, null));
        ValueTask<SessionPage<SessionWorkspaceEntry>> ISessionWorkspaceStore.ReadMetadataPageAsync(Guid? after, int limit,
            CancellationToken token) => ValueTask.FromResult(new SessionPage<SessionWorkspaceEntry>([Entry], null));
        public ValueTask<SessionPermissionPolicy> ReadSessionPermissionAsync(HostId<SessionIdentity> session,
            CancellationToken token)
        {
            AfterPermissionRead?.Invoke();
            return ValueTask.FromResult(Policy);
        }
        public ValueTask<SessionPermissionPolicy> SetSessionPermissionAsync(HostRequest request, SessionPermissionPolicy expected,
            SessionPermissionLevel desired, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationSubmissionReceipt> RetainPermissionBoundConversationAsync(HostRequest request,
            HostRevision generation, long metadata, string body, IReadOnlyList<HostRequest> predecessors,
            SessionPermissionPolicy expected, AutomaticApprovalAudit approval, Func<bool> admitted, CancellationToken token)
        {
            BeforeBound?.Invoke();
            if (Drift) { throw new ISessionConversationStore.PermissionChangedException(); }
            if (AuditFailure) { throw new IOException("Synthetic audit failure"); }
            expected.Should().Be(Policy);
            Approval = approval;
            return RetainConversationMessageAsync(request, generation, metadata, body, admitted, token);
        }
        public ValueTask<ConversationSubmissionReceipt> RetainConversationMessageAsync(HostRequest request,
            HostRevision generation, long metadata, string body, Func<bool> admitted, CancellationToken token)
        {
            admitted().Should().BeTrue();
            Retains++;
            Message = new(request, generation, 1, 1, DateTimeOffset.UtcNow, body, null, null);
            messages.Add(request.RequestId, Message);
            var task = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
            Tasks.Add(task);
            return ValueTask.FromResult(new ConversationSubmissionReceipt(Message, task));
        }
        public ValueTask<ConversationSubmissionReceipt> CompleteConversationSubmissionAsync(HostRequest request,
            ModelTurnOutcome outcome, ModelTurnReason reason, Func<bool> admitted, CancellationToken token)
        {
            Message = messages[request.RequestId] with { Revision = 2, Outcome = outcome, Reason = reason };
            messages[request.RequestId] = Message;
            var completed = Tasks.Last(task => task.Request == request).Next(Message.ExpectedTaskState);
            Tasks.Add(completed);
            return ValueTask.FromResult(new ConversationSubmissionReceipt(Message, completed));
        }
        public ValueTask<ConversationMessage> ReadConversationMessageAsync(HostId<SessionIdentity> session,
            HostId<RequestIdentity> request, CancellationToken token) => ValueTask.FromResult(messages[request]);
        public async Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token)
        {
            Admissions++;
            AdmissionEntered.TrySetResult();
            if (AdmissionGate is { } gate) { await gate.Task.WaitAsync(token); }
            if (ExecutionGate is not null)
            {
                var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), context.Request, ModelProviderSelection.OllamaCandidate);
                var turn = new ModelTurn(Guid.NewGuid(), HostActivity.RequireCurrent(), provenance, context, context.Serialize(),
                    new(1), new(1), ControlVersion,
                    new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.None, DateTimeOffset.MinValue, null));
                return new(new(ModelTurnOutcome.Succeeded, ModelTurnReason.None, provenance), turn);
            }
            return new(new(ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel));
        }
        public ValueTask<ConversationSubmissionReceipt> BeginConversationExecutionAsync(HostRequest request,
            ModelTurnProvenance provenance, long metadataRevision, Func<bool> admitted, CancellationToken token)
        {
            Message = messages[request.RequestId] with
            {
                Version = 2, Revision = 2, Execution = provenance, ExecutionMetadataRevision = metadataRevision
            };
            messages[request.RequestId] = Message;
            var task = Tasks.Last(item => item.Request == request).Next(HostTaskState.DispatchRecorded);
            Tasks.Add(task);
            return ValueTask.FromResult(new ConversationSubmissionReceipt(Message, task));
        }
        public async Task<ModelTurnResult> RunAsync(ModelTurn turn, Func<bool> admitted, CancellationToken token,
            Func<CancellationToken, Task>? validateSource = null)
        {
            Executions++;
            ExecutionEntered.TrySetResult();
            await ExecutionGate!.Task.WaitAsync(token);
            return new(ModelTurnOutcome.Cancelled, ModelTurnReason.CallerCancelled, turn.Provenance);
        }
        public Task WaitForQuiescenceAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask<HostTaskRecord> RecordConversationCredentialRejectionAsync(HostRequest request,
            HostRevision generation, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> ReadConversationDraftAsync(HostId<SessionIdentity> session,
            CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> CommitConversationDraftAsync(HostRequest control,
            ConversationDraftSnapshot expected, string? body, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationPage> ReadConversationPageAsync(HostId<SessionIdentity> session,
            ConversationCursor? cursor, int limit, CancellationToken token) => throw new NotSupportedException();
    }
}
