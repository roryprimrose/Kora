using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;


namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("cancel")]
    [InlineData("expiry")]
    [InlineData("privacy")]
    [InlineData("source")]
    [InlineData("delete-failure")]
    [InlineData("terminal-failure")]
    [InlineData("bad-terminal")]
    [InlineData("join-failure")]
    public async Task StopAndDeleteRetainsPhysicalOwnershipAndTruthfulPartialOutcomes(string change)
    {
        using var f = new Fixture { CompoundStoreEnabled = true };
        var host = ConfigureCancellation(f);
        var execution = change is "join-failure" ? new FailedCompoundJoin(host) : (IModelTurnExecution)host;
        var service = f.ConversationService(host, execution: execution);
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = pending.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var submission = service.SubmitAsync(target, "original", static () => true, Token);
        var review = await service.ReviewStopAndDeleteAsync(target, RequestOrigin.LocalUi, static () => true, Token);
        review.Origin.Should().Be(RequestOrigin.LocalUi);
        review.Phase.Should().Be(SessionStopAndDeletePhase.Running);
        review.CreatedAt.Should().Be(f.Time.Now);
        review.ExpiresAt.Should().BeAfter(review.CreatedAt);
        f.DispositionCommits.Should().Be(0);
        f.CancelAudit.Events.Should().BeEmpty("preview must not stop work");
        var live = true;
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.RunningConversationChanged += value =>
        {
            if (value.Phase == ConversationExecutionPhase.Cancelling) { accepted.TrySetResult(); }
        };
        var confirm = service.ConfirmStopAndDeleteAsync(review, () => live, Token);
        await accepted.Task;
        confirm.IsCompleted.Should().BeFalse();
        f.DispositionCommits.Should().Be(0);
        var duplicate = await service.ConfirmStopAndDeleteAsync(review, static () => true, Token);
        duplicate.Outcome.Should().Be(SessionStopAndDeleteOutcome.NotStopped);
        await service.Invoking(value => value.SubmitAsync(target, "new unreviewed request", static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        if (change is "cancel") { live = false; }
        if (change is "expiry") { f.Time.Now = f.Time.Now.AddMinutes(2); }
        if (change is "privacy") { f.CanControl = false; f.ControlRevision++; }
        if (change is "source") { f.CompoundSource++; }
        if (change is "delete-failure") { f.CompoundDeleteError = new IOException("unconfirmed disposition"); }
        if (change is "terminal-failure") { f.BeforeExecutionComplete = () => throw new IOException("unconfirmed terminal"); }
        if (change is "bad-terminal") { f.BadExecutionReadback = true; }
        pending.SetResult(LateAnswer());
        try { await submission; }
        catch (ConversationSubmissionUnconfirmedException) { }
        var result = await confirm;
        result.Outcome.Should().Be(change switch
        {
            "success" => SessionStopAndDeleteOutcome.Deleted,
            "source" or "delete-failure" => SessionStopAndDeleteOutcome.DeletionUnconfirmed,
            "join-failure" => SessionStopAndDeleteOutcome.StopUnconfirmed,
            _ => SessionStopAndDeleteOutcome.WorkStoppedNotDeleted,
        });
        f.DispositionCommits.Should().Be(change is "success" ? 1 : 0);
        f.Retained!.AssistantResponse.Should().BeNull();
        if (change is "success") { result.Receipt!.Removed.Should().Be(review.Disposition); }
        else { result.Recovery.Should().NotBeNullOrWhiteSpace(); }
        if (change is "cancel" or "expiry")
        {
            live = true;
            var fresh = await service.ReviewStopAndDeleteAsync(target, RequestOrigin.LocalUi, static () => true, Token);
            fresh.Phase.Should().Be(SessionStopAndDeletePhase.WorkStopped);
            fresh.Should().NotBeSameAs(review);
            var retry = await service.ConfirmStopAndDeleteAsync(fresh, static () => true, Token);
            retry.Outcome.Should().Be(SessionStopAndDeleteOutcome.Deleted);
            f.CancelAudit.Events.Should().HaveCount(2, "fresh deletion review must not replay the accepted stop");
        }
    }

    [Theory]
    [InlineData("discard")]
    [InlineData("replaced")]
    [InlineData("privacy")]
    [InlineData("admission")]
    [InlineData("expired")]
    [InlineData("clock-back")]
    [InlineData("caller")]
    [InlineData("changed-source")]
    [InlineData("preview-failure")]
    [InlineData("cancelled-reader")]
    [InlineData("audit-request")]
    [InlineData("audit-terminal")]
    [InlineData("already-terminal")]
    public async Task StopAndDeleteInvalidOrDeclinedConfirmationHasNoStopOrDeletion(string fault)
    {
        using var f = new Fixture { CompoundStoreEnabled = true };
        var host = ConfigureCancellation(f);
        var service = f.ConversationService(host, execution: host);
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = pending.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var submission = service.SubmitAsync(target, "original", static () => true, Token);
        var review = await service.ReviewStopAndDeleteAsync(target, RequestOrigin.LocalUi, static () => true, Token);
        using var caller = new CancellationTokenSource();
        if (fault is "discard") { service.DiscardStopAndDeleteReview(review); service.DiscardStopAndDeleteReview(review); }
        if (fault is "replaced") { await service.ReviewStopAndDeleteAsync(target, RequestOrigin.LocalUi, static () => true, Token); }
        if (fault is "privacy") { f.ControlRevision++; }
        if (fault is "expired") { f.Time.Now = review.ExpiresAt; }
        if (fault is "clock-back") { f.Time.Now = review.CreatedAt.AddTicks(-1); }
        if (fault is "caller") { caller.Cancel(); }
        if (fault is "changed-source") { f.CompoundSource++; }
        if (fault is "preview-failure") { f.CompoundPreviewError = new IOException("source unavailable"); }
        if (fault is "cancelled-reader") { f.CompoundPreviewError = new OperationCanceledException("source read cancelled"); }
        if (fault is "audit-request") { f.CancelAudit.FailAt = 0; }
        if (fault is "audit-terminal") { f.CancelAudit.FailAt = 1; }
        if (fault is "already-terminal") { pending.SetResult(LateAnswer()); await submission; }
        var result = await service.ConfirmStopAndDeleteAsync(review, () => fault is not "admission", caller.Token);
        result.Outcome.Should().Be(SessionStopAndDeleteOutcome.NotStopped);
        result.Receipt.Should().BeNull();
        f.DispositionCommits.Should().Be(0);
        pending.TrySetResult(LateAnswer());
        if (fault is "privacy")
        {
            Func<Task> observe = async () => { await submission; };
            await observe.Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
            f.Retained!.Outcome.Should().BeNull();
        }
        else
        {
            await submission;
            f.Retained!.Outcome.Should().Be(fault is "expired" or "clock-back"
                ? ModelTurnOutcome.Denied : ModelTurnOutcome.Succeeded);
        }
    }

    [Theory]
    [InlineData("voice")]
    [InlineData("model")]
    [InlineData("privacy")]
    [InlineData("admission")]
    [InlineData("no-running")]
    [InlineData("source")]
    [InlineData("lease")]
    [InlineData("expired")]
    [InlineData("clock-back")]
    [InlineData("preview-privacy")]
    [InlineData("preview-admission")]
    [InlineData("preview-caller")]
    [InlineData("preview-replaced")]
    [InlineData("preview-terminal")]
    [InlineData("preview-cancelling")]
    [InlineData("missing-work")]
    public async Task StopAndDeleteReviewRejectsUnknownForeignOrChangedSourceWithoutSideEffects(string fault)
    {
        using var f = new Fixture { CompoundStoreEnabled = true };
        var host = ConfigureCancellation(f);
        var service = f.ConversationService(host, execution: host);
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = pending.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var submission = fault is "no-running" ? null : service.SubmitAsync(target, "original", static () => true, Token);
        var live = true;
        using var caller = new CancellationTokenSource();
        var origin = fault switch
        {
            "voice" => RequestOrigin.ActivatedVoice,
            "model" => RequestOrigin.HostSystem,
            _ => RequestOrigin.LocalUi,
        };
        if (fault is "privacy") { f.CanControl = false; }
        if (fault is "admission") { live = false; }
        if (fault is "source") { f.CompoundWrongSession = true; }
        if (fault is "lease") { f.CompoundWrongLease = true; }
        if (fault is "missing-work") { f.CompoundMissingWork = true; }
        f.AfterCompoundPreview = async () =>
        {
            f.AfterCompoundPreview = null;
            if (fault is "expired") { f.Time.Now = f.Time.Now.AddMinutes(2); }
            if (fault is "clock-back") { f.Time.Now = f.Time.Now.AddDays(-1); }
            if (fault is "preview-privacy") { f.ControlRevision++; }
            if (fault is "preview-admission") { live = false; }
            if (fault is "preview-caller") { caller.Cancel(); }
            if (fault is "preview-replaced") { await service.ReviewStopAndDeleteAsync(target, origin, static () => true, Token); }
            if (fault is "preview-terminal") { pending.SetResult(LateAnswer()); await submission!; }
            if (fault is "preview-cancelling")
            {
                var observed = await service.ReadRunningConversationAsync(target, static () => true, Token);
                await service.CancelRunningConversationAsync(observed!.Target, static () => true, Token);
            }
        };
        await service.Invoking(value => value.ReviewStopAndDeleteAsync(target, origin, () => live, caller.Token))
            .Should().ThrowAsync<Exception>();
        f.DispositionCommits.Should().Be(0);
        if (fault is not "preview-cancelling") { f.CancelAudit.Events.Should().BeEmpty(); }
        pending.TrySetResult(LateAnswer());
        if (submission is not null)
        {
            try { await submission; }
            catch (ConversationSubmissionUnconfirmedException) { }
        }
    }

    [Fact]
    public async Task StopAndDeleteRequiresTheAuthoritativeCompoundReaderRatherThanFallingBackToPlainDelete()
    {
        using var f = new Fixture();
        var proxy = new CompoundReaderUnavailableStore(f);
        var host = ConfigureCancellation(f);
        var workspace = new SessionWorkspaceService(proxy, new(f), f, NullLogger<SessionWorkspaceService>.Instance);
        var service = new SessionConversationService(f, proxy, workspace, f, host, f.Time,
            NullLogger<SessionConversationService>.Instance, host, f.CancelAudit);
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = pending.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var submission = service.SubmitAsync(target, "original", static () => true, Token);
        await service.Invoking(value => value.ReviewStopAndDeleteAsync(target, RequestOrigin.LocalUi, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*reader is unavailable*");
        pending.SetResult(LateAnswer());
        await submission;
        f.CancelAudit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task StopAndDeleteRejectsMalformedInternalReviewBeforeAnyCancellationOrDeletion()
    {
        using var f = new Fixture();
        var service = f.ConversationService(ConfigureCancellation(f));
        var preview = new SessionDispositionPreview(Guid.NewGuid(), new(f.Session, null), "revision", 0, 0, 0, 0);
        var review = new SessionStopAndDeleteReview(preview, RequestOrigin.LocalUi, f.ControlRevision,
            f.Time.Now, f.Time.Now.AddSeconds(10), SessionStopAndDeletePhase.Running);
        await service.Invoking(value => value.ConfirmStopAndDeleteAsync(review, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*original is missing*");
        f.DispositionCommits.Should().Be(0);
    }

    [Fact]
    public async Task StopAndDeleteOverlappingReviewRetiresTheEarlierConfirmationAndCannotAcquireItsHold()
    {
        using var f = new Fixture { CompoundStoreEnabled = true };
        var host = ConfigureCancellation(f);
        var service = f.ConversationService(host, execution: host);
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = pending.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var submission = service.SubmitAsync(target, "original", static () => true, Token);
        var first = await service.ReviewStopAndDeleteAsync(target, RequestOrigin.LocalUi, static () => true, Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.AfterCompoundPreview = async () => { f.AfterCompoundPreview = null; entered.SetResult(); await release.Task; };
        var confirming = service.ConfirmStopAndDeleteAsync(first, static () => true, Token);
        await entered.Task;
        var later = await service.ReviewStopAndDeleteAsync(target, RequestOrigin.LocalUi, static () => true, Token);
        (await service.ConfirmStopAndDeleteAsync(later, static () => true, Token)).Outcome.Should().Be(SessionStopAndDeleteOutcome.NotStopped);
        release.SetResult();
        (await confirming).Outcome.Should().Be(SessionStopAndDeleteOutcome.NotStopped);
        f.CancelAudit.Events.Should().BeEmpty();
        pending.SetResult(LateAnswer());
        await submission;
    }

    [Fact]
    public async Task StopAndDeleteJoinsTheProductionToolLoopAndNoncooperativeToolBeforeDeleting()
    {
        using var f = new Fixture { CompoundStoreEnabled = true };
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var transport = new ToolTransport(PageCall, PageAnswer);
        await using var loop = CreateLoop(f, transport);
        var host = f.ConfiguredProfiles(adapter: loop);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<Kora.Core.Network.WebPageResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var network = new PageNetwork { Pending = release.Task, Entered = entered };
        var approval = new PageApprovalStore(f);
        var service = PageService(f, host, approval, network);
        var target = new SessionWorkspaceEntry(f.Session, null);
        var submission = service.SubmitWebPageAsync(target, "get web page https://example.com/",
            new("https://example.com/"), approval.Present, static () => true, Token);
        await entered.Task;
        var originalRequest = f.Retained!.Request;
        var terminalPublished = false;
        var deleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.RunningConversationChanged += status =>
        {
            if (status.Phase == ConversationExecutionPhase.Terminal) { terminalPublished = true; }
        };
        f.BeforeTaskRead = async () =>
        {
            if (terminalPublished && HostActivity.Current?.Request == originalRequest)
            {
                await deleted.Task;
            }
        };
        var review = await service.ReviewStopAndDeleteAsync(target, RequestOrigin.LocalUi, static () => true, Token);
        var confirming = ConfirmDeletion();
        async Task<SessionStopAndDeleteResult> ConfirmDeletion()
        {
            try { return await service.ConfirmStopAndDeleteAsync(review, static () => true, Token); }
            finally { deleted.TrySetResult(); }
        }
        confirming.IsCompleted.Should().BeFalse("the admitted tool ignores cancellation until its actual transport returns");
        f.DispositionCommits.Should().Be(0);
        using var body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("late tool content"));
        release.SetResult(new(200, new("https://example.com/"), "text/plain", "utf-8", null, null, [], body));
        var receipt = await submission;
        receipt.Message.Request.Should().Be(originalRequest);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        receipt.Message.AssistantResponse.Should().BeNull();
        (await confirming).Outcome.Should().Be(SessionStopAndDeleteOutcome.Deleted);
        body.CanRead.Should().BeFalse("the actual tool response is disposed before deletion");
        f.Retained!.AssistantResponse.Should().BeNull();
        f.DispositionCommits.Should().Be(1);
    }

    [Fact]
    public async Task StopAndDeleteNeverCancelsOrWaitsForAnIndependentConversationResourceOwner()
    {
        using var first = new Fixture { CompoundStoreEnabled = true };
        using var other = new Fixture();
        var firstHost = ConfigureCancellation(first);
        var otherHost = ConfigureCancellation(other);
        var service = first.ConversationService(firstHost, execution: firstHost);
        var otherService = other.ConversationService(otherHost, execution: otherHost);
        var firstRelease = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherRelease = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Adapter.Pending = firstRelease.Task;
        other.Adapter.Pending = otherRelease.Task;
        var target = new SessionWorkspaceEntry(first.Session, null);
        var firstSubmission = service.SubmitAsync(target, "first", static () => true, Token);
        var otherTarget = new SessionWorkspaceEntry(other.Session, null);
        var otherSubmission = otherService.SubmitAsync(otherTarget, "independent", static () => true, Token);
        var review = await service.ReviewStopAndDeleteAsync(target, RequestOrigin.LocalUi, static () => true, Token);
        var confirming = service.ConfirmStopAndDeleteAsync(review, static () => true, Token);
        firstRelease.SetResult(LateAnswer());
        await firstSubmission;
        (await confirming).Outcome.Should().Be(SessionStopAndDeleteOutcome.Deleted);
        otherSubmission.IsCompleted.Should().BeFalse();
        other.CancelAudit.Events.Should().BeEmpty();
        (await otherService.ReadRunningConversationAsync(otherTarget, static () => true, Token))!.Phase
            .Should().Be(ConversationExecutionPhase.Running);
        otherRelease.SetResult(LateAnswer());
        (await otherSubmission).Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
    }

    private sealed class FailedCompoundJoin(IModelTurnExecution host) : IModelTurnExecution
    {
        public Task<ModelTurnResult> RunAsync(ModelTurn turn, Func<bool> admitted, CancellationToken token,
            Func<CancellationToken, Task>? validateSource = null) => host.RunAsync(turn, admitted, token, validateSource);
        public async Task WaitForQuiescenceAsync(CancellationToken cancellationToken)
        {
            await host.WaitForQuiescenceAsync(cancellationToken);
            throw new IOException("physical completion observation unavailable");
        }
    }

    private sealed class CompoundReaderUnavailableStore(Fixture owner) : ISessionWorkspaceStore
    {
        public ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken token) =>
            owner.ReadMetadataAsync(session, token);
        public ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task, CancellationToken token) =>
            owner.ReadTaskAsync(session, task, token);
        public ValueTask<SessionDispositionPreview> PreviewDispositionAsync(HostId<SessionIdentity> session, HostRevision generation, long revision, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionDispositionReceipt> DisposeSessionAsync(HostRequest request, SessionDispositionPreview preview, Func<bool> eligible, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<SessionWorkspaceEntry>> ReadMetadataPageAsync(Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<HostTaskObservation> CancelWaitingTaskAsync(HostRequest request, HostTaskCancellationTarget target, Func<bool> eligible, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> CreateNamedSessionAsync(HostRequest request, SessionName name, Func<bool> eligible, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> RenameSessionAsync(HostRequest request, HostRevision generation, long revision, SessionName name, Func<bool> eligible, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request, HostRevision generation, bool active, Func<bool> eligible, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed partial class Fixture : ISessionStopAndDeleteStore
    {
        internal bool CompoundStoreEnabled { get; set; }
        internal long CompoundSource { get; set; } = 1;
        internal int DispositionCommits { get; set; }
        internal Exception? CompoundDeleteError { get; set; }
        internal Exception? CompoundPreviewError { get; set; }
        internal bool CompoundWrongSession { get; set; }
        internal bool CompoundWrongLease { get; set; }
        internal bool CompoundMissingWork { get; set; }
        internal Func<Task>? AfterCompoundPreview { get; set; }
        public async ValueTask<SessionDispositionPreview> PreviewStopAndDeleteAsync(SessionWorkspaceEntry session,
            ConversationCancellationTarget running, CancellationToken cancellationToken)
        {
            if (!CompoundStoreEnabled) { throw new InvalidOperationException("Compound reader unavailable."); }
            if (CompoundPreviewError is { } error) { throw error; }
            var original = Retained! with { Revision = 2, Outcome = null, Reason = null };
            var preview = new SessionDispositionPreview(Guid.NewGuid(),
                CompoundWrongSession ? session with { Metadata = new(session.Authority.SessionId, new(1), new("changed")) } : session,
                CompoundSource.ToString(System.Globalization.CultureInfo.InvariantCulture), 0, 0, 0, 0)
            {
                StoppedWork = CompoundMissingWork ? null
                    : new(CompoundWrongLease ? running with { LeaseId = Guid.NewGuid() } : running, original),
            };
            if (AfterCompoundPreview is { } after) { await after(); }
            return preview;
        }

        private ValueTask<SessionDispositionReceipt> DisposeCompoundSession(HostRequest request,
            SessionDispositionPreview preview, Func<bool> eligible, CancellationToken token)
        {
            if (!CompoundStoreEnabled) { throw new NotSupportedException(); }
            if (CompoundDeleteError is { } error) { throw error; }
            token.ThrowIfCancellationRequested();
            if (!eligible() || !string.Equals(preview.StoreRevision,
                CompoundSource.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
            { throw new InvalidOperationException("Exact disposition source or control changed."); }
            DispositionCommits++;
            return ValueTask.FromResult(new SessionDispositionReceipt(request.SessionId,
                new(Session.Generation.Value + 1), preview));
        }
    }
}