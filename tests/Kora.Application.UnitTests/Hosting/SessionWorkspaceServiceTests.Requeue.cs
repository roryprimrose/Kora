using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    public async Task ExpiredReviewAndSeparateConfirmationUseSharedWorkspaceWithNoReadOrDuplicateOutcome(RequestOrigin origin, bool cancelLate)
    {
        using var f = new Fixture();
        using var cancel = new CancellationTokenSource();
        var old = ExpiredEntry(f);
        f.QueueRows.Add(old);
        await using var queue = f.QueueService();
        var snapshot = await f.ReadQueueAsync(f.Request.SessionId, f.Token);
        var reviewed = await queue.ExecuteCommandAsync(RequeueReviewCommand(snapshot, old), origin, () => true, f.Token);
        f.TaskWrites.Should().BeEmpty();
        var review = reviewed.QueueRequeueReview!;
        reviewed.Outcome.Should().Be("reviewed");
        f.AfterMove = () => { if (cancelLate) { cancel.Cancel(); } };
        var result = await queue.ExecuteCommandAsync(RequeueConfirmCommand(review), origin, () => true, cancel.Token);
        result.Outcome.Should().Be("committed");
        result.QueueRequeueReceipt!.Original.Should().Be(old);
        result.QueueRequeueReceipt.Fresh.Request.Should().Be(review.FreshRequest);
        f.QueueRows.Single(entry => entry.Request.TaskId == old.Request.TaskId).State.Should().Be(SessionQueueState.Removed);
        f.QueueActions.Should().Be(0);
        f.TaskWrites.Should().HaveCount(2);
        f.TaskWrites.Last().State.Should().Be(HostTaskState.Succeeded);
        var replay = () => queue.ExecuteCommandAsync(RequeueConfirmCommand(review), origin, () => true, f.Token);
        await replay.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RequeueDiscardDoesNotWriteAndRevokesOnlyTheExactHostReview()
    {
        using var f = new Fixture();
        var old = ExpiredEntry(f);
        f.QueueRows.Add(old);
        await using var queue = f.QueueService();
        var snapshot = await f.ReadQueueAsync(f.Request.SessionId, f.Token);
        var review = (await queue.ExecuteCommandAsync(RequeueReviewCommand(snapshot, old), RequestOrigin.LocalUi, () => true, f.Token)).QueueRequeueReview!;
        var result = await queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueRequeueExpiredDiscard, snapshot.SessionId.Value)
            { QueueRequeueReviewId = review.Id }, RequestOrigin.LocalUi, () => true, f.Token);
        result.Outcome.Should().Be("cancelled-review");
        f.QueueRows.Should().Equal(old);
        f.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("queue")]
    [InlineData("control")]
    [InlineData("run")]
    [InlineData("oldtask")]
    [InlineData("oldrevision")]
    [InlineData("freshrequest")]
    [InlineData("freshtask")]
    [InlineData("review")]
    [InlineData("origin")]
    [InlineData("epoch")]
    [InlineData("source")]
    [InlineData("nullrun")]
    [InlineData("nulltask")]
    [InlineData("nullrequest")]
    [InlineData("nullfresh")]
    [InlineData("nullreview")]
    public async Task AnyChangedExactConfirmationOrOriginalSourceRejectsTheWholeReview(string mismatch)
    {
        using var f = new Fixture();
        var old = ExpiredEntry(f);
        f.QueueRows.Add(old);
        await using var queue = f.QueueService();
        var snapshot = await f.ReadQueueAsync(f.Request.SessionId, f.Token);
        var sourceEligible = true;
        var review = (await queue.ExecuteCommandAsync(RequeueReviewCommand(snapshot, old), RequestOrigin.LocalUi, () => sourceEligible, f.Token)).QueueRequeueReview!;
        var command = RequeueConfirmCommand(review);
        command = mismatch switch
        {
            "generation" => command with { Generation = 2 }, "queue" => command with { QueueRevision = 99 },
            "control" => command with { QueueControlRevision = 99 }, "run" => command with { QueueRunId = Guid.NewGuid() },
            "oldtask" => command with { TaskId = Guid.NewGuid() }, "oldrevision" => command with { TaskRevision = 99 },
            "freshrequest" => command with { FreshRequestId = Guid.NewGuid() }, "freshtask" => command with { FreshTaskId = Guid.NewGuid() },
            "review" => command with { QueueRequeueReviewId = Guid.NewGuid() }, _ => command,
        };
        command = mismatch switch
        {
            "nullrun" => command with { QueueRunId = null }, "nulltask" => command with { TaskId = null },
            "nullrequest" => command with { FreshRequestId = null }, "nullfresh" => command with { FreshTaskId = null },
            "nullreview" => command with { QueueRequeueReviewId = null }, _ => command,
        };
        if (mismatch is "epoch") { f.AdvanceRevision(); }
        if (mismatch is "source") { sourceEligible = false; }
        var confirm = () => queue.ExecuteCommandAsync(command, mismatch is "origin" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi, () => true, f.Token);
        await confirm.Should().ThrowAsync<InvalidOperationException>();
        f.QueueRows.Should().Equal(old);
        f.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("queue")]
    [InlineData("run")]
    [InlineData("control")]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("nullrun")]
    public async Task StaleQueueReviewRequiresRefreshAndNeverAdmitsIds(string mismatch)
    {
        using var f = new Fixture();
        var old = ExpiredEntry(f);
        f.QueueRows.Add(old);
        await using var queue = f.QueueService();
        var snapshot = await f.ReadQueueAsync(f.Request.SessionId, f.Token);
        var command = RequeueReviewCommand(snapshot, old);
        command = mismatch switch
        {
            "generation" => command with { Generation = 2 }, "queue" => command with { QueueRevision = 99 },
            "run" => command with { QueueRunId = Guid.NewGuid() }, "control" => command with { QueueControlRevision = 99 }, _ => command,
        };
        if (mismatch is "missing") { f.QueueReadProjection = value => value with { DispatchControl = null }; }
        if (mismatch is "nullrun") { command = command with { QueueRunId = null }; }
        if (mismatch is "inactive") { f.ExactListReader = _ => new(new(f.Request.SessionId, new(1), false), null); }
        var review = () => queue.ExecuteCommandAsync(command, RequestOrigin.LocalUi, () => true, f.Token);
        await review.Should().ThrowAsync<InvalidOperationException>();
        f.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData("confirmed")]
    [InlineData("latecancel")]
    [InlineData("settings")]
    [InlineData("saved")]
    [InlineData("postcommit")]
    public async Task ExpiredNewDecisionCapturesCurrentConfirmedSettingsAndCannotReuseChangedConfiguration(string boundary)
    {
        using var f = new Fixture();
        using var cancel = new CancellationTokenSource();
        await using var settings = new SessionQueueConfigurationTestFixture();
        await settings.Refresh();
        await settings.Set("2", Kora.Core.Configuration.SessionQueueOption.PendingLifetimeMinutes);
        var old = ExpiredEntry(f);
        f.QueueRows.Add(old);
        await using var queue = f.QueueService(configuration: settings.Service);
        var snapshot = await f.ReadQueueAsync(f.Request.SessionId, f.Token);
        var review = (await queue.ExecuteCommandAsync(RequeueReviewCommand(snapshot, old), RequestOrigin.LocalUi, () => true, f.Token)).QueueRequeueReview!;
        review.Limits.PendingLifetimeMinutes.Should().Be(2);
        review.ExpiresAt.Should().Be(review.EnqueuedAt.AddMinutes(2));
        if (boundary is "settings") { await settings.Set("3", Kora.Core.Configuration.SessionQueueOption.PendingLifetimeMinutes); }
        if (boundary is "saved") { f.BeforeMove = () => settings.PreferencesStore.Value = new(pendingLifetimeMinutes: 3); }
        if (boundary is "latecancel") { f.AfterMove = cancel.Cancel; }
        if (boundary is "postcommit") { f.AfterMove = () => settings.PreferencesStore.Value = new(pendingLifetimeMinutes: 3); }
        var confirm = () => queue.ExecuteCommandAsync(RequeueConfirmCommand(review), RequestOrigin.LocalUi, () => true, cancel.Token);
        if (boundary is "settings" or "saved" or "postcommit")
        {
            await confirm.Should().ThrowAsync<InvalidOperationException>();
            if (boundary is "postcommit")
            {
                f.QueueRows.Single(entry => entry.Request.TaskId == old.Request.TaskId).State.Should().Be(SessionQueueState.Removed);
                f.TaskWrites.Last().State.Should().Be(HostTaskState.Succeeded, "known physical commit is not relabelled Denied by a failed configuration receipt");
                (await f.ReadQueueAsync(f.Request.SessionId, f.Token)).DispatchControl!.Held.Should().BeTrue();
            }
            else { f.QueueRows.Should().Equal(old); }
        }
        else
        {
            var result = await confirm();
            result.QueueRequeueReceipt!.Fresh.PendingLifetimeMinutes.Should().Be(2);
            result.QueueRequeueReceipt.Original.ExpiresAt.Should().Be(old.ExpiresAt);
        }
    }

    [Theory]
    [InlineData("source")]
    [InlineData("current")]
    [InlineData("delivery")]
    public async Task BothOriginalReviewAndConfirmationRemainEligibleThroughTransactionAndPublication(string boundary)
    {
        using var f = new Fixture();
        f.QueueRows.Add(ExpiredEntry(f));
        await using var queue = f.QueueService();
        var snapshot = await f.ReadQueueAsync(f.Request.SessionId, f.Token);
        var source = true;
        var current = true;
        var review = (await queue.ExecuteCommandAsync(RequeueReviewCommand(snapshot, f.QueueRows[0]), RequestOrigin.LocalUi,
            () => source, f.Token)).QueueRequeueReview!;
        f.BeforeMove = () =>
        {
            if (boundary is "source") { source = false; }
            if (boundary is "current") { current = false; }
        };
        if (boundary is "delivery") { f.AfterMove = () => current = false; }
        var confirm = () => queue.ExecuteCommandAsync(RequeueConfirmCommand(review), RequestOrigin.LocalUi, () => current, f.Token);
        await confirm.Should().ThrowAsync<InvalidOperationException>();
        if (boundary is "delivery")
        {
            f.TaskWrites.Last().State.Should().Be(HostTaskState.Succeeded);
            (await f.ReadQueueAsync(f.Request.SessionId, f.Token)).DispatchControl!.Held.Should().BeTrue();
        }
        else { f.QueueRows.Should().Equal(review.Original); }
    }

    private static SessionQueueEntry ExpiredEntry(Fixture f) => new(new(new(Guid.NewGuid()), f.Request.SessionId, new(Guid.NewGuid()),
        RequestOrigin.LocalUi), new(1), new(1), 1, SessionQueueState.Pending, f.QueueRun, f.ControlRevision,
        f.QueueTime.GetUtcNow().AddMinutes(-30), f.QueueTime.GetUtcNow());

    private static SessionCommand RequeueReviewCommand(SessionQueueSnapshot snapshot, SessionQueueEntry old) =>
        new(SessionCommandOperation.QueueRequeueExpiredReview, snapshot.SessionId.Value, snapshot.Generation.Value)
        {
            QueueRevision = snapshot.Revision, QueueRunId = snapshot.DispatchControl!.RunId,
            QueueControlRevision = snapshot.DispatchControl.Revision, TaskId = old.Request.TaskId.Value, TaskRevision = old.Revision.Value,
        };

    private static SessionCommand RequeueConfirmCommand(SessionQueueRequeueReview review) => RequeueReviewCommand(review.Queue, review.Original) with
    {
        Operation = SessionCommandOperation.QueueRequeueExpiredConfirm, QueueRequeueReviewId = review.Id,
        FreshRequestId = review.FreshRequest.RequestId.Value, FreshTaskId = review.FreshRequest.TaskId.Value,
    };
}
