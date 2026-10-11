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
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdjacentMutationConsumesOnlyCurrentConfirmedSettingsAtTheShortFence(bool changed)
    {
        using var f = new Fixture();
        await using var settings = new SessionQueueConfigurationTestFixture();
        settings.Service.Observe();
        await using var queue = f.QueueService(configuration: settings.Service);
        await AddMovePair(f, queue);
        var review = (await queue.ExecuteCommandAsync(MoveCommand(f, true), RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
        f.BeforeMove = () => { if (changed) { settings.PreferencesStore.Value = new(pendingPerSession: 1); } };
        var confirm = () => queue.ExecuteCommandAsync(ConfirmMove(review), RequestOrigin.LocalUi, () => true, f.Token);
        if (changed)
        {
            await confirm.Should().ThrowAsync<InvalidOperationException>();
            f.QueueRows.Should().Equal(review.Queue.Entries);
            f.TaskWrites.Last().State.Should().Be(HostTaskState.Denied);
        }
        else
        {
            (await confirm()).Queue!.Entries.Should().Equal(review.Queue.Entries.Reverse());
            f.TaskWrites.Last().State.Should().Be(HostTaskState.Succeeded);
        }
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, true)]
    [InlineData(RequestOrigin.ActivatedVoice, false)]
    public async Task ExactReviewedAdjacentMoveRoutesThroughSharedServiceAndNeverDispatchesOrRenewsEntries(RequestOrigin origin, bool up)
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        var workspace = new SessionWorkspaceService(f, new(f), f, f.Logger, queue);
        await AddMovePair(f, queue);
        var original = f.QueueRows.ToArray();
        var writes = f.TaskWrites.Count;
        var review = (await workspace.ExecuteCommandAsync(MoveCommand(f, up), origin, () => true, f.Token)).QueueMoveReview!;
        f.TaskWrites.Should().HaveCount(writes, "reviewing does not acquire a mutation intent or renew activity");
        review.Selected.Should().Be(up ? original[1] : original[0]);
        review.Neighbor.Should().Be(up ? original[0] : original[1]);
        review.ObservedAt.Should().Be(f.QueueTime.GetUtcNow());
        var result = await workspace.ExecuteCommandAsync(ConfirmMove(review), origin, () => true, f.Token);
        result.Outcome.Should().Be("committed");
        result.Queue!.Entries.Should().Equal(original.Reverse());
        f.QueueRows.Should().Equal(original);
        f.QueueActions.Should().Be(0);
        f.TaskWrites.Skip(writes).Should().HaveCount(2, "control intent and atomic terminal only; no duplicate success write");
        var repeat = () => queue.ExecuteCommandAsync(ConfirmMove(review), origin, () => true, f.Token);
        await repeat.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CancelledOrReplacedExactReviewCannotBeConfirmedOrRetargeted()
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        await AddMovePair(f, queue);
        var command = MoveCommand(f, true);
        var review = (await queue.ExecuteCommandAsync(command, RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
        var replacement = (await queue.ExecuteCommandAsync(command, RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
        var stale = () => queue.ExecuteCommandAsync(ConfirmMove(review), RequestOrigin.LocalUi, () => true, f.Token);
        await stale.Should().ThrowAsync<InvalidOperationException>();
        var ignored = () => queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueMoveCancel, f.Request.SessionId.Value)
            { QueueMoveReviewId = review.Id }, RequestOrigin.LocalUi, () => true, f.Token);
        await ignored.Should().ThrowAsync<InvalidOperationException>();
        var cancelled = await queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueMoveCancel, f.Request.SessionId.Value)
            { QueueMoveReviewId = replacement.Id }, RequestOrigin.LocalUi, () => true, f.Token);
        cancelled.Outcome.Should().Be("cancelled-review");
        var confirm = () => queue.ExecuteCommandAsync(ConfirmMove(replacement), RequestOrigin.LocalUi, () => true, f.Token);
        await confirm.Should().ThrowAsync<InvalidOperationException>();
        var repeated = () => queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueMoveCancel, f.Request.SessionId.Value)
            { QueueMoveReviewId = replacement.Id }, RequestOrigin.LocalUi, () => true, f.Token);
        await repeated.Should().ThrowAsync<InvalidOperationException>();
        f.QueueRevision.Should().Be(2);
        f.TaskWrites.Should().HaveCount(4);
    }

    [Fact]
    public async Task EveryConfirmationBindingRejectsStaleIdentityRevisionRunOriginAndPrivateEpochWithoutConsumingCurrentReview()
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        await AddMovePair(f, queue);
        var review = (await queue.ExecuteCommandAsync(MoveCommand(f, true), RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
        var exact = ConfirmMove(review);
        foreach (var malformed in new[]
        {
            exact with { QueueMoveReviewId = Guid.NewGuid() }, exact with { Generation = 2 },
            exact with { QueueRevision = 99 }, exact with { QueueRunId = Guid.NewGuid() },
            exact with { QueueControlRevision = 1 }, exact with { TaskId = Guid.NewGuid() },
            exact with { TaskRevision = 2 }, exact with { QueueNeighborId = Guid.NewGuid() },
            exact with { QueueNeighborRevision = 2 },
            exact with { QueueMoveReviewId = null }, exact with { QueueRunId = null },
            exact with { TaskId = null }, exact with { QueueNeighborId = null },
        })
        {
            var confirm = () => queue.ExecuteCommandAsync(malformed, RequestOrigin.LocalUi, () => true, f.Token);
            await confirm.Should().ThrowAsync<InvalidOperationException>();
        }

        var changedOrigin = () => queue.ExecuteCommandAsync(exact, RequestOrigin.ActivatedVoice, () => true, f.Token);
        await changedOrigin.Should().ThrowAsync<InvalidOperationException>();
        f.AdvanceRevision();
        var privateChanged = () => queue.ExecuteCommandAsync(exact, RequestOrigin.LocalUi, () => true, f.Token);
        await privateChanged.Should().ThrowAsync<InvalidOperationException>();
        f.QueueRevision.Should().Be(2);
        f.TaskWrites.Should().HaveCount(4);
    }

    [Fact]
    public async Task ReviewRefusesWrongSessionMissingOrHeldControlInsteadOfAnEmptyOrDefaultQueue()
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        await AddMovePair(f, queue);
        var exact = MoveCommand(f, true);
        foreach (var projection in new Func<SessionQueueSnapshot, SessionQueueSnapshot>[]
        {
            snapshot => snapshot with { SessionId = new(Guid.NewGuid()) },
            snapshot => snapshot with { DispatchControl = null },
            snapshot => snapshot with { DispatchControl = snapshot.DispatchControl! with { Held = true } },
        })
        {
            f.QueueReadProjection = projection;
            var review = () => queue.ExecuteCommandAsync(exact, RequestOrigin.LocalUi, () => true, f.Token);
            await review.Should().ThrowAsync<InvalidOperationException>();
        }
        f.QueueReadProjection = null;
        f.ExactListReader = _ => new(new(f.Session.SessionId, f.Session.Generation, false), null);
        var done = () => queue.ExecuteCommandAsync(exact, RequestOrigin.LocalUi, () => true, f.Token);
        await done.Should().ThrowAsync<InvalidOperationException>();
        f.ExactListReader = null;
        var current = (await queue.ExecuteCommandAsync(exact, RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
        var missing = () => queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueMoveCancel, f.Request.SessionId.Value),
            RequestOrigin.LocalUi, () => true, f.Token);
        await missing.Should().ThrowAsync<InvalidOperationException>();
        (await queue.ExecuteCommandAsync(ConfirmMove(current), RequestOrigin.LocalUi, () => true, f.Token)).Outcome.Should().Be("committed",
            "missing cancel identity revokes nothing and cannot retarget another review");
    }

    [Fact]
    public async Task ReviewRequiresExactQueueGenerationRunControlAndEntryRevisionsAndActualAdjacency()
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        await AddMovePair(f, queue);
        var exact = MoveCommand(f, true);
        foreach (var malformed in new[]
        {
            exact with { Generation = 2 }, exact with { QueueRevision = 99 },
            exact with { QueueRunId = Guid.NewGuid() }, exact with { QueueControlRevision = 1 },
            exact with { QueueRunId = null },
            exact with { TaskId = Guid.NewGuid() }, exact with { TaskRevision = 2 },
            exact with { QueueNeighborId = Guid.NewGuid() }, exact with { QueueNeighborRevision = 2 },
            exact with { QueueNeighborId = exact.TaskId },
            exact with { Operation = SessionCommandOperation.QueueMoveDown },
        })
        {
            var review = () => queue.ExecuteCommandAsync(malformed, RequestOrigin.LocalUi, () => true, f.Token);
            await review.Should().ThrowAsync<InvalidOperationException>();
        }
        f.AfterQueueRead = f.AdvanceRevision;
        var changing = () => queue.ExecuteCommandAsync(exact, RequestOrigin.LocalUi, () => true, f.Token);
        await changing.Should().ThrowAsync<InvalidOperationException>();
        f.AfterQueueRead = null;
        f.QueueRevision.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedPostCommitMoveDeliveryHoldsExactControlAndNeverPublishesSuccess(bool cancellation)
    {
        using var f = new Fixture();
        using var cancel = new CancellationTokenSource();
        await using var queue = f.QueueService();
        await AddMovePair(f, queue);
        var review = (await queue.ExecuteCommandAsync(MoveCommand(f, true), RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
        f.AfterMove = () => { if (cancellation) { cancel.Cancel(); } else { f.CanControl = false; } };
        var confirm = () => queue.ExecuteCommandAsync(ConfirmMove(review), RequestOrigin.LocalUi, () => true, cancel.Token);
        await confirm.Should().ThrowAsync<Exception>();
        (await f.ReadQueueAsync(f.Request.SessionId, f.Token)).DispatchControl!.Held.Should().BeTrue();
        f.TaskWrites.Last().State.Should().Be(HostTaskState.Succeeded);
        f.QueueActions.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentQueueOrPrivateMutationRefusesConfirmationWithDeniedTerminalNotAChangedNeighbor(bool privacy)
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        await AddMovePair(f, queue);
        var review = (await queue.ExecuteCommandAsync(MoveCommand(f, true), RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
        f.BeforeMove = () => { if (privacy) { f.AdvanceRevision(); } else { f.AdvanceQueueRevision(); } };
        var confirm = () => queue.ExecuteCommandAsync(ConfirmMove(review), RequestOrigin.LocalUi, () => true, f.Token);
        await confirm.Should().ThrowAsync<InvalidOperationException>();
        f.TaskWrites.Last().State.Should().Be(HostTaskState.Denied);
        f.QueueRows.Should().Equal(review.Queue.Entries);
    }

    private static async Task AddMovePair(Fixture f, SessionQueueService queue)
    {
        await queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueEnqueue), RequestOrigin.LocalUi, () => true, f.Token);
        await queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueEnqueue) with { QueueRevision = 1 },
            RequestOrigin.LocalUi, () => true, f.Token);
    }

    private static SessionCommand MoveCommand(Fixture f, bool up) => new(up ? SessionCommandOperation.QueueMoveUp : SessionCommandOperation.QueueMoveDown,
        f.Request.SessionId.Value, 1)
    {
        QueueRevision = f.QueueRevision, QueueRunId = f.QueueRun, QueueControlRevision = 0,
        TaskId = f.QueueRows[up ? 1 : 0].Request.TaskId.Value, TaskRevision = 1,
        QueueNeighborId = f.QueueRows[up ? 0 : 1].Request.TaskId.Value, QueueNeighborRevision = 1,
    };

    private static SessionCommand ConfirmMove(SessionQueueMoveReview review) => new(SessionCommandOperation.QueueMoveConfirm, review.Queue.SessionId.Value,
        review.Queue.Generation.Value)
    {
        QueueRevision = review.Queue.Revision, QueueRunId = review.Queue.DispatchControl!.RunId,
        QueueControlRevision = review.Queue.DispatchControl.Revision, TaskId = review.Selected.Request.TaskId.Value,
        TaskRevision = review.Selected.Revision.Value, QueueNeighborId = review.Neighbor.Request.TaskId.Value,
        QueueNeighborRevision = review.Neighbor.Revision.Value, QueueMoveReviewId = review.Id,
    };
}
