using System.Text;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    internal static async Task WithPendingClearHost(
        Func<SessionConversationService, SessionWorkspaceService, SessionRequestCommandService, SessionWorkspaceEntry, Task> test)
    {
        await using var p = await PendingClearFixture.Create();
        var workspace = new SessionWorkspaceService(p.F, new(p.F), p.F,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionWorkspaceService>.Instance);
        await test(p.Service, workspace, new(workspace, p.Service, p.F, p.F.Time), p.Target);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task PendingOnlyClearReviewsAllWithoutEffectsThenCancelsExactOriginalsAndKeepsRunningOwner(RequestOrigin origin)
    {
        await using var p = await PendingClearFixture.Create(origin);
        var resources = p.F.Audits.Count(item => item.ActionId is "runtime.resource.acquire");
        var running = await p.Service.ReadRunningConversationAsync(p.Target, () => true, Token);
        var review = await p.Review(origin);
        review.Snapshot.Entries.Select(item => item.Request).Should().Equal(p.B.Receipt.Message.Request, p.C.Receipt.Message.Request);
        review.Snapshot.Entries.Select(item => item.Position).Should().Equal(1, 2);
        review.Snapshot.Origin.Should().Be(origin);
        var json = Encoding.UTF8.GetString(SessionCommandResult.Serialize(new("review", SessionConversationService.PendingClearImpact)
        { PendingClearReview = review.Snapshot }));
        Encoding.UTF8.GetByteCount(json).Should().BeLessThanOrEqualTo(65536);
        json.Should().NotContain("private pending B").And.NotContain("private pending C").And.Contain("Positions");
        p.B.Completion.IsCompleted.Should().BeFalse();
        p.F.CancelAudit.Events.Should().BeEmpty();
        p.Service.DiscardPendingClearReview(review);
        await p.Service.Invoking(service => service.ConfirmPendingClearAsync(review, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        (await p.Service.ReadPendingConversationAsync(p.Target, () => true, Token)).Paused.Should().BeFalse();
        var fresh = await p.Review(origin);
        p.Service.DiscardPendingClearReview(review);
        var result = await p.Service.ConfirmPendingClearAsync(fresh, () => true, Token);
        result.Outcome.Should().Be("reviewed-pending-cancelled");
        result.Outcomes.Should().HaveCount(2).And.OnlyContain(item => item.CancellationRequested && item.Confirmed
            && item.RetainedState == HostTaskState.Cancelled && string.Equals(item.Outcome, "cancelled-retained", StringComparison.Ordinal));
        result.RemainingPending.Should().BeEmpty();
        result.Running.Should().Be(running);
        result.DispatchHeld.Should().BeTrue();
        p.A.Completion.IsCompleted.Should().BeFalse();
        p.F.Adapter.Calls.Should().Be(1);
        p.F.Audits.Count(item => item.ActionId is "runtime.resource.acquire").Should().Be(resources);
        p.F.CancelAudit.Events.Should().OnlyContain(item => string.Equals(item.ActionId, "conversation.pending-clear", StringComparison.Ordinal));
        p.F.PendingRecords[p.B.Receipt.Message.Request.RequestId].Message.Body.Should().Be("private pending B");
        p.F.PendingRecords[p.C.Receipt.Message.Request.RequestId].Message.Execution.Should().BeNull();
        await p.Service.Invoking(service => service.ConfirmPendingClearAsync(fresh, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        p.Provider.TrySetResult(LateAnswer());
        (await p.A.Completion).RequireReceipt().Task.State.Should().Be(HostTaskState.Succeeded);
    }

    [Theory]
    [InlineData("arrival")]
    [InlineData("dispatch")]
    [InlineData("expiry")]
    [InlineData("clock-back")]
    [InlineData("source")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("session")]
    [InlineData("task")]
    [InlineData("cancel")]
    [InlineData("audit")]
    [InlineData("audit-source")]
    [InlineData("read-source")]
    public async Task PendingClearRejectsChangedSnapshotsAndUnknownAuthorityWithoutActiveCancellation(string fault)
    {
        await using var p = await PendingClearFixture.Create();
        var source = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var review = await p.Service.ReviewPendingClearAsync(p.Target, RequestOrigin.LocalUi, () => source, cancellation.Token);
        if (fault is "arrival") { await p.Service.SubmitQueuedAsync(p.Target, "unreviewed arrival", () => true, Token); }
        if (fault is "dispatch")
        {
            p.F.Adapter.Pending = null;
            p.Provider.TrySetResult(LateAnswer());
            await p.A.Completion;
            await p.B.Completion;
            await p.C.Completion;
        }
        if (fault is "expiry") { p.F.Time.Now = review.Snapshot.ExpiresAt; }
        if (fault is "clock-back") { p.F.Time.Now = review.Snapshot.CreatedAt.AddTicks(-1); }
        if (fault is "source") { source = false; }
        if (fault is "private") { p.F.CanControl = false; }
        if (fault is "control") { p.F.ControlRevision++; }
        if (fault is "session") { p.F.Session = p.F.Session with { Generation = new(2) }; }
        if (fault is "task")
        {
            var receipt = p.F.PendingRecords[p.B.Receipt.Message.Request.RequestId];
            p.F.PendingRecords[receipt.Message.Request.RequestId] = receipt with
            { Task = new(receipt.Task.Request, receipt.Task.Revision, HostTaskState.Unknown) };
        }
        if (fault is "cancel") { cancellation.Cancel(); }
        if (fault is "audit") { p.F.CancelAudit.FailAt = 1; }
        if (fault is "audit-source") { p.F.CancelAudit.OnWrite = _ => source = false; }
        if (fault is "read-source") { p.F.OnRead = () => source = false; }
        try
        {
            var result = await p.Service.ConfirmPendingClearAsync(review, () => true, cancellation.Token);
            result.Outcomes.Should().HaveCount(2).And.OnlyContain(item => !item.CancellationRequested && !item.Confirmed
                && string.Equals(item.Outcome, "not-requested", StringComparison.Ordinal));
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
        {
            fault.Should().BeOneOf("arrival", "dispatch", "expiry", "clock-back", "source", "private", "control", "cancel");
        }
        p.F.CancelAudit.Events.Should().NotContain(item => string.Equals(item.ActionId, "conversation.cancel-request", StringComparison.Ordinal));
        if (fault is not "dispatch") { p.A.Completion.IsCompleted.Should().BeFalse(); p.F.Adapter.Calls.Should().Be(1); }
        p.F.CanControl = true;
        p.F.OnRead = null;
        p.F.CancelAudit.OnWrite = null;
        p.F.Session = p.Target.Authority;
    }

    [Theory]
    [InlineData("commit", false)]
    [InlineData("readback", true)]
    [InlineData("source-at-commit", false)]
    public async Task PendingClearEnumeratesPartialDurabilityWithoutRollbackOrReplay(string fault, bool durable)
    {
        await using var p = await PendingClearFixture.Create();
        var source = true;
        var review = await p.Service.ReviewPendingClearAsync(p.Target, RequestOrigin.LocalUi, () => source, Token);
        p.F.BeforePendingTerminal = (request, admitted) =>
        {
            if (request != p.B.Receipt.Message.Request) { return; }
            if (fault is "commit") { throw new IOException("Synthetic terminal commit unavailable"); }
            if (fault is "source-at-commit")
            {
                source = false;
                admitted().Should().BeFalse();
                throw new InvalidOperationException("Synthetic original source changed at canonical commit");
            }
        };
        if (fault is "readback")
        {
            p.F.BeforeTaskRead = () => p.F.PendingRecords[p.B.Receipt.Message.Request.RequestId].Task.IsTerminal
                ? Task.FromException(new IOException("Synthetic readback unavailable")) : Task.CompletedTask;
        }
        var result = await p.Service.ConfirmPendingClearAsync(review, () => true, Token);
        result.Outcome.Should().Be("partial-or-unconfirmed");
        result.Outcomes.Should().HaveCount(2);
        result.Outcomes[0].Outcome.Should().Be("unknown-inspect-exact-original");
        result.Outcomes[0].Confirmed.Should().BeFalse();
        result.Outcomes[0].CancellationRequested.Should().BeTrue();
        p.F.PendingRecords[p.B.Receipt.Message.Request.RequestId].Task.State.Should().Be(
            durable ? HostTaskState.Cancelled : HostTaskState.IntentRecorded);
        result.DispatchHeld.Should().BeTrue();
        p.A.Completion.IsCompleted.Should().BeFalse();
        p.F.Adapter.Calls.Should().Be(1);
        p.F.BeforeTaskRead = null;
        p.F.BeforePendingTerminal = null;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalTypedAndVoicePendingClearCommandExecutesRealBatchInsteadOfPreparingFields(bool voice)
    {
        var origin = voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi;
        await using var p = await PendingClearFixture.Create(origin);
        var workspace = new SessionWorkspaceService(p.F, new(p.F), p.F,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionWorkspaceService>.Instance);
        var commands = new SessionRequestCommandService(workspace, p.Service, p.F, p.F.Time);
        var reviewCommand = SessionCommand.Parse("session pending clear " + p.Target.Authority.SessionId.Value.ToString("D"), "Kora")!;
        var reviewed = await commands.ExecuteAsync(reviewCommand, origin, () => true, Token);
        p.F.Adapter.Calls.Should().Be(1);
        var decision = new SessionCommand(SessionCommandOperation.PendingClearConfirm, p.Target.Authority.SessionId.Value)
        { PendingClearReviewId = reviewed.PendingClearReview!.Id };
        var result = await commands.ExecuteAsync(decision, origin, () => true, Token);
        result.PendingClearResult!.Outcomes.Should().OnlyContain(item => item.Confirmed && item.RetainedState == HostTaskState.Cancelled);
        p.A.Completion.IsCompleted.Should().BeFalse();
    }

    [Theory]
    [InlineData("private")]
    [InlineData("readonly")]
    [InlineData("source-before-read")]
    [InlineData("source-after-read")]
    [InlineData("wrong-session")]
    [InlineData("done")]
    [InlineData("missing-lane")]
    [InlineData("disposed")]
    public async Task PendingClearReviewRequiresCurrentPrivateActiveOriginalAndActualOrdinaryLane(string loss)
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var source = true;
        if (loss is "private") { f.CanControl = false; }
        if (loss is "readonly") { f.InspectOverride = true; f.CanControl = false; }
        if (loss is "source-before-read") { source = false; }
        if (loss is "source-after-read") { f.OnRead = () => source = false; }
        var target = new SessionWorkspaceEntry(f.Session, null);
        if (loss is "wrong-session") { f.Session = f.Session with { Generation = new(2) }; }
        if (loss is "done") { f.Session = f.Session with { IsActive = false }; target = new(f.Session, null); }
        if (loss is "disposed") { await service.DisposeAsync(); }
        await service.Invoking(value => value.ReviewPendingClearAsync(target, RequestOrigin.LocalUi, () => source, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.CancelAudit.Events.Should().BeEmpty();
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task PendingClearReviewRefusesAnIncompleteConcurrentAdmissionWithoutPublishingOrCancellingItsBody()
    {
        await using var p = await PendingClearFixture.Create();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        p.F.BeforeRead = async () =>
        {
            p.F.BeforeRead = null;
            entered.TrySetResult();
            await release.Task.WaitAsync(Token);
        };
        var admission = p.Service.SubmitQueuedAsync(p.Target, "not yet retained", () => true, Token);
        try
        {
            await entered.Task.WaitAsync(Token);
            await p.Service.Invoking(value => value.ReviewPendingClearAsync(p.Target, RequestOrigin.LocalUi, () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        finally { release.TrySetResult(); await admission; }
        p.F.CancelAudit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task PendingClearReviewRefusesCancellationStillJoiningAndEmptyLaneRatherThanClaimingANoOp()
    {
        await using var p = await PendingClearFixture.Create();
        var snapshot = await p.Service.ReadPendingConversationAsync(p.Target, () => true, Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        p.F.BeforeTaskRead = async () =>
        {
            if (p.F.PendingRecords[p.B.Receipt.Message.Request.RequestId].Task.State != HostTaskState.Cancelled) { return; }
            p.F.BeforeTaskRead = null;
            entered.TrySetResult();
            await release.Task.WaitAsync(Token);
        };
        var cancellation = p.Service.CancelPendingConversationAsync(p.Target,
            snapshot.Turns.Single(item => item.Submission == p.B.Receipt), snapshot.Revision, () => true, Token);
        try
        {
            await entered.Task.WaitAsync(Token);
            await p.Service.Invoking(value => value.ReviewPendingClearAsync(p.Target, RequestOrigin.LocalUi, () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        finally { release.TrySetResult(); await cancellation; }
        var review = await p.Review();
        await p.Service.ConfirmPendingClearAsync(review, () => true, Token);
        await p.Service.Invoking(value => value.ReviewPendingClearAsync(p.Target, RequestOrigin.LocalUi, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*No current*");
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("task")]
    [InlineData("readonly")]
    [InlineData("confirmation-source")]
    public async Task PendingClearRetiresAReviewWhoseCanonicalSourceChangesDuringObservation(string loss)
    {
        await using var p = await PendingClearFixture.Create();
        if (loss is "expired") { p.F.Time.Now = p.F.Time.Now.AddSeconds(15); }
        if (loss is "task")
        {
            p.F.BeforeTaskRead = () =>
            {
                p.F.PendingRecords.TryRemove(p.B.Receipt.Message.Request.RequestId, out _).Should().BeTrue();
                p.F.BeforeTaskRead = null;
                return Task.CompletedTask;
            };
        }
        if (loss is "readonly" or "confirmation-source")
        {
            var review = await p.Review();
            if (loss is "readonly") { p.F.InspectOverride = true; p.F.CanControl = false; }
            await p.Service.Invoking(value => value.ConfirmPendingClearAsync(review, () => loss is not "confirmation-source", Token))
                .Should().ThrowAsync<InvalidOperationException>();
            p.F.CanControl = true;
        }
        else
        {
            await p.Service.Invoking(value => value.ReviewPendingClearAsync(p.Target, RequestOrigin.LocalUi, () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        p.F.CancelAudit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingClearTransactionGateCannotDispatchNewWorkOrResumeWhileRunningCompletionKeepsItsOwnOwner(bool completeRunning)
    {
        await using var p = await PendingClearFixture.Create();
        var review = await p.Review();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        p.F.BeforeRead = async () =>
        {
            p.F.BeforeRead = null;
            entered.TrySetResult();
            await release.Task.WaitAsync(Token);
        };
        var clearing = p.Service.ConfirmPendingClearAsync(review, () => true, Token);
        try
        {
            await entered.Task.WaitAsync(Token);
            await p.Service.Invoking(value => value.SubmitQueuedAsync(p.Target, "unreviewed arrival during transaction", () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*pending-only clear*");
            await p.Service.Invoking(value => value.ResumePendingConversationAsync(p.Target, review.Snapshot.LaneRevision, () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>();
            await p.Service.Invoking(value => value.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.StopAllWork,
                RequestOrigin.LocalUi, () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
            (await p.Service.ReadPendingConversationAsync(p.Target, () => true, Token)).Paused.Should().BeFalse();
            if (completeRunning)
            {
                p.F.Adapter.Pending = null;
                p.Provider.TrySetResult(LateAnswer());
                await p.A.Completion;
                p.F.Adapter.Calls.Should().Be(1, "the temporary reason prevents next dispatch, without stopping the running owner");
            }
        }
        finally { release.TrySetResult(); }
        var result = await clearing;
        result.Outcome.Should().Be(completeRunning ? "changed-before-cancellation" : "reviewed-pending-cancelled");
        result.Outcomes.Should().OnlyContain(item => item.CancellationRequested == !completeRunning);
        p.F.CancelAudit.Events.Should().NotContain(item => string.Equals(item.ActionId, "conversation.cancel-request", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PendingClearUsesNamedMetadataAndPreservesAnExistingRecoveryHoldWithoutAnyRunningLease()
    {
        await using var p = await PendingClearFixture.Create(named: true);
        var snapshot = await p.Service.ReadPendingConversationAsync(p.Target, () => true, Token);
        await p.Service.CancelPendingConversationAsync(p.Target, snapshot.Turns.Single(item => item.Submission == p.C.Receipt),
            snapshot.Revision, () => true, Token);
        p.Provider.TrySetResult(LateAnswer());
        await p.A.Completion;
        var review = await p.Review();
        review.Snapshot.Running.Should().BeNull();
        review.Snapshot.MetadataRevision.Should().Be(1);
        review.Snapshot.Count.Should().Be(1);
        var observed = new List<ConversationPendingSnapshot>();
        p.Service.PendingConversationChanged += observed.Add;
        var result = await p.Service.ConfirmPendingClearAsync(review, () => true, Token);
        result.Running.Should().BeNull();
        result.DispatchHeld.Should().BeTrue();
        result.Outcomes.Should().ContainSingle().Which.Confirmed.Should().BeTrue();
        observed.Should().NotBeEmpty();
    }

    [Fact]
    public async Task PendingClearCannotConsumeAnOldReviewAfterTheOwningServiceHasClosedAndJoined()
    {
        await using var p = await PendingClearFixture.Create();
        var review = await p.Review();
        p.Provider.TrySetResult(LateAnswer());
        await p.Service.DisposeAsync();
        await p.Service.Invoking(value => value.ConfirmPendingClearAsync(review, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("replace")]
    [InlineData("identity")]
    [InlineData("session")]
    [InlineData("origin")]
    [InlineData("retire")]
    [InlineData("retire-during-review")]
    [InlineData("source-after-review")]
    [InlineData("partial")]
    [InlineData("missing-id")]
    [InlineData("missing-review")]
    public async Task PendingClearCommandOwnsOneExactReviewAndReportsFailureOrPartialInsteadOfReplaying(string change)
    {
        await using var p = await PendingClearFixture.Create();
        var workspace = new SessionWorkspaceService(p.F, new(p.F), p.F,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionWorkspaceService>.Instance);
        var commands = new SessionRequestCommandService(workspace, p.Service, p.F, p.F.Time);
        var reviewCommand = new SessionCommand(SessionCommandOperation.PendingClearReview, p.Target.Authority.SessionId.Value);
        var calls = 0;
        var previous = await commands.ExecuteAsync(reviewCommand, RequestOrigin.LocalUi, () => { calls++; return true; }, Token);
        if (change is "missing-id")
        {
            await commands.Invoking(value => value.ExecuteAsync(reviewCommand with { SessionId = null }, RequestOrigin.LocalUi, () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>();
            return;
        }
        if (change is "replace")
        { previous = await commands.ExecuteAsync(reviewCommand, RequestOrigin.LocalUi, () => true, Token); }
        if (change is "retire") { commands.Retire(); }
        if (change is "retire-during-review" or "source-after-review")
        {
            commands.Retire();
            var currentCalls = 0;
            p.F.BeforeTaskRead = () =>
            {
                p.F.BeforeTaskRead = null;
                if (change is "retire-during-review") { commands.Retire(); }
                return Task.CompletedTask;
            };
            await commands.Invoking(value => value.ExecuteAsync(reviewCommand, RequestOrigin.LocalUi,
                () => ++currentCalls < calls || change is "retire-during-review", Token))
                .Should().ThrowAsync<InvalidOperationException>();
            return;
        }
        if (change is "partial") { p.F.CompletionFault = "wrong-state"; }
        var command = new SessionCommand(SessionCommandOperation.PendingClearConfirm, change is "session" ? Guid.NewGuid() : p.Target.Authority.SessionId.Value)
        { PendingClearReviewId = change is "missing-review" ? null : change is "identity" ? Guid.NewGuid() : previous.PendingClearReview!.Id };
        if (change is "identity" or "session" or "origin" or "retire" or "missing-review")
        {
            await commands.Invoking(value => value.ExecuteAsync(command,
                change is "origin" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi, () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>();
            p.B.Completion.IsCompleted.Should().BeFalse();
        }
        else
        {
            var result = await commands.ExecuteAsync(command, RequestOrigin.LocalUi, () => true, Token);
            result.Outcome.Should().Be(change is "partial" ? "partial-or-unconfirmed" : "reviewed-pending-cancelled");
        }
    }

    private sealed class PendingClearFixture : IAsyncDisposable
    {
        internal Fixture F { get; } = new() { TrackPendingRecords = true };
        internal SessionConversationService Service { get; private set; } = null!;
        internal SessionWorkspaceEntry Target { get; private set; } = null!;
        internal TaskCompletionSource<ModelProviderReply> Provider { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ConversationQueuedSubmission A { get; private set; } = null!;
        internal ConversationQueuedSubmission B { get; private set; } = null!;
        internal ConversationQueuedSubmission C { get; private set; } = null!;
        internal static async Task<PendingClearFixture> Create(RequestOrigin origin = RequestOrigin.LocalUi, bool named = false)
        {
            var p = new PendingClearFixture();
            var host = ConfigureCancellation(p.F);
            p.Service = p.F.ConversationService(host, execution: host);
            if (named) { p.F.Metadata = new(p.F.Session.SessionId, new(1), new("Ordinary topic")); }
            p.Target = new(p.F.Session, p.F.Metadata);
            p.F.Adapter.Pending = p.Provider.Task;
            p.A = await p.Service.SubmitQueuedAsync(p.Target, "active original", () => true, Token, origin: origin);
            p.B = await p.Service.SubmitQueuedAsync(p.Target, "private pending B", () => true, Token, origin: origin);
            p.C = await p.Service.SubmitQueuedAsync(p.Target, "private pending C", () => true, Token, origin: origin);
            return p;
        }
        internal Task<ConversationPendingClearReview> Review(RequestOrigin origin = RequestOrigin.LocalUi) =>
            Service.ReviewPendingClearAsync(Target, origin, () => true, Token);
        public async ValueTask DisposeAsync()
        {
            Provider.TrySetResult(LateAnswer());
            await Service.DisposeAsync();
            F.Dispose();
        }
    }
}
