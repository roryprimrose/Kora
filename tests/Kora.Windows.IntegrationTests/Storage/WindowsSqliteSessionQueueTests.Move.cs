using System.Globalization;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionQueueTests
{
    [WindowsFact]
    public async Task ReviewedMixedProfileSwapChangesActualAdmissionAndEveryProjectionButPreservesOriginalBytesDeadlinesAndFairness()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var session = f.Request.SessionId;
        var other = (await WindowsSqliteSessionWorkspaceTests.Service(f, new()).CreateAsync(new("Independent"),
            RequestOrigin.LocalUi, f.Token)).Authority.SessionId;
        var first = await EnqueueAsync(f, session);
        var otherEntry = await EnqueueAsync(f, other);
        var second = await EnqueueAsync(f, session, profile: SessionQueueReadProfile.Capabilities);
        var third = await EnqueueAsync(f, session, profile: SessionQueueReadProfile.Readiness);
        var fourth = await EnqueueAsync(f, session, profile: SessionQueueReadProfile.Runtimes);
        var original = QueueBytes(f);
        var retention = await f.Store.ReadRetentionAsync(session, f.Token);
        using var actions = new FixedRegistryFixture(f.Time);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using var queue = new SessionQueueService(f.Store, f.Store, new(f.Tasks), access,
            new DeterministicVersionQueueAction(actions.Registry), new(), NullLogger<SessionQueueService>.Instance, f.Time);
        var before = await f.Store.ReadQueueAsync(session, f.Token);
        f.Time.Now = f.Time.Now.AddMinutes(1);
        var reviewed = await queue.ExecuteCommandAsync(MoveCommand(before, second, first, true), RequestOrigin.ActivatedVoice, () => true, f.Token);
        reviewed.Outcome.Should().Be("reviewed");
        QueueBytes(f).Should().Equal(original);
        var committed = await queue.ExecuteCommandAsync(ConfirmMove(reviewed.QueueMoveReview!), RequestOrigin.ActivatedVoice, () => true, f.Token);
        var ordered = committed.Queue!;
        ordered.Entries.Should().Equal(second, first, third, fourth);
        ordered.Revision.Should().BeGreaterThan(before.Revision);
        ordered.DispatchControl!.Paused.Should().BeFalse();
        ordered.DispatchControl.Held.Should().BeFalse();
        ordered.Orders.Should().ContainSingle().Which.Tasks.Should().Equal(second.Request.TaskId, first.Request.TaskId, third.Request.TaskId, fourth.Request.TaskId);
        QueueBytes(f).Should().Equal(original, "all queue payloads, entry revisions and original committed digests are unchanged");
        (await f.Store.ReadRetentionAsync(session, f.Token)).Should().Be(retention);
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().Be(second, "head Position is not a new global priority");
        var work = await f.Store.ReadWorkAsync(session, 1, new(), f.Token);
        work.QueueRecords.Where(row => row.Entry.IsPending).Select(row => row.Entry).Should().Equal(second, first, third, fourth);
        work.QueueRecords.Where(row => row.Entry.IsPending).Select(row => row.PendingOrder).Should().Equal(1, 2, 3, 4);
        work.QueueRecords.Single(row => row.Entry == second).Eligibility.Should().Be(SessionQueueEligibility.Ready);
        work.QueueRecords.Single(row => row.Entry == first).Eligibility.Should().Be(SessionQueueEligibility.EarlierPendingEntry);
        AuthorityLocalEventSource.FromWork(work, f.Time.Now).Single(item => item.TaskId == second.Request.TaskId).QueueReason.Should().Be(SessionQueueEligibility.Ready);
        var workspace = new SessionWorkspaceService(f.Store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance, queue);
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            var overview = await workspace.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token);
            overview.Records.Single(row => row.Session.Authority.SessionId == session).Work!.QueueRecords.Should().Equal(work.QueueRecords);
        }
        var receipts = (await queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueDispatch, session.Value, 1)
            { QueueRevision = ordered.Revision }, RequestOrigin.LocalUi, () => true, f.Token)).QueueDispatch!;
        receipts.Select(receipt => receipt.Entry.Request.TaskId).Should().Equal(second.Request.TaskId, otherEntry.Request.TaskId,
            first.Request.TaskId, third.Request.TaskId, fourth.Request.TaskId);
        receipts.Should().OnlyContain(receipt => receipt.Entry.State == SessionQueueState.Succeeded);
        receipts.Single(receipt => receipt.Entry.Request.TaskId == second.Request.TaskId).Entry.Position.Should().Be(second.Position);
        receipts.Single(receipt => receipt.Entry.Request.TaskId == second.Request.TaskId).Entry.ExpiresAt.Should().Be(second.ExpiresAt);
        actions.Probe.Calls.Should().Be(0);
        (await f.Store.ReadQueueAsync(session, f.Token)).Orders.Single().Tasks.Should().BeEmpty();
        using var raw = f.OpenRaw();
        using var audit = raw.CreateCommand();
        audit.CommandText = """
            SELECT COUNT(*) FROM security_audit_events
            WHERE json_extract(envelope,'$.Audit.ActionId')='queue.move-up'
                AND json_extract(envelope,'$.RequestedAudit.Outcome')=0
                AND length(json_extract(envelope,'$.TraceId'))=32
                AND length(json_extract(envelope,'$.SpanId'))=16;
            """;
        ((long)audit.ExecuteScalar()!).Should().Be(1);
    }

    [WindowsFact]
    public async Task PendingSwapCompletesWhileRunningCallbackIsHeldAndPreservesIndependentExplicitPause()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var first = await EnqueueAsync(f, f.Request.SessionId);
        var second = await EnqueueAsync(f, f.Request.SessionId);
        var third = await EnqueueAsync(f, f.Request.SessionId);
        using var action = new HeldPauseAction(f.Token);
        await using var queue = PauseQueue(f, new(), action);
        var before = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var dispatch = queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueDispatch, before.SessionId.Value, 1)
            { QueueRevision = before.Revision }, RequestOrigin.LocalUi, () => true, f.Token);
        await action.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
        try
        {
            var current = await f.Store.ReadQueueAsync(before.SessionId, f.Token);
            var running = current.Entries.Single(entry => entry.IsCurrent);
            var paused = (await queue.ExecuteCommandAsync(PauseCommand(current, true), RequestOrigin.LocalUi, () => true, f.Token)).Queue!;
            var review = (await queue.ExecuteCommandAsync(MoveCommand(paused, third, second, true),
                RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
            var moved = (await queue.ExecuteCommandAsync(ConfirmMove(review), RequestOrigin.LocalUi, () => true, f.Token)).Queue!;
            moved.DispatchControl!.Paused.Should().BeTrue();
            moved.Entries.Where(entry => entry.IsPending).Should().Equal(third, second);
            (await f.Store.ReadQueueEntryAsync(before.SessionId, first.Request.TaskId, f.Token)).Should().Be(running);
            dispatch.IsCompleted.Should().BeFalse("reorder owns a short mutation fence, not the held callback/batch");
            action.Release.Set();
            (await dispatch).QueueDispatch!.Select(receipt => receipt.Entry.Request.TaskId).Should().Equal(first.Request.TaskId);
            var resumed = (await queue.ExecuteCommandAsync(PauseCommand(await f.Store.ReadQueueAsync(before.SessionId, f.Token), false),
                RequestOrigin.LocalUi, () => true, f.Token)).Queue!;
            resumed.Entries.Should().Equal(third, second);
            (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().Be(third);
        }
        finally { action.Release.Set(); await dispatch; }
    }

    [WindowsFact]
    public async Task ReviewedOrderSurvivesRemovalEnqueueFullBoundsAndRestartWithoutReplayingWork()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var entries = new List<SessionQueueEntry>();
        for (var index = 0; index < 10; index++) { entries.Add(await EnqueueAsync(f, f.Request.SessionId)); }
        await using var queue = PauseQueue(f, new(), new NeverAction());
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var review = (await queue.ExecuteCommandAsync(MoveCommand(snapshot, entries[9], entries[8], true),
            RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
        var moved = (await queue.ExecuteCommandAsync(ConfirmMove(review), RequestOrigin.LocalUi, () => true, f.Token)).Queue!;
        var expected = entries.Take(8).Concat([entries[9], entries[8]]).ToArray();
        moved.Entries.Should().Equal(expected);
        var full = () => EnqueueAsync(f, f.Request.SessionId);
        await full.Should().ThrowAsync<InvalidOperationException>();
        await RemoveAsync(f, entries[0], moved.Revision);
        var added = await EnqueueAsync(f, f.Request.SessionId);
        var after = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        after.Entries.Should().Equal(expected.Skip(1).Append(added));
        after.Orders.Single().Tasks.Should().HaveCount(10).And.OnlyHaveUniqueItems();
        var order = after.Orders.Single();
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().BeNull();
        var previous = await f.Store.ReadWorkAsync(f.Request.SessionId, 1, new(), f.Token);
        previous.Queue.Orders.Single().Should().BeEquivalentTo(order);
        previous.QueueRecords.Where(row => row.Entry.State == SessionQueueState.Interrupted).Select(row => row.Entry.Request.TaskId)
            .Should().Equal(order.Tasks);
        previous.QueueRecords.Where(row => row.Entry.State == SessionQueueState.Interrupted)
            .Should().OnlyContain(row => row.Eligibility == SessionQueueEligibility.InterruptedNoReplay);
        var stale = () => queue.ExecuteCommandAsync(ConfirmMove(review), RequestOrigin.LocalUi, () => true, f.Token);
        await stale.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("admission")]
    [InlineData("remove")]
    [InlineData("enqueue")]
    [InlineData("reorder")]
    public async Task ConcurrentAuthoritativeMutationInvalidatesReviewedPairRatherThanChoosingAnotherNeighbor(string mutation)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var first = await EnqueueAsync(f, f.Request.SessionId);
        var second = await EnqueueAsync(f, f.Request.SessionId);
        var third = await EnqueueAsync(f, f.Request.SessionId);
        await using var queue = PauseQueue(f, new(), new NeverAction());
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var review = (await queue.ExecuteCommandAsync(MoveCommand(snapshot, second, first, true),
            RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
        if (mutation is "admission") { await AdmitAsync(f, first); }
        if (mutation is "remove") { await RemoveAsync(f, first, snapshot.Revision); }
        if (mutation is "enqueue") { await EnqueueAsync(f, f.Request.SessionId); }
        if (mutation is "reorder")
        {
            await using var otherController = PauseQueue(f, new(), new NeverAction());
            var other = (await otherController.ExecuteCommandAsync(MoveCommand(snapshot, third, second, true),
                RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
            await otherController.ExecuteCommandAsync(ConfirmMove(other), RequestOrigin.LocalUi, () => true, f.Token);
        }
        var beforeConfirm = QueueBytes(f);
        var confirm = () => queue.ExecuteCommandAsync(ConfirmMove(review), RequestOrigin.LocalUi, () => true, f.Token);
        await confirm.Should().ThrowAsync<InvalidOperationException>();
        QueueBytes(f).Should().Equal(beforeConfirm);
        var actual = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        actual.DispatchControl!.Held.Should().BeFalse("stale refusals do not silently pause an otherwise unchanged control");
    }

    [WindowsFact]
    public async Task PendingDependencyInversionAndExpiredOrPrivateRetiredPairAreExplicitlyRefusedButCrossSessionBlockRemainsTruthful()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var first = await EnqueueAsync(f, f.Request.SessionId);
        var second = await EnqueueAsync(f, f.Request.SessionId, dependency: first.Request.TaskId);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using var queue = PauseQueue(f, access, new NeverAction());
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var inversion = () => queue.ExecuteCommandAsync(MoveCommand(snapshot, second, first, true), RequestOrigin.LocalUi, () => true, f.Token);
        await inversion.Should().ThrowAsync<InvalidOperationException>().WithMessage("*prerequisite*");
        var other = (await WindowsSqliteSessionWorkspaceTests.Service(f, new()).CreateAsync(new("Other"),
            RequestOrigin.LocalUi, f.Token)).Authority.SessionId;
        var blocked = await EnqueueAsync(f, other, dependency: first.Request.TaskId);
        var ready = await EnqueueAsync(f, other);
        var otherSnapshot = await f.Store.ReadQueueAsync(other, f.Token);
        var review = (await queue.ExecuteCommandAsync(MoveCommand(otherSnapshot, blocked, ready, false),
            RequestOrigin.LocalUi, () => true, f.Token)).QueueMoveReview!;
        await queue.ExecuteCommandAsync(ConfirmMove(review), RequestOrigin.LocalUi, () => true, f.Token);
        var work = await f.Store.ReadWorkAsync(other, 1, new(), f.Token);
        work.QueueRecords.Single(row => row.Entry == blocked).Eligibility.Should().Be(SessionQueueEligibility.EarlierPendingEntry);
        await RemoveAsync(f, ready, work.Queue.Revision);
        (await f.Store.ReadWorkAsync(other, 1, new(), f.Token)).QueueRecords.Single(row => row.Entry == blocked)
            .Eligibility.Should().Be(SessionQueueEligibility.DependencyNotSucceeded);
        access.ControlRevision++;
        var retired = () => queue.ExecuteCommandAsync(MoveCommand(snapshot, first, second, false), RequestOrigin.LocalUi, () => true, f.Token);
        await retired.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Private admission*");
        access.ControlRevision--;
        f.Time.Now = first.ExpiresAt;
        var expiredSnapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var expired = () => queue.ExecuteCommandAsync(MoveCommand(expiredSnapshot, first, second, false), RequestOrigin.LocalUi, () => true, f.Token);
        await expired.Should().ThrowAsync<InvalidOperationException>();
    }

    internal static SessionCommand MoveCommand(SessionQueueSnapshot snapshot, SessionQueueEntry selected, SessionQueueEntry neighbor, bool up) =>
        new(up ? SessionCommandOperation.QueueMoveUp : SessionCommandOperation.QueueMoveDown, snapshot.SessionId.Value, snapshot.Generation.Value)
        {
            QueueRevision = snapshot.Revision, QueueRunId = snapshot.DispatchControl!.RunId,
            QueueControlRevision = snapshot.DispatchControl.Revision, TaskId = selected.Request.TaskId.Value,
            TaskRevision = selected.Revision.Value, QueueNeighborId = neighbor.Request.TaskId.Value, QueueNeighborRevision = neighbor.Revision.Value,
        };

    internal static SessionCommand ConfirmMove(SessionQueueMoveReview review) =>
        MoveCommand(review.Queue, review.Selected, review.Neighbor, review.Up) with
        { Operation = SessionCommandOperation.QueueMoveConfirm, QueueMoveReviewId = review.Id };

    private static string[] QueueBytes(InteractionStorageFixture f)
    {
        using var connection = f.OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT task_id,payload,audit_sequence FROM session_queue ORDER BY task_id;";
        using var rows = command.ExecuteReader();
        var payloads = new List<string>();
        while (rows.Read()) { payloads.Add(rows.GetString(0) + "|" + rows.GetString(1) + "|" + rows.GetInt64(2).ToString(CultureInfo.InvariantCulture)); }
        return [.. payloads];
    }
}
