using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, 1)]
    [InlineData(RequestOrigin.ActivatedVoice, 10)]
    public async Task SelectedPendingReviewAndDiscardWriteNothingThenExactConfirmationUsesOneSharedAtomicControl(RequestOrigin origin, int count)
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        var workspace = new Kora.Application.Hosting.SessionWorkspaceService(f, new(f), f, f.Logger, queue);
        for (var index = 0; index < count; index++)
        {
            await queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueEnqueue) with
                { QueueRevision = f.QueueRevision, QueueProfile = (SessionQueueReadProfile)(index % 4) }, origin, () => true, f.Token);
        }
        var snapshot = await f.ReadQueueAsync(f.Request.SessionId, f.Token);
        var command = CancelSelection(snapshot, snapshot.Entries);
        var writes = f.TaskWrites.Count;
        var review = (await workspace.ExecuteCommandAsync(command, origin, () => true, f.Token)).QueueCancelReview!;
        f.TaskWrites.Should().HaveCount(writes);
        f.QueueRows.Should().Equal(snapshot.Entries);
        var discarded = await workspace.ExecuteCommandAsync(new(SessionCommandOperation.QueueCancelSelectedDiscard, snapshot.SessionId.Value)
            { QueueCancelReviewId = review.Id }, origin, () => true, f.Token);
        discarded.Outcome.Should().Be("cancelled-review");
        f.TaskWrites.Should().HaveCount(writes);
        var retired = () => queue.ExecuteCommandAsync(ConfirmCancellation(review), origin, () => true, f.Token);
        await retired.Should().ThrowAsync<InvalidOperationException>();
        review = (await queue.ExecuteCommandAsync(command, origin, () => true, f.Token)).QueueCancelReview!;
        var result = await workspace.ExecuteCommandAsync(ConfirmCancellation(review), origin, () => true, f.Token);
        result.Outcome.Should().Be("committed");
        result.QueueCancelReceipt!.Entries.Should().Equal(snapshot.Entries);
        result.Queue!.DispatchControl.Should().Be(snapshot.DispatchControl);
        f.QueueRows.Should().OnlyContain(entry => entry.State == SessionQueueState.Cancelled);
        f.QueueActions.Should().Be(0);
        f.TaskWrites.Skip(writes).Should().HaveCount(2).And.OnlyContain(task => task.Request.Origin == origin);
        var replay = () => queue.ExecuteCommandAsync(ConfirmCancellation(review), origin, () => true, f.Token);
        await replay.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task EveryChangedConfirmationBindingRevokesWholeProposalWithoutPartialCancellation()
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        await AddMovePair(f, queue);
        var snapshot = await f.ReadQueueAsync(f.Request.SessionId, f.Token);
        var command = CancelSelection(snapshot, snapshot.Entries);
        foreach (var boundary in new[] { "review", "missing-review", "epoch", "generation", "queue", "run", "missing-run", "pause", "origin", "selection", "member", "empty" })
        {
            var review = (await queue.ExecuteCommandAsync(command, RequestOrigin.LocalUi, () => true, f.Token)).QueueCancelReview!;
            var exact = ConfirmCancellation(review);
            var invalid = boundary switch
            {
                "review" => exact with { QueueCancelReviewId = Guid.NewGuid() },
                "missing-review" => exact with { QueueCancelReviewId = null },
                "generation" => exact with { Generation = 2 },
                "queue" => exact with { QueueRevision = 99 },
                "run" => exact with { QueueRunId = Guid.NewGuid() },
                "missing-run" => exact with { QueueRunId = null },
                "pause" => exact with { QueueControlRevision = 1 },
                "selection" => exact with { QueueSelection = [.. exact.QueueSelection.Reverse()] },
                "member" => exact with { QueueSelection = [exact.QueueSelection[0], exact.QueueSelection[1] with { Revision = new(2) }] },
                "empty" => exact with { QueueSelection = [] },
                _ => exact,
            };
            if (boundary is "epoch") { f.AdvanceRevision(); }
            var confirm = () => queue.ExecuteCommandAsync(invalid,
                boundary is "origin" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi, () => true, f.Token);
            await confirm.Should().ThrowAsync<InvalidOperationException>();
            f.QueueRows.Should().Equal(snapshot.Entries);
            var replay = () => queue.ExecuteCommandAsync(exact, RequestOrigin.LocalUi, () => true, f.Token);
            await replay.Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [Fact]
    public async Task ReviewRequiresWholeExactObservedCurrentSessionControlAndCompleteOutput()
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        await AddMovePair(f, queue);
        var snapshot = await f.ReadQueueAsync(f.Request.SessionId, f.Token);
        var exact = CancelSelection(snapshot, snapshot.Entries);
        foreach (var invalid in new[]
        {
            exact with { Generation = 2 }, exact with { QueueRevision = 99 }, exact with { QueueRunId = Guid.NewGuid() },
            exact with { QueueRunId = null },
            exact with { QueueControlRevision = 99 }, exact with { QueueSelection = [] },
        })
        {
            var review = () => queue.ExecuteCommandAsync(invalid, RequestOrigin.LocalUi, () => true, f.Token);
            await review.Should().ThrowAsync<InvalidOperationException>();
        }
        foreach (var projection in new Func<SessionQueueSnapshot, SessionQueueSnapshot>[]
        {
            value => value with { SessionId = new(Guid.NewGuid()) },
            value => value with { DispatchControl = null },
        })
        {
            f.QueueReadProjection = projection;
            var review = () => queue.ExecuteCommandAsync(exact, RequestOrigin.LocalUi, () => true, f.Token);
            await review.Should().ThrowAsync<InvalidOperationException>();
        }
        f.QueueReadProjection = value => value with { Entries = Enumerable.Repeat(value.Entries[0], 300).Concat(value.Entries.Skip(1)).ToArray() };
        var oversized = () => queue.ExecuteCommandAsync(exact, RequestOrigin.LocalUi, () => true, f.Token);
        await oversized.Should().ThrowAsync<InvalidDataException>();
        f.QueueReadProjection = null;
        var writes = f.TaskWrites.Count;
        var normal = (await queue.ExecuteCommandAsync(exact, RequestOrigin.LocalUi, () => true, f.Token)).QueueCancelReview!;
        foreach (var changed in new[] { "id", "origin", "epoch" })
        {
            if (changed is "epoch") { f.AdvanceRevision(); }
            var discard = () => queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueCancelSelectedDiscard, snapshot.SessionId.Value)
                { QueueCancelReviewId = changed is "id" ? Guid.NewGuid() : normal.Id },
                changed is "origin" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi, () => true, f.Token);
            await discard.Should().ThrowAsync<InvalidOperationException>();
        }
        f.TaskWrites.Should().HaveCount(writes);
        var missing = () => queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueCancelSelectedDiscard, snapshot.SessionId.Value),
            RequestOrigin.LocalUi, () => true, f.Token);
        await missing.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task PendingCancellationReviewRequiresActiveSessionAndCurrentPrivatePauseEpochButDoesNotChangePause()
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        await AddMovePair(f, queue);
        var snapshot = await f.ReadQueueAsync(f.Request.SessionId, f.Token);
        var paused = await queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueuePause) with
            { QueueRevision = snapshot.Revision, QueueRunId = snapshot.DispatchControl!.RunId },
            RequestOrigin.LocalUi, () => true, f.Token);
        var exact = CancelSelection(paused.Queue!, paused.Queue!.Entries);
        var review = (await queue.ExecuteCommandAsync(exact, RequestOrigin.LocalUi, () => true, f.Token)).QueueCancelReview!;
        var missingId = () => queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueCancelSelectedDiscard, snapshot.SessionId.Value),
            RequestOrigin.LocalUi, () => true, f.Token);
        await missingId.Should().ThrowAsync<InvalidOperationException>();
        await queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueCancelSelectedDiscard, snapshot.SessionId.Value)
            { QueueCancelReviewId = review.Id }, RequestOrigin.LocalUi, () => true, f.Token);
        f.AdvanceRevision();
        var staleEpoch = () => queue.ExecuteCommandAsync(exact, RequestOrigin.LocalUi, () => true, f.Token);
        await staleEpoch.Should().ThrowAsync<InvalidOperationException>();
        f.ExactListReader = _ => new(f.Session with { IsActive = false }, null);
        var done = () => queue.ExecuteCommandAsync(exact, RequestOrigin.LocalUi, () => true, f.Token);
        await done.Should().ThrowAsync<InvalidOperationException>().WithMessage("*active session*");
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("failure")]
    [InlineData("cancel")]
    [InlineData("privacy")]
    public async Task ChangedPhysicalFenceOrFailedDeliveryNeverPublishesPartialOrSuccessShapedResults(string boundary)
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        await using var queue = f.QueueService();
        await AddMovePair(f, queue);
        var review = (await queue.ExecuteCommandAsync(CancelSelection(await f.ReadQueueAsync(f.Request.SessionId, f.Token), f.QueueRows),
            RequestOrigin.LocalUi, () => true, f.Token)).QueueCancelReview!;
        if (boundary is "stale") { f.BeforeMove = f.AdvanceQueueRevision; }
        if (boundary is "failure") { f.QueueFailure = "cancel-selected"; }
        if (boundary is "cancel") { f.AfterMove = cancellation.Cancel; }
        if (boundary is "privacy") { f.AfterMove = () => f.CanControl = false; }
        var confirm = () => queue.ExecuteCommandAsync(ConfirmCancellation(review), RequestOrigin.LocalUi, () => true, cancellation.Token);
        await confirm.Should().ThrowAsync<Exception>();
        if (boundary is "cancel" or "privacy")
        {
            f.QueueRows.Should().OnlyContain(entry => entry.State == SessionQueueState.Cancelled);
            (await f.ReadQueueAsync(f.Request.SessionId, f.Token)).DispatchControl!.Held.Should().BeTrue();
            f.TaskWrites.Last().State.Should().Be(HostTaskState.Succeeded, "known COMMIT cannot be undone by cancellation during delivery");
        }
        else { f.QueueRows.Should().Equal(review.Entries); }
    }

    private static SessionCommand CancelSelection(SessionQueueSnapshot snapshot, IEnumerable<SessionQueueEntry> entries) =>
        new(SessionCommandOperation.QueueCancelSelectedReview, snapshot.SessionId.Value, snapshot.Generation.Value)
        {
            QueueRevision = snapshot.Revision, QueueRunId = snapshot.DispatchControl!.RunId,
            QueueControlRevision = snapshot.DispatchControl.Revision,
            QueueSelection = [.. entries.Select(entry => new SessionQueueSelection(entry.Request.TaskId, entry.Revision))],
        };

    private static SessionCommand ConfirmCancellation(SessionQueueCancelReview review) =>
        CancelSelection(review.Queue, review.Entries) with
        { Operation = SessionCommandOperation.QueueCancelSelectedConfirm, QueueCancelReviewId = review.Id };
}
