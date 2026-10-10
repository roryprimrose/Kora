using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed partial class WindowsSqliteSessionQueueTests
{
    [WindowsFact]
    public async Task Lowered_capacity_and_slots_hold_future_admission_without_eviction_reclassification_or_deadline_rewrite()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var session = fixture.Request.SessionId;
        var first = await EnqueueAsync(fixture, session);
        var second = await EnqueueAsync(fixture, session);
        var before = await fixture.Store.ReadQueueAsync(session, fixture.Token);
        var full = () => EnqueueAsync(fixture, session, pending: 1);
        await full.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.Store.ReadQueueAsync(session, fixture.Token)).Should().BeEquivalentTo(before);
        var other = (await WindowsSqliteSessionWorkspaceTests.Service(fixture, new())
            .CreateAsync(new("Independent fixed read"), RequestOrigin.LocalUi, fixture.Token)).Authority.SessionId;
        var otherEntry = await EnqueueAsync(fixture, other);
        var runningFirst = await AdmitAsync(fixture, first, slots: 2);
        var runningOther = await AdmitAsync(fixture, otherEntry, slots: 2);
        var activeBefore = await fixture.Store.ReadWorkAsync(session, 1, new(executionSlots: 2), fixture.Token);
        fixture.Time.Now = fixture.Time.Now.AddMinutes(1);
        var activeAfter = await fixture.Store.ReadWorkAsync(session, 1, new(1, 1), fixture.Token);
        activeAfter.PendingCapacity.Should().Be(1);
        activeAfter.ExecutionSlots.Should().Be(1);
        activeAfter.QueueRecords.Single(row => row.Entry.Request.TaskId == first.Request.TaskId).ActiveDeadline
            .Should().Be(activeBefore.QueueRecords.Single(row => row.Entry.Request.TaskId == first.Request.TaskId).ActiveDeadline);
        activeAfter.QueueRecords.Single(row => row.Entry.Request.TaskId == second.Request.TaskId).Entry.ExpiresAt.Should().Be(second.ExpiresAt);
        (await fixture.Store.FindReadyAsync(1, new(1, 1), fixture.Token)).Should().BeNull();
        await CompleteAsync(fixture, runningFirst);
        (await fixture.Store.FindReadyAsync(1, new(1, 1), fixture.Token)).Should().BeNull();
        await CompleteAsync(fixture, runningOther);
        (await fixture.Store.FindReadyAsync(1, new(1, 1), fixture.Token)).Should().Be(second);
    }

    [WindowsFact]
    public async Task Fair_exact_FIFO_admission_preserves_history_and_one_current_task_per_session()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var first = fixture.Request.SessionId;
        var other = (await WindowsSqliteSessionWorkspaceTests.Service(fixture, new())
            .CreateAsync(new("Same label is not routing"), RequestOrigin.LocalUi, fixture.Token)).Authority.SessionId;
        var a1 = await EnqueueAsync(fixture, first);
        var a2 = await EnqueueAsync(fixture, first);
        var b1 = await EnqueueAsync(fixture, other);
        (await fixture.Store.FindReadyAsync(1, new(), fixture.Token)).Should().Be(a1);
        var admittedA = await AdmitAsync(fixture, a1, slots: 2);
        (await fixture.Store.FindReadyAsync(1, new(), fixture.Token)).Should().BeNull();
        (await fixture.Store.FindReadyAsync(1, new(executionSlots: 2), fixture.Token)).Should().Be(b1);
        var admittedB = await AdmitAsync(fixture, b1, slots: 2);
        (await fixture.Store.FindReadyAsync(1, new(executionSlots: 2), fixture.Token)).Should().BeNull();
        var stale = () => AdmitAsync(fixture, a2, slots: 2);
        await stale.Should().ThrowAsync<InvalidOperationException>();
        await CompleteAsync(fixture, admittedA);
        (await fixture.Store.FindReadyAsync(1, new(executionSlots: 2), fixture.Token)).Should().Be(a2);
        await CompleteAsync(fixture, admittedB);
        var admittedNext = await AdmitAsync(fixture, a2);
        await CompleteAsync(fixture, admittedNext);
        (await fixture.Store.ReadQueueAsync(first, fixture.Token)).Entries.Should().BeEmpty();
        (await fixture.Store.ReadQueueEntryAsync(first, a1.Request.TaskId, fixture.Token))!.State.Should().Be(SessionQueueState.Succeeded);
        var history = await fixture.Store.ReadHistoryAsync(first, null, 50, fixture.Token);
        history.Records.Where(row => row.TaskId == a1.Request.TaskId.Value).Should().HaveCount(3);
        var late = () => CompleteAsync(fixture, admittedA);
        await late.Should().ThrowAsync<Exception>();
    }

    [WindowsFact]
    public async Task Pending_capacity_duplicate_request_task_concurrent_revisions_and_cancel_races_fail_closed()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var entry = await EnqueueAsync(fixture, fixture.Request.SessionId, pending: 1);
        var full = () => EnqueueAsync(fixture, fixture.Request.SessionId, pending: 1);
        await full.Should().ThrowAsync<InvalidOperationException>();
        var duplicate = () => EnqueueAsync(fixture, fixture.Request.SessionId, work: entry.Request);
        await duplicate.Should().ThrowAsync<InvalidOperationException>();
        var snapshot = await fixture.Store.ReadQueueAsync(fixture.Request.SessionId, fixture.Token);
        var revisions = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { await EnqueueAsync(fixture, fixture.Request.SessionId, expected: snapshot.Revision); return true; }
            catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException) { return false; }
        }));
        revisions.Should().ContainSingle(value => value);
        snapshot = await fixture.Store.ReadQueueAsync(fixture.Request.SessionId, fixture.Token);
        var stale = () => RemoveAsync(fixture, entry, snapshot.Revision, entryRevision: 99);
        await stale.Should().ThrowAsync<InvalidOperationException>();
        await RemoveAsync(fixture, entry, snapshot.Revision);
        (await fixture.Store.ReadQueueEntryAsync(entry.Request.SessionId, entry.Request.TaskId, fixture.Token))!.State
            .Should().Be(SessionQueueState.Cancelled);
        var ready = (await fixture.Store.FindReadyAsync(1, new(), fixture.Token))!;
        var admitted = await AdmitAsync(fixture, ready);
        snapshot = await fixture.Store.ReadQueueAsync(ready.Request.SessionId, fixture.Token);
        var afterAdmission = () => RemoveAsync(fixture, admitted, snapshot.Revision);
        await afterAdmission.Should().ThrowAsync<InvalidOperationException>();
        await CompleteAsync(fixture, admitted);
    }

    [WindowsFact]
    public async Task Racing_cancel_and_admission_have_one_transactional_winner_and_no_dispatch_after_cancel()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var entry = await EnqueueAsync(fixture, fixture.Request.SessionId);
        var revision = (await fixture.Store.ReadQueueAsync(entry.Request.SessionId, fixture.Token)).Revision;
        var admit = Task.Run(async () =>
        {
            try { await AdmitAsync(fixture, entry); return true; }
            catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException) { return false; }
        }, fixture.Token);
        var cancel = Task.Run(async () =>
        {
            try { await RemoveAsync(fixture, entry, revision); return true; }
            catch (InvalidOperationException) { return false; }
        }, fixture.Token);
        (await Task.WhenAll(admit, cancel)).Should().ContainSingle(won => won);
        var actual = (await fixture.Store.ReadQueueEntryAsync(entry.Request.SessionId, entry.Request.TaskId, fixture.Token))!;
        actual.State.Should().BeOneOf(SessionQueueState.Running, SessionQueueState.Cancelled);
        if (actual.State == SessionQueueState.Cancelled)
        {
            var late = () => AdmitAsync(fixture, entry);
            await late.Should().ThrowAsync<Exception>();
        }
        else { await CompleteAsync(fixture, actual); }
    }

    [WindowsFact]
    public async Task Unknown_quarantines_addressed_and_dependent_work_but_unrelated_sessions_progress()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        var dependentSession = (await service.CreateAsync(new("Dependent"), RequestOrigin.LocalUi, fixture.Token)).Authority.SessionId;
        var independentSession = (await service.CreateAsync(new("Independent"), RequestOrigin.LocalUi, fixture.Token)).Authority.SessionId;
        var first = await EnqueueAsync(fixture, fixture.Request.SessionId);
        await EnqueueAsync(fixture, fixture.Request.SessionId);
        await EnqueueAsync(fixture, dependentSession, dependency: first.Request.TaskId);
        var independent = await EnqueueAsync(fixture, independentSession);
        var admitted = await AdmitAsync(fixture, first);
        await CompleteAsync(fixture, admitted, SessionQueueState.Unknown);
        (await fixture.Store.FindReadyAsync(1, new(), fixture.Token)).Should().Be(independent);
        var unknown = (await fixture.Store.ReadQueueAsync(first.Request.SessionId, fixture.Token)).Entries;
        unknown.Should().Contain(entry => entry.State == SessionQueueState.Unknown);
        var done = () => service.ChangeLifecycleAsync(first.Request.SessionId, new(1), false, RequestOrigin.LocalUi, fixture.Token);
        await done.Should().ThrowAsync<InvalidOperationException>();
    }

    [WindowsFact]
    public async Task Expiry_and_admitted_question_waits_block_only_their_session_without_extending_activity()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var waitingSession = fixture.Request.SessionId;
        await fixture.RunAsync(() => fixture.Questions.CreateAsync(fixture.Request,
            new("Explicit user question", Kora.Core.Interaction.QuestionKind.SingleChoice, [new("yes", "Yes")]),
            fixture.Time.Now.AddMinutes(10), fixture.Token));
        var other = (await WindowsSqliteSessionWorkspaceTests.Service(fixture, new())
            .CreateAsync(new("Independent"), RequestOrigin.LocalUi, fixture.Token)).Authority.SessionId;
        var waiting = await EnqueueAsync(fixture, waitingSession);
        var independent = await EnqueueAsync(fixture, other);
        (await fixture.Store.FindReadyAsync(1, new(), fixture.Token)).Should().Be(independent);
        fixture.Time.Now = fixture.Time.Now.AddMinutes(30);
        (await fixture.Store.FindReadyAsync(1, new(), fixture.Token)).Should().BeNull();
        var expired = (await fixture.Store.ReadQueueAsync(other, fixture.Token)).Entries.Single();
        expired.State.Should().Be(SessionQueueState.Expired);
        expired.ExpiresAt.Should().Be(independent.ExpiresAt);
        var state = await fixture.Store.ReadSessionAsync(waitingSession, fixture.Token);
        state!.Generation.Should().Be(waiting.Generation);
    }

    [WindowsFact]
    public async Task Restart_interrupts_pending_marks_dispatch_unknown_and_never_replays_or_reuses_old_callbacks()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var pending = await EnqueueAsync(fixture, fixture.Request.SessionId);
        var other = (await WindowsSqliteSessionWorkspaceTests.Service(fixture, new())
            .CreateAsync(new("Other"), RequestOrigin.LocalUi, fixture.Token)).Authority.SessionId;
        var running = await EnqueueAsync(fixture, other);
        var admitted = await AdmitAsync(fixture, pending);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        await new HostTaskCoordinator(fixture.Tasks).RecoverAsync(100, fixture.Token);
        (await fixture.Store.ReadQueueEntryAsync(other, running.Request.TaskId, fixture.Token))!.State.Should().Be(SessionQueueState.Interrupted);
        (await fixture.Store.ReadQueueEntryAsync(pending.Request.SessionId, pending.Request.TaskId, fixture.Token))!.State.Should().Be(SessionQueueState.Unknown);
        (await fixture.Store.FindReadyAsync(1, new(), fixture.Token)).Should().BeNull();
        var late = () => CompleteAsync(fixture, admitted);
        await late.Should().ThrowAsync<Exception>();
        var fresh = await EnqueueAsync(fixture, other);
        (await fixture.Store.FindReadyAsync(1, new(), fixture.Token)).Should().Be(fresh);
    }

    [WindowsFact]
    public async Task Queue_cannot_bypass_gateway_and_private_commit_or_audit_failures_leave_no_admitted_receipt()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var entry = await EnqueueAsync(fixture, fixture.Request.SessionId);
        using (var root = HostActivity.BeginRoot(entry.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            var bypass = () => new HostTaskCoordinator(fixture.Tasks).RecordDispatchAsync(new(entry.Request, new(1), HostTaskState.IntentRecorded),
                fixture.Token).AsTask();
            await bypass.Should().ThrowAsync<InvalidOperationException>();
        }
        using (var root = HostActivity.BeginRoot(entry.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            var denied = () => fixture.Store.AdmitAsync(entry, 1, new(), () => false, fixture.Token).AsTask();
            await denied.Should().ThrowAsync<InvalidOperationException>();
        }
        (await fixture.Store.ReadQueueEntryAsync(entry.Request.SessionId, entry.Request.TaskId, fixture.Token))!.State.Should().Be(SessionQueueState.Pending);
        var audits = fixture.Count("security_audit_events");
        fixture.Reopen(new InteractionTransactionCheckpoint { Audit = (_, _) => throw new IOException("Audit unavailable") });
        await fixture.Store.InitializeAsync(fixture.Token);
        var fresh = await EnqueueAsync(fixture, fixture.Request.SessionId, fail: true);
        fresh.Should().BeNull();
        fixture.Count("security_audit_events").Should().Be(audits);
    }

    [WindowsFact]
    public async Task V4_migration_preserves_ordered_history_and_refuses_missing_committed_queue_authority()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var before = await fixture.Store.ReadHistoryAsync(fixture.Request.SessionId, null, 50, fixture.Token);
        fixture.Mutate("DROP TABLE session_file; DROP TABLE reviewed_memory; DROP TABLE memory_profile; DROP TABLE session_retention; DROP TABLE session_queue; PRAGMA user_version=4;");
        fixture.Reopen(new InteractionTransactionCheckpoint { Commit = (_, _) => throw new IOException("Migration interrupted") });
        var interrupted = () => fixture.Store.InitializeAsync(fixture.Token).AsTask();
        await interrupted.Should().ThrowAsync<IOException>();
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        (await fixture.Store.ReadHistoryAsync(fixture.Request.SessionId, null, 50, fixture.Token)).Should().BeEquivalentTo(before);
        var entry = await EnqueueAsync(fixture, fixture.Request.SessionId);
        fixture.Mutate("DROP TABLE session_file; DROP TABLE reviewed_memory; DROP TABLE memory_profile; DROP TABLE session_retention; DROP TABLE session_queue; PRAGMA user_version=4;");
        fixture.Reopen();
        var missing = () => fixture.Store.InitializeAsync(fixture.Token).AsTask();
        await missing.Should().ThrowAsync<InvalidDataException>();
        entry.Request.SessionId.Should().Be(fixture.Request.SessionId);
    }

    [WindowsFact]
    public async Task Clear_is_exact_pending_only_and_disposition_keeps_content_free_receipts_and_independent_grants()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var grant = await fixture.GrantAsync("perpetual");
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var entry = await EnqueueAsync(fixture, fixture.Request.SessionId);
        var snapshot = await fixture.Store.ReadQueueAsync(entry.Request.SessionId, fixture.Token);
        await ControlAsync(fixture, entry.Request.SessionId, request => fixture.Store.RemovePendingAsync(request,
            new(1), snapshot.Revision, null, null, SessionQueueState.Removed, () => true, fixture.Token));
        var workspace = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var preview = await workspace.PreviewDispositionAsync(entry.Request.SessionId, new(1), 0, fixture.Token);
        await workspace.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
        var denied = () => fixture.Store.ReadQueueAsync(entry.Request.SessionId, fixture.Token).AsTask();
        await denied.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().Contain(grant);
        fixture.Count("session_queue").Should().Be(1);
        var history = await fixture.Store.ReadHistoryAsync(entry.Request.SessionId, null, 50, fixture.Token);
        history.Disposed.Should().BeTrue();
        fixture.Mutate("DELETE FROM session_queue;");
        fixture.Reopen();
        var missing = () => fixture.Store.InitializeAsync(fixture.Token).AsTask();
        await missing.Should().ThrowAsync<InvalidDataException>();
    }

    private static async Task<SessionQueueEntry> EnqueueAsync(InteractionStorageFixture fixture, HostId<SessionIdentity> session,
        int pending = 10, HostRequest? work = null, long? expected = null, HostId<TaskIdentity>? dependency = null, bool fail = false,
        int minutes = SessionQueueLimits.DefaultPendingLifetimeMinutes)
    {
        var snapshot = await fixture.Store.ReadQueueAsync(session, fixture.Token);
        var queued = work ?? new HostRequest(new(Guid.NewGuid()), session, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        try
        {
            var accepted = await ControlAsync(fixture, session, control => fixture.Store.EnqueueAsync(control, queued,
                new(1), expected ?? snapshot.Revision, 1, dependency, new(pending, pendingLifetimeMinutes: minutes), () => true, fixture.Token));
            return accepted.Entries.Single(entry => entry.Request.TaskId == queued.TaskId);
        }
        catch (IOException) when (fail) { return null!; }
    }

    private static async Task<T> ControlAsync<T>(InteractionStorageFixture fixture, HostId<SessionIdentity> session,
        Func<HostRequest, ValueTask<T>> mutation)
    {
        var control = new HostRequest(new(Guid.NewGuid()), session, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        using var root = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request);
        var intent = await fixture.Store.RecordControlIntentAsync(control, fixture.Token);
        try
        {
            var result = await mutation(control);
            await new HostTaskCoordinator(fixture.Tasks).RecordOutcomeAsync(intent, HostTaskState.Succeeded, fixture.Token);
            return result;
        }
        catch (InvalidOperationException)
        {
            await new HostTaskCoordinator(fixture.Tasks).RecordOutcomeAsync(intent, HostTaskState.Denied, fixture.Token);
            throw;
        }
    }

    private static async Task<SessionQueueEntry> AdmitAsync(InteractionStorageFixture fixture, SessionQueueEntry entry, int slots = 1, int budget = 5)
    {
        using var root = HostActivity.BeginRoot(entry.Request, HostActivityLayer.Application, HostOperation.Request);
        return (await fixture.Store.AdmitAsync(entry, 1, new(executionSlots: slots, activeBudgetMinutes: budget), () => true, fixture.Token)).Entry;
    }

    private static Task<SessionQueueSnapshot> RemoveAsync(InteractionStorageFixture fixture, SessionQueueEntry entry,
        long expectedRevision, long entryRevision = 1) =>
        ControlAsync(fixture, entry.Request.SessionId, request => fixture.Store.RemovePendingAsync(request,
            entry.Generation, expectedRevision, entry.Request.TaskId, new(entryRevision), SessionQueueState.Cancelled,
            () => true, fixture.Token));

    private static async Task<SessionQueueEntry> CompleteAsync(InteractionStorageFixture fixture, SessionQueueEntry entry,
        SessionQueueState outcome = SessionQueueState.Succeeded)
    {
        using var root = HostActivity.BeginRoot(entry.Request, HostActivityLayer.Application, HostOperation.Tool);
        return await fixture.Store.CompleteAsync(entry, outcome, () => true, fixture.Token);
    }
}
