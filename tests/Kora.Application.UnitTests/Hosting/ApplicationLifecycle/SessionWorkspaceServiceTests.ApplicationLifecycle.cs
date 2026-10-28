using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Fact]
    public async Task UnconfirmedCompatibilityWithdrawalCannotCommitASuccessfulControlOrCleanApplicationRelease()
    {
        using var f = new UnconfirmedLifecycleWithdrawal { FileReceipts = true };
        await using var queue = f.QueueService();
        await queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueEnqueue), RequestOrigin.LocalUi, () => true, f.Token);
        using var review = await queue.ReviewApplicationLifecycleAsync(RequestOrigin.LocalUi, () => true, f.Token);
        await queue.Invoking(service => service.ConfirmApplicationLifecycleAsync(review, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        review.Joined.Should().BeFalse();
        f.TaskWrites.Last().State.Should().Be(HostTaskState.IntentRecorded);
        f.QueueRows.Should().OnlyContain(entry => entry.State == SessionQueueState.Pending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedCompatibilitySourceOrUnconfirmedFinalWorkReadbackCannotClaimCleanRelease(bool source)
    {
        using var f = new Fixture { FileReceipts = true };
        await using var queue = f.QueueService();
        await queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueEnqueue), RequestOrigin.LocalUi, () => true, f.Token);
        var original = f.QueueRows.ToArray();
        var current = true;
        using var review = await queue.ReviewApplicationLifecycleAsync(RequestOrigin.LocalUi, () => current, f.Token);
        f.BeforeTaskWrite = record =>
        {
            if (!record.IsTerminal) { return; }
            if (source) { current = false; }
            else { f.QueueReadProjection = snapshot => snapshot with { Entries = original }; }
        };
        await queue.Invoking(service => service.ConfirmApplicationLifecycleAsync(review, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        review.Joined.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LifecycleCompatibilityBarrierParksActualResumptionUntilCancelOrItsOwnedDeadline(bool expire)
    {
        using var f = new Fixture { FileReceipts = true };
        using var clock = new LifecycleQueueClock();
        await using var queue = f.QueueService(clock: clock);
        await AddMovePair(f, queue);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.OnObserve = () => { entered.TrySetResult(); release.Wait(f.Token); };
        var dispatch = queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueDispatch) with { QueueRevision = f.QueueRevision },
            RequestOrigin.LocalUi, () => true, f.Token);
        await entered.Task;
        using var review = await queue.ReviewApplicationLifecycleAsync(RequestOrigin.LocalUi, () => true, f.Token);
        try
        {
            release.Set();
            await clock.TimerCreated.Task.WaitAsync(f.Token);
            queue.IsApplicationLifecycleCurrent(review).Should().BeFalse("the reviewed read has entered terminal publication");
            f.QueueActions.Should().Be(1);
            f.QueueRows.Should().Contain(entry => entry.State == SessionQueueState.Pending);
            if (expire) { clock.Fire(); }
            else { review.Dispose(); }
            await dispatch;
            f.QueueActions.Should().Be(2);
            f.QueueRows.Should().OnlyContain(entry => entry.State == SessionQueueState.Succeeded);
            queue.IsApplicationLifecycleCurrent(review).Should().BeFalse();
        }
        finally { release.Set(); review.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredCompatibilityAttemptCannotReleaseItsReplacementEvenWhenTheOldSnapshotFailsLater(bool duringRead)
    {
        using var f = new Fixture();
        using var clock = new LifecycleQueueClock();
        await using var queue = f.QueueService(clock: clock);
        Task<Kora.Application.Hosting.SessionQueueLifecycleReview>? replacement = null;
        if (duringRead)
        {
            f.AfterListRead = () =>
            {
                f.AfterListRead = null;
                clock.Now = clock.Now.AddMinutes(1);
                replacement = queue.ReviewApplicationLifecycleAsync(RequestOrigin.LocalUi, () => true, f.Token);
            };
            await queue.Invoking(service => service.ReviewApplicationLifecycleAsync(RequestOrigin.LocalUi, () => true, f.Token))
                .Should().ThrowAsync<InvalidOperationException>();
            using var current = await replacement!;
            queue.IsApplicationLifecycleCurrent(current).Should().BeTrue();
        }
        else
        {
            using var old = await queue.ReviewApplicationLifecycleAsync(RequestOrigin.LocalUi, () => true, f.Token);
            clock.Now = old.ExpiresAt;
            using var current = await queue.ReviewApplicationLifecycleAsync(RequestOrigin.LocalUi, () => true, f.Token);
            old.Dispose();
            queue.IsApplicationLifecycleCurrent(current).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task LifecycleCompatibilityConfirmationJoinsAnAlreadyDispatchedFiniteReadAndWithdrawsItsPendingSuccessor(bool fails, bool releaseEarly)
    {
        using var f = new Fixture { FileReceipts = true };
        await using var queue = f.QueueService();
        await AddMovePair(f, queue);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.OnObserve = () => { entered.TrySetResult(); release.Wait(f.Token); };
        var dispatch = queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueDispatch) with { QueueRevision = f.QueueRevision },
            RequestOrigin.LocalUi, () => true, f.Token);
        await entered.Task;
        using var review = await queue.ReviewApplicationLifecycleAsync(RequestOrigin.LocalUi, () => true, f.Token);
        review.Queues.Single().Entries.Should().Contain(entry => entry.State == SessionQueueState.Running)
            .And.Contain(entry => entry.State == SessionQueueState.Pending);
        var joining = queue.ConfirmApplicationLifecycleAsync(review, f.Token);
        joining.IsCompleted.Should().BeFalse();
        f.QueueActions.Should().Be(1);
        if (releaseEarly)
        {
            review.Dispose();
            await queue.Invoking(service => service.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueEnqueue),
                RequestOrigin.LocalUi, () => true, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        if (fails) { f.BeforeComplete = () => throw new IOException("Synthetic terminal failure."); }
        release.Set();
        if (fails)
        {
            await this.Invoking(_ => dispatch).Should().ThrowAsync<IOException>();
            await this.Invoking(_ => joining).Should().ThrowAsync<InvalidOperationException>();
            review.Joined.Should().BeFalse();
        }
        else
        {
            await dispatch;
            if (releaseEarly)
            {
                await this.Invoking(_ => joining).Should().ThrowAsync<InvalidOperationException>();
                review.Joined.Should().BeFalse();
            }
            else
            {
                await joining;
                review.Joined.Should().BeTrue();
            }
        }
        f.QueueActions.Should().Be(1);
        f.QueueRows.Should().Contain(entry => entry.State == SessionQueueState.Cancelled);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    public async Task LifecycleCompatibilityReviewIsReversibleAndExactConfirmationCancelsOnlyPendingWork(RequestOrigin origin, bool empty)
    {
        using var f = new Fixture { FileReceipts = true };
        await using var queue = f.QueueService();
        if (!empty)
        {
            await queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueEnqueue), origin, () => true, f.Token);
        }
        var rows = f.QueueRows.ToArray();
        using (var cancelled = await queue.ReviewApplicationLifecycleAsync(origin, () => true, f.Token))
        {
            queue.IsApplicationLifecycleCurrent(cancelled).Should().BeTrue();
            cancelled.Origin.Should().Be(origin);
            cancelled.Queues.Should().HaveCount(empty ? 0 : 1);
            f.QueueRows.Should().Equal(rows);
            await queue.Invoking(service => service.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueEnqueue),
                origin, () => true, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        f.QueueRows.Should().Equal(rows);
        using var confirmed = await queue.ReviewApplicationLifecycleAsync(origin, () => true, f.Token);
        await queue.ConfirmApplicationLifecycleAsync(confirmed, f.Token);
        confirmed.Joined.Should().BeTrue();
        f.QueueRows.Should().OnlyContain(entry => entry.State == SessionQueueState.Cancelled);
        f.QueueActions.Should().Be(0);
        await queue.Invoking(service => service.ConfirmApplicationLifecycleAsync(confirmed, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        confirmed.Dispose();
        queue.IsApplicationLifecycleCurrent(confirmed).Should().BeFalse();
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("private")]
    [InlineData("revision")]
    [InlineData("read")]
    [InlineData("changed-during-read")]
    [InlineData("held")]
    [InlineData("disposed")]
    public async Task FailedCompatibilityLifecycleReviewNeverMutatesOrLatchesItsBarrier(string fault)
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        using var first = fault is "held" ? await queue.ReviewApplicationLifecycleAsync(RequestOrigin.LocalUi, () => true, f.Token) : null;
        if (fault is "private") { f.CanControl = false; }
        if (fault is "read") { f.QueueFailure = "read"; }
        if (fault is "revision") { f.AfterListRead = f.AdvanceRevision; }
        if (fault is "changed-during-read") { f.AfterListRead = () => f.CanControl = false; }
        if (fault is "disposed") { await queue.DisposeAsync(); }
        await queue.Invoking(service => service.ReviewApplicationLifecycleAsync(
            fault is "origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi, () => true, f.Token))
            .Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().BeEmpty();
        f.QueueRows.Should().BeEmpty();
        first?.Dispose();
        if (fault is not "disposed")
        {
            f.CanControl = true;
            f.QueueFailure = null;
            f.AfterListRead = null;
            using var fresh = await queue.ReviewApplicationLifecycleAsync(RequestOrigin.LocalUi, () => true, f.Token);
            fresh.Joined.Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("source")]
    [InlineData("revision")]
    [InlineData("queue")]
    [InlineData("release")]
    [InlineData("terminal")]
    [InlineData("terminal-null")]
    public async Task ChangedOrUnconfirmedCompatibilityLifecycleCannotClaimCleanShutdown(string fault)
    {
        using var f = new Fixture { FileReceipts = fault is not ("terminal" or "terminal-null"), UnknownTask = fault is "terminal-null" };
        await using var queue = f.QueueService();
        await queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueEnqueue), RequestOrigin.LocalUi, () => true, f.Token);
        var current = true;
        using var review = await queue.ReviewApplicationLifecycleAsync(RequestOrigin.LocalUi, () => current, f.Token);
        if (fault is "source") { current = false; }
        if (fault is "revision") { f.AdvanceRevision(); }
        if (fault is "queue") { f.AdvanceQueueRevision(); }
        if (fault is "release") { review.Dispose(); }
        await queue.Invoking(service => service.ConfirmApplicationLifecycleAsync(review, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        review.Joined.Should().BeFalse();
        f.QueueActions.Should().Be(0);
        if (fault is not ("terminal" or "terminal-null")) { f.QueueRows.Should().OnlyContain(entry => entry.State == SessionQueueState.Pending); }
    }

    private sealed class UnconfirmedLifecycleWithdrawal : Fixture, ISessionQueueStore
    {
        ValueTask<SessionQueueSnapshot> ISessionQueueStore.RemovePendingAsync(HostRequest control, HostRevision generation,
            long expectedRevision, HostId<TaskIdentity>? task, HostRevision? entryRevision, SessionQueueState outcome,
            Func<bool> eligible, CancellationToken token) => ReadQueueAsync(control.SessionId, token);
    }

    private sealed class LifecycleQueueClock : TimeProvider, IDisposable
    {
        private LifecycleTimer? timer;
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
        internal TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override DateTimeOffset GetUtcNow() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => 0;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            timer = new(callback, state);
            TimerCreated.TrySetResult();
            return timer;
        }
        internal void Fire() => timer!.Fire();
        public void Dispose() => timer?.Dispose();

        private sealed class LifecycleTimer(TimerCallback callback, object? state) : ITimer
        {
            private bool disposed;
            internal void Fire() { if (!disposed) { callback(state); } }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !disposed;
            public void Dispose() => disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
