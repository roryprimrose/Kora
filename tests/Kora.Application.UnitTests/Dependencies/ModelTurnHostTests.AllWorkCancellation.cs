using System.Collections.Immutable;
using System.Text;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Mcp;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Mcp;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Fact]
    public async Task StopAllIncludesOriginalMcpAndPhysicallyJoinsCancellationIgnoringTransportWithoutLateDisclosure()
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var pending = new TaskCompletionSource<ImmutableArray<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var broker = new McpBroker(source.Account) { PendingCall = pending, EnteredCall = entered };
        var approvals = new McpApprovals(f);
        await using var service = McpService(f, f.Host, source, broker, approvals, await source.Policy());
        var operation = service.InvokeMcpAsync(new(f.Session, null), source.ServerId, source.Account,
            approvals.Present, McpSelect, ExplicitMcpInput, () => true, Token);
        await entered.Task.WaitAsync(Token);
        using var review = await service.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.StopAllWork,
            RequestOrigin.LocalUi, () => true, Token);
        review.Work.Should().ContainSingle().Which.Description.Should().StartWith("Explicit MCP request:");
        var joining = service.ConfirmApplicationLifecycleAsync(review, Token);
        try
        {
            joining.IsCompleted.Should().BeFalse();
            operation.IsCompleted.Should().BeFalse();
            broker.Disposed.Should().BeFalse();
        }
        finally
        {
            pending.TrySetResult(ImmutableArray.CreateRange(Encoding.UTF8.GetBytes(
                """{"jsonrpc":"2.0","id":3,"result":{"content":[{"type":"text","text":"late private MCP answer"}],"isError":false}}""")));
        }
        await joining;
        var result = await operation;
        result.Outcome.Should().Be(McpExplicitOutcome.Cancelled);
        result.DurableState.Should().Be(HostTaskState.Unknown);
        result.Result.Should().BeNull();
        review.Joined.Should().BeTrue();
        review.Results.Should().ContainSingle().Which.Should().Be(
            new AllWorkStopResult.Original(result.Request, true, true, HostTaskState.Unknown));
        broker.Disposed.Should().BeTrue();
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, false)]
    [InlineData(RequestOrigin.LocalUi, true)]
    public async Task StopAllReviewsAcceptedOriginalsWithoutStoppingThenWithdrawsAndPhysicallyJoinsExactWork(
        RequestOrigin origin, bool alreadyCancelling)
    {
        using var f = new Fixture(origin) { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var first = await service.SubmitQueuedAsync(target, "accepted running original", () => true, Token, origin: origin);
        var second = await service.SubmitQueuedAsync(target, "accepted pending original", () => true, Token, origin: origin);
        try
        {
            if (alreadyCancelling)
            {
                var running = (await service.ReadRunningConversationAsync(target, () => true, Token))!;
                await service.CancelRunningConversationAsync(running.Target, () => true, Token, origin);
            }
            using (var passive = await service.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.StopAllWork,
                origin, () => true, Token))
            {
                passive.Operation.Should().Be(ApplicationLifecycleOperation.StopAllWork);
                passive.Origin.Should().Be(origin);
                passive.StopAccepted.Should().BeFalse();
                passive.Results.Should().BeEmpty();
                passive.Work.Should().HaveCount(2);
                first.Completion.IsCompleted.Should().BeFalse();
                second.Completion.IsCompleted.Should().BeFalse();
            }
            using var review = await service.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.StopAllWork,
                origin, () => true, Token);
            var joining = service.ConfirmApplicationLifecycleAsync(review, Token);
            review.StopAccepted.Should().BeTrue();
            joining.IsCompleted.Should().BeFalse();
            await service.Invoking(value => value.SubmitQueuedAsync(target, "racing submission", () => true, Token, origin: origin))
                .Should().ThrowAsync<InvalidOperationException>();
            provider.SetResult(LateAnswer());
            await joining;
            await first.Completion;
            await second.Completion;
            review.Joined.Should().BeTrue();
            review.Results.Should().HaveCount(2).And.OnlyContain(result =>
                result.StopAccepted && result.PhysicallyJoined);
            review.Results.Single(result => result.Request == first.Receipt.Message.Request).TerminalState.Should().Be(HostTaskState.Unknown);
            review.Results.Single(result => result.Request == second.Receipt.Message.Request).TerminalState.Should().Be(HostTaskState.Cancelled);
            review.Results.Select(result => result.Request).Should().BeEquivalentTo(
                [first.Receipt.Message.Request, second.Receipt.Message.Request]);
            f.Adapter.Calls.Should().Be(1);
            f.PendingRecords.Values.Should().OnlyContain(value => value.Message.AssistantResponse == null);
            review.Dispose();
            using var fresh = await service.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.StopAllWork,
                origin, () => true, Token);
            fresh.Work.Should().BeEmpty();
            await service.ConfirmApplicationLifecycleAsync(fresh, Token);
            fresh.Joined.Should().BeTrue();
        }
        finally { provider.TrySetResult(LateAnswer()); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopAllJoinsAcceptedCancellationEvenWhenItsNotificationFails(bool stopAll)
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var submission = await service.SubmitQueuedAsync(new(f.Session, null), "original", () => true, Token);
        using var review = await service.ReviewApplicationLifecycleAsync(
            stopAll ? ApplicationLifecycleOperation.StopAllWork : ApplicationLifecycleOperation.Exit,
            RequestOrigin.LocalUi, () => true, Token);
        static void Fail(ConversationExecutionStatus status)
        {
            if (status.Phase == ConversationExecutionPhase.Cancelling) { throw new IOException("Synthetic notification failure."); }
        }
        service.RunningConversationChanged += Fail;
        try
        {
            var joining = service.ConfirmApplicationLifecycleAsync(review, Token);
            joining.IsCompleted.Should().BeFalse();
            review.StopAccepted.Should().BeTrue();
            provider.SetResult(LateAnswer());
            await this.Invoking(_ => joining).Should().ThrowAsync<IOException>();
            await submission.Completion;
            review.Joined.Should().BeFalse();
            review.Results.Should().ContainSingle().Which.Should().Be(
                new AllWorkStopResult.Original(submission.Receipt.Message.Request, true, false, null));
        }
        finally { service.RunningConversationChanged -= Fail; provider.TrySetResult(LateAnswer()); }
    }

    [Fact]
    public async Task StopAllRetainsFailedPendingReadbackAndStillJoinsEveryAcceptedOriginal()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var first = await service.SubmitQueuedAsync(target, "running original", () => true, Token);
        var second = await service.SubmitQueuedAsync(target, "pending original", () => true, Token);
        using var review = await service.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.StopAllWork,
            RequestOrigin.LocalUi, () => true, Token);
        f.BeforeTaskRead = () => f.PendingRecords[second.Receipt.Message.Request.RequestId].Task.State == HostTaskState.Cancelled
            ? throw new IOException("Synthetic pending terminal readback failure.") : Task.CompletedTask;
        try
        {
            var joining = service.ConfirmApplicationLifecycleAsync(review, Token);
            joining.IsCompleted.Should().BeFalse();
            provider.SetResult(LateAnswer());
            await this.Invoking(_ => joining).Should().ThrowAsync<InvalidOperationException>();
            await first.Completion;
            (await second.Completion).Failure.Should().NotBeNull();
            review.Joined.Should().BeFalse();
            review.Results.Should().HaveCount(2);
            review.Results.Single(item => item.Request == second.Receipt.Message.Request)
                .Should().Be(new AllWorkStopResult.Original(second.Receipt.Message.Request, true, false, null));
            f.Adapter.Calls.Should().Be(1);
        }
        finally { provider.TrySetResult(LateAnswer()); }
    }

    [Fact]
    public async Task StopAllRetainsPhysicalJoinFailureAsUnconfirmedInsteadOfReleasingCleanAuthority()
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        f.DetachExpectedFailedHost(host);
        await using var service = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var submission = await service.SubmitQueuedAsync(new(f.Session, null), "original", () => true, Token);
        using var review = await service.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.StopAllWork,
            RequestOrigin.LocalUi, () => true, Token);
        try
        {
            var joining = service.ConfirmApplicationLifecycleAsync(review, Token);
            joining.IsCompleted.Should().BeFalse();
            f.Adapter.JoinConfirmed = false;
            provider.SetResult(LateAnswer());
            await this.Invoking(_ => joining).Should().ThrowAsync<InvalidOperationException>();
            await submission.Completion;
            review.Joined.Should().BeFalse();
            review.Results.Should().ContainSingle().Which
                .Should().Be(new AllWorkStopResult.Original(submission.Receipt.Message.Request, true, false, null));
            host.IsQuiescent.Should().BeFalse();
        }
        finally
        {
            provider.TrySetResult(LateAnswer());
            await Assert.ThrowsAsync<AggregateException>(async () => await host.DisposeAsync());
            f.Adapter.JoinConfirmed = true;
        }
    }
}
