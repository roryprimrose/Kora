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
    [Fact]
    public async Task LifecycleReviewIncludesAnAcceptedDirectHostSubmissionWithoutAnOrdinaryPendingLane()
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var submission = service.SubmitOriginalLocalAsync(new(f.Session, null), "direct original", () => true,
            _ => Task.CompletedTask, [], RequestOrigin.LocalUi, Token);
        using var review = await service.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.Exit,
            RequestOrigin.LocalUi, () => true, Token);
        review.Work.Should().ContainSingle().Which.Description.Should().Be("direct original");
        var joining = service.ConfirmApplicationLifecycleAsync(review, Token);
        joining.IsCompleted.Should().BeFalse();
        provider.SetResult(LateAnswer());
        await joining;
        (await submission).Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        review.Joined.Should().BeTrue();
    }

    [Fact]
    public async Task LifecycleConfirmationCanWithdrawAnAcceptedOriginalStillWaitingForItsPhysicalDispatch()
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        Task? joining = null;
        ApplicationLifecycleReview? review = null;
        service.RunningConversationChanged += status =>
        {
            if (status.Phase != ConversationExecutionPhase.Waiting || joining is not null) { return; }
            joining = StopWaitingAsync();
        };
        async Task StopWaitingAsync()
        {
            review = await service.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.Exit,
                RequestOrigin.LocalUi, () => true, Token);
            await service.ConfirmApplicationLifecycleAsync(review, Token);
        }
        var submitted = await service.SubmitQueuedAsync(new(f.Session, null), "waiting original", () => true, Token);
        try
        {
            await joining!;
            (await submitted.Completion).RequireReceipt().Task.State.Should().Be(HostTaskState.Cancelled);
            review!.Joined.Should().BeTrue();
            f.Adapter.Calls.Should().Be(0);
        }
        finally { review?.Dispose(); }
    }

    [Fact]
    public async Task LifecycleReviewIncludesAnAlreadyCancellingOriginalAndStillJoinsItsPhysicalResources()
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var submitted = await service.SubmitQueuedAsync(target, "original", () => true, Token);
        var running = (await service.ReadRunningConversationAsync(target, () => true, Token))!;
        await service.CancelRunningConversationAsync(running.Target, () => true, Token);
        using var review = await service.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.Exit,
            RequestOrigin.LocalUi, () => true, Token);
        var joining = service.ConfirmApplicationLifecycleAsync(review, Token);
        joining.IsCompleted.Should().BeFalse();
        provider.SetResult(LateAnswer());
        await joining;
        await submitted.Completion;
        review.Joined.Should().BeTrue();
    }

    [Fact]
    public async Task ExpiredLifecycleReviewCannotLatchAdmissionOrReleaseANewerAttempt()
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        using var expired = await service.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.Exit,
            RequestOrigin.LocalUi, () => true, Token);
        f.Time.Now = expired.ExpiresAt;
        using var current = await service.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.Restart,
            RequestOrigin.LocalUi, () => true, Token);
        expired.Dispose();
        service.IsApplicationLifecycleCurrent(expired).Should().BeFalse();
        service.IsApplicationLifecycleCurrent(current).Should().BeTrue();
        await service.ConfirmApplicationLifecycleAsync(current, Token);
    }

    [Fact]
    public async Task SourceChangeDuringReviewSnapshotReleasesOnlyTheFailedAttemptWithoutStoppingWork()
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var original = f.Session;
        var submitted = await service.SubmitQueuedAsync(new(original, null), "original", () => true, Token);
        f.OnRead = () => f.Session = original with { Generation = new(2) };
        try
        {
            await service.Invoking(value => value.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.Exit,
                RequestOrigin.LocalUi, () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
            f.CancelAudit.Events.Should().BeEmpty();
            submitted.Completion.IsCompleted.Should().BeFalse();
        }
        finally
        {
            f.OnRead = null;
            f.Session = original;
            provider.SetResult(LateAnswer());
            await submitted.Completion;
        }
        using var fresh = await service.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.Exit,
            RequestOrigin.LocalUi, () => true, Token);
        fresh.Work.Should().BeEmpty();
    }

    [Fact]
    public async Task ChangedSessionAfterConfirmedTerminalJoinStillBlocksTheApplicationEffect()
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var submitted = await service.SubmitQueuedAsync(new(f.Session, null), "original", () => true, Token);
        using var review = await service.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.Exit,
            RequestOrigin.LocalUi, () => true, Token);
        service.RunningConversationChanged += status =>
        {
            if (status.Phase == ConversationExecutionPhase.Terminal)
            { f.Metadata = new(f.Session.SessionId, new(1), new("Changed after terminal retention")); }
        };
        var joining = service.ConfirmApplicationLifecycleAsync(review, Token);
        provider.SetResult(LateAnswer());
        await this.Invoking(_ => joining).Should().ThrowAsync<InvalidOperationException>();
        await submitted.Completion;
        review.Joined.Should().BeFalse();
    }

    [Fact]
    public async Task LifecycleBarrierFencesActualPendingResumptionAndNaturallyCompletedWorkInvalidatesTheReview()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var first = await service.SubmitQueuedAsync(target, "first", () => true, Token);
        var second = await service.SubmitQueuedAsync(target, "second", () => true, Token);
        using var review = await service.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.Exit,
            RequestOrigin.LocalUi, () => true, Token);
        f.Adapter.Pending = null;
        provider.SetResult(LateAnswer());
        await first.Completion;
        f.Adapter.Calls.Should().Be(1);
        second.Completion.IsCompleted.Should().BeFalse();
        await service.Invoking(value => value.ResumePendingConversationAsync(target, 0, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        await service.Invoking(value => value.ConfirmApplicationLifecycleAsync(review, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.CancelAudit.Events.Should().BeEmpty();
        review.Dispose();
        (await second.Completion).RequireReceipt().Task.State.Should().Be(HostTaskState.Succeeded);
        f.Adapter.Calls.Should().Be(2);
    }

    [Fact]
    public async Task LifecycleBarrierCanReviewANewlyRetainedPendingOriginalBeforeItsFirstDispatch()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        Task<ApplicationLifecycleReview>? reviewing = null;
        var submission = await service.SubmitQueuedAsync(new(f.Session, null), "retained original", () => true, Token,
            accepted: () => reviewing = service.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.Exit,
                RequestOrigin.LocalUi, () => true, Token));
        using var review = await reviewing!;
        review.Work.Should().ContainSingle();
        f.Adapter.Calls.Should().Be(0);
        await service.Invoking(value => value.ConfirmApplicationLifecycleAsync(review, Token))
            .Should().ThrowAsync<InvalidOperationException>("the final acceptance pulse changed the reviewed lane revision");
        f.CancelAudit.Events.Should().BeEmpty();
        review.Dispose();
        (await submission.Completion).RequireReceipt().Task.State.Should().Be(HostTaskState.Succeeded);
        f.Adapter.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("early-release")]
    [InlineData("terminal-readback")]
    [InlineData("notification")]
    [InlineData("notification-and-readback")]
    [InlineData("private-during-join")]
    public async Task LifecycleRetainsThePhysicalJoinAcrossReleaseRevocationAndTerminalFailure(string fault)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var submission = await service.SubmitQueuedAsync(target, "accepted original", () => true, Token);
        var current = true;
        using var review = await service.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.Exit,
            RequestOrigin.LocalUi, () => current, Token);
        if (fault is "terminal-readback" or "notification-and-readback") { f.TerminalReceiptFault = "revision"; }
        var notificationFailed = false;
        service.RunningConversationChanged += status =>
        {
            if (!notificationFailed && status.Phase == ConversationExecutionPhase.Cancelling
                && fault is "notification" or "notification-and-readback")
            {
                notificationFailed = true;
                throw new IOException("Synthetic observer failure after accepted stop.");
            }
        };
        var joining = service.ConfirmApplicationLifecycleAsync(review, Token);
        joining.IsCompleted.Should().BeFalse();
        if (fault is "early-release") { review.Dispose(); }
        if (fault is "private-during-join") { current = false; }
        await service.Invoking(value => value.SubmitQueuedAsync(target, "must not escape the join", () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        submission.Completion.IsCompleted.Should().BeFalse();
        provider.SetResult(LateAnswer());
        await this.Invoking(_ => joining).Should().ThrowAsync<Exception>();
        await submission.Completion;
        review.Joined.Should().BeFalse();
        f.Adapter.Calls.Should().Be(1);
        review.Dispose();
        current = true;
        if (fault is "terminal-readback" or "notification-and-readback")
        {
            await service.Invoking(value => value.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.Exit,
                RequestOrigin.LocalUi, () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            using var fresh = await service.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.Exit,
                RequestOrigin.LocalUi, () => true, Token);
            fresh.Work.Should().BeEmpty();
            await service.ConfirmApplicationLifecycleAsync(fresh, Token);
            fresh.Joined.Should().BeTrue();
        }
    }

    [Theory]
    [InlineData("source")]
    [InlineData("control")]
    [InlineData("session")]
    [InlineData("task")]
    [InlineData("released")]
    [InlineData("audit-request")]
    [InlineData("audit-outcome")]
    [InlineData("audit-revocation")]
    [InlineData("foreign-host")]
    [InlineData("manual-stop")]
    public async Task ChangedOrUnauditedLifecycleReviewDoesNotStopOrReplayAcceptedOriginals(string fault)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var first = await service.SubmitQueuedAsync(target, "accepted original", () => true, Token);
        var live = true;
        using var review = await service.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.Exit,
            RequestOrigin.LocalUi, () => live, Token);
        var task = f.Task;
        var session = f.Session;
        var retained = f.PendingRecords[first.Receipt.Message.Request.RequestId];
        if (fault is "source") { live = false; }
        if (fault is "control") { f.ControlRevision++; }
        if (fault is "session") { f.Session = f.Session with { Generation = new(2) }; }
        if (fault is "task")
        {
            f.PendingRecords[first.Receipt.Message.Request.RequestId] = retained with
            { Task = new(retained.Task.Request, new(99), retained.Task.State) };
        }
        if (fault is "released") { review.Dispose(); }
        if (fault is "audit-request") { f.CancelAudit.FailAt = 0; }
        if (fault is "audit-outcome") { f.CancelAudit.FailAt = 1; }
        if (fault is "audit-revocation") { f.CancelAudit.OnWrite = _ => live = false; }
        if (fault is "manual-stop")
        {
            var running = (await service.ReadRunningConversationAsync(target, () => true, Token))!;
            await service.CancelRunningConversationAsync(running.Target, () => true, Token);
        }
        await using var foreign = f.ConversationService(host, execution: host);
        try
        {
            await (fault is "foreign-host" ? foreign : service)
                .Invoking(value => value.ConfirmApplicationLifecycleAsync(review, Token)).Should().ThrowAsync<Exception>();
            review.Joined.Should().BeFalse();
            first.Completion.IsCompleted.Should().BeFalse();
            f.Adapter.Calls.Should().Be(1);
        }
        finally
        {
            f.Task = task;
            f.Session = session;
            f.PendingRecords[first.Receipt.Message.Request.RequestId] = retained;
            review.Dispose();
            provider.SetResult(LateAnswer());
            await first.Completion;
        }
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("origin")]
    [InlineData("source")]
    [InlineData("private")]
    [InlineData("disposed")]
    public async Task LifecycleReviewCannotInventAdmissionOrCleanUpUnknownWork(string fault)
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        if (fault is "private") { f.CanControl = false; }
        if (fault is "disposed") { await service.DisposeAsync(); }
        await service.Invoking(value => value.ReviewApplicationLifecycleAsync(
            fault is "operation" ? (ApplicationLifecycleOperation)99 : ApplicationLifecycleOperation.Exit,
            fault is "origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi, () => fault is not "source", Token))
            .Should().ThrowAsync<Exception>();
        f.Adapter.Calls.Should().Be(0);
        f.CancelAudit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task LifecycleReviewRejectsARequestStillInsideOriginalAdmissionWithoutStoppingIt()
    {
        using var f = new Fixture();
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var submission = service.SubmitQueuedAsync(new(f.Session, null), "preparing", () => true, Token,
            validateSource: async _ => { entered.TrySetResult(); await release.Task; });
        await entered.Task;
        await service.Invoking(value => value.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.Exit,
            RequestOrigin.LocalUi, () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Adapter.Calls.Should().Be(0);
        release.SetResult();
        await (await submission).Completion;
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task LifecycleReviewDoesNotStopSubmittedWorkAndConfirmationJoinsThePhysicalOriginal(RequestOrigin origin)
    {
        using var f = new Fixture(origin) { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var first = await service.SubmitQueuedAsync(target, "running original", () => true, Token, origin: origin);
        var second = await service.SubmitQueuedAsync(target, "queued original", () => true, Token, origin: origin);
        try
        {
            using (var cancelled = await service.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.Exit,
                origin, () => true, Token))
            {
                cancelled.Work.Should().HaveCount(2);
                f.CancelAudit.Events.Should().BeEmpty();
                first.Completion.IsCompleted.Should().BeFalse();
                second.Completion.IsCompleted.Should().BeFalse();
                await service.Invoking(value => value.SubmitQueuedAsync(target, "not accepted", () => true, Token, origin: origin))
                    .Should().ThrowAsync<InvalidOperationException>();
            }
            f.Adapter.Calls.Should().Be(1);
            first.Completion.IsCompleted.Should().BeFalse();
            using var confirmed = await service.ReviewApplicationLifecycleAsync(ApplicationLifecycleOperation.Restart,
                origin, () => true, Token);
            var stopped = service.ConfirmApplicationLifecycleAsync(confirmed, Token);
            stopped.IsCompleted.Should().BeFalse("cancellation is not a physical provider join");
            f.Adapter.Calls.Should().Be(1);
            provider.SetResult(LateAnswer());
            await stopped;
            confirmed.Joined.Should().BeTrue();
            (await first.Completion).RequireReceipt().Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
            (await second.Completion).RequireReceipt().Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
            f.PendingRecords.Values.Should().OnlyContain(value => value.Message.AssistantResponse == null);
            await service.Invoking(value => value.ConfirmApplicationLifecycleAsync(confirmed, Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        finally { provider.TrySetResult(LateAnswer()); }
    }
}
