using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubmittedActivityDistinguishesOwnedExecutionRetirementCancellationAndPhysicalJoinForBothRoutes(bool direct)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var source = true;
        var observations = new List<SubmittedWorkActivity>();
        service.RunningConversationChanged += _ => observations.Add(service.ReadSubmittedWorkActivity());
        var target = new SessionWorkspaceEntry(f.Session, null);
        var submission = direct
            ? service.SubmitOriginalLocalAsync(target, "private direct original", () => source,
                _ => Task.CompletedTask, [], RequestOrigin.LocalUi, Token)
            : service.SubmitAsync(target, "private ordinary original", () => source, Token);
        try
        {
            observations.Should().Contain(value => value.Waiting == 1);
            var before = (f.RetainCalls, f.Adapter.Calls, f.ExecutionCompletions, f.Audits.Count, f.CancelAudit.Events.Count);
            var busy = service.ReadSubmittedWorkActivity();
            busy.Busy.Should().Be(1);
            busy.Format().Should().Contain("busy 1").And.NotContain("private").And.NotContain(target.Authority.SessionId.Value.ToString("D"));
            (f.RetainCalls, f.Adapter.Calls, f.ExecutionCompletions, f.Audits.Count, f.CancelAudit.Events.Count).Should().Be(before);
            source = false;
            service.ReadSubmittedWorkActivity().Retiring.Should().Be(1, "withdrawn authority is not idle or continued execution");
            source = true;
            var running = (await service.ReadRunningConversationAsync(target, () => true, Token))!;
            await service.CancelRunningConversationAsync(running.Target, () => true, Token);
            var cancelling = service.ReadSubmittedWorkActivity();
            cancelling.Cancelling.Should().Be(1);
            cancelling.Busy.Should().Be(0);
            cancelling.Format().Should().Contain("Cancelling is a request, not a joined stop");
            submission.IsCompleted.Should().BeFalse();
            host.IsQuiescent.Should().BeFalse();
            f.ExecutionCompletions.Should().Be(0);
            provider.SetResult(LateAnswer());
            var receipt = await submission;
            receipt.Message.AssistantResponse.Should().BeNull();
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
            observations.Should().Contain(value => value.Retiring == 1, "terminal publication still has an owned lease");
            var settled = service.ReadSubmittedWorkActivity();
            settled.Busy.Should().Be(0);
            settled.Cancelling.Should().Be(0);
            settled.Retiring.Should().Be(0);
            settled.Format().Should().Contain("does not resolve retained Unknown or Interrupted");
            f.Adapter.Calls.Should().Be(1);
        }
        finally
        {
            source = true;
            provider.TrySetResult(LateAnswer());
            await submission;
        }
    }

    [Fact]
    public async Task SubmittedActivityObservesCompletingAcceptingAndUnconfirmedWithoutPromotingThemToIdle()
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        SubmittedWorkActivity? accepting = null;
        SubmittedWorkActivity? completing = null;
        f.AfterRetain = () => accepting = service.ReadSubmittedWorkActivity();
        f.BeforeExecutionComplete = () =>
        {
            completing = service.ReadSubmittedWorkActivity();
            throw new IOException("Synthetic terminal retention unavailable.");
        };
        await service.Invoking(value => value.SubmitAsync(new(f.Session, null), "private original", () => true, Token))
            .Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        accepting!.Accepting.Should().Be(1);
        completing!.Retiring.Should().Be(1);
        var held = service.ReadSubmittedWorkActivity();
        held.CompletionUnconfirmed.Should().BeTrue();
        held.Format().Should().Contain("Resource release or terminal retention unavailable/unconfirmed")
            .And.Contain("Owned work remains held").And.NotContain("private original");
        f.Adapter.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubmittedActivityObservesPendingAndIndependentRecoveryOrManualHoldsWithoutReleasingThem(bool manual)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var first = await service.SubmitQueuedAsync(target, "private running", () => true, Token);
        var next = await service.SubmitQueuedAsync(target, "private pending", () => true, Token);
        try
        {
            var pending = await service.ReadPendingConversationAsync(target, () => true, Token);
            if (manual)
            {
                await service.SetManualDispatchHoldAsync(pending.DispatchControl!, ConversationDispatchHoldReason.OriginalUser,
                    () => true, Token);
            }
            else
            {
                await service.CancelPendingConversationAsync(target, pending.Turns.Single(turn => turn.Position == 1),
                    pending.Revision, () => true, Token);
            }
            var status = service.ReadSubmittedWorkActivity();
            status.HeldConversations.Should().Be(1);
            status.Pending.Should().Be(manual ? 1 : 0);
            using (var review = await service.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.StopAllWork,
                RequestOrigin.LocalUi, () => true, Token))
            {
                service.ReadSubmittedWorkActivity().ApplicationHeld.Should().BeTrue();
                service.ReadSubmittedWorkActivity().Format().Should().Contain("application hold held");
                first.Completion.IsCompleted.Should().BeFalse();
            }
            service.ReadSubmittedWorkActivity().ApplicationHeld.Should().BeFalse();
            var after = await service.ReadPendingConversationAsync(target, () => true, Token);
            after.Paused.Should().Be(!manual);
            after.ManualHoldReason.Should().Be(manual ? ConversationDispatchHoldReason.OriginalUser : ConversationDispatchHoldReason.None);
            f.Adapter.Calls.Should().Be(1);
        }
        finally
        {
            var disposal = service.DisposeAsync();
            provider.TrySetResult(LateAnswer());
            await disposal;
            await first.Completion;
            await next.Completion;
        }
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("closed")]
    [InlineData("changed-control")]
    [InlineData("changed-privacy")]
    [InlineData("caller")]
    public async Task SubmittedActivityFailsClosedInsteadOfPublishingEmptyOrLateCounts(string fault)
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        if (fault is "privacy") { f.CanControl = false; }
        if (fault is "closed") { await service.DisposeAsync(); }
        Task<ConversationSubmissionReceipt>? submission = null;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var change = false;
        if (fault is "changed-control" or "changed-privacy" or "caller")
        {
            f.Adapter.Pending = provider.Task;
            submission = service.SubmitAsync(new(f.Session, null), "private original", () =>
            {
                if (change)
                {
                    if (fault is "changed-control") { f.ControlRevision++; }
                    else { f.CanControl = false; }
                }
                return true;
            }, caller.Token);
            change = fault is not "caller";
            if (fault is "caller") { await caller.CancelAsync(); }
        }
        try
        {
            var status = service.ReadSubmittedWorkActivity();
            if (fault is "caller")
            {
                status.Retiring.Should().Be(1);
                host.IsQuiescent.Should().BeFalse();
            }
            else
            {
                status.Available.Should().BeFalse();
                status.Format().Should().Contain("unavailable").And.NotContain("busy 0").And.NotContain("private original");
            }
        }
        finally
        {
            change = false;
            f.CanControl = true;
            provider.TrySetResult(LateAnswer());
            if (submission is not null)
            {
                try { await submission; }
                catch (ConversationSubmissionUnconfirmedException) when (fault is "changed-control" or "caller") { }
            }

        }
    }

    [Fact]
    public async Task SubmittedActivityReportsPendingClearTransactionHoldBeforeAnyWithdrawal()
    {
        await using var p = await PendingClearFixture.Create();
        var review = await p.Review(RequestOrigin.LocalUi);
        SubmittedWorkActivity? held = null;
        p.F.BeforeTaskRead = () =>
        {
            held ??= p.Service.ReadSubmittedWorkActivity();
            return Task.CompletedTask;
        };
        var result = await p.Service.ConfirmPendingClearAsync(review, () => true, Token);
        held!.HeldConversations.Should().Be(1);
        held.Pending.Should().Be(2, "the transaction hold precedes acceptance of the reviewed withdrawal");
        result.Outcome.Should().Be("reviewed-pending-cancelled");
        p.F.Adapter.Calls.Should().Be(1);
        p.A.Completion.IsCompleted.Should().BeFalse();
    }
}