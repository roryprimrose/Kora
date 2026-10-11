using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionQueueTests
{
    [WindowsFact]
    public async Task ExactPauseSkipsOneSessionWithoutReprioritizingOrTouchingWorkClocksGrantsAndRetention()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var session = f.Request.SessionId;
        var other = (await WindowsSqliteSessionWorkspaceTests.Service(f, new())
            .CreateAsync(new("Not routing authority"), RequestOrigin.LocalUi, f.Token)).Authority.SessionId;
        var first = await EnqueueAsync(f, session);
        var later = await EnqueueAsync(f, session);
        var second = await EnqueueAsync(f, other);
        var third = await EnqueueAsync(f, other);
        var before = await f.Store.ReadQueueAsync(session, f.Token);
        var retention = await f.Store.ReadRetentionAsync(session, f.Token);
        f.Time.Now = f.Time.Now.AddMinutes(1);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using var queue = PauseQueue(f, access, new NeverAction());
        var paused = (await queue.ExecuteCommandAsync(PauseCommand(before, true), RequestOrigin.LocalUi, () => true, f.Token)).Queue!;
        paused.DispatchControl!.Paused.Should().BeTrue();
        paused.DispatchControl.Held.Should().BeFalse();
        paused.Revision.Should().Be(before.Revision);
        paused.Entries.Should().Equal(before.Entries);
        (await f.Store.ReadRetentionAsync(session, f.Token)).Should().Be(retention);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().Contain(grant);
        var work = await f.Store.ReadWorkAsync(session, 1, new(3, 2), f.Token);
        work.PendingCount.Should().Be(2);
        work.PendingCapacity.Should().Be(3);
        work.ExecutionSlots.Should().Be(2);
        work.QueueRecords.Should().OnlyContain(row => row.Eligibility == SessionQueueEligibility.DispatchPaused);
        AuthorityLocalEventSource.FromWork(work, f.Time.Now).Should().OnlyContain(item => item.QueueReason == SessionQueueEligibility.DispatchPaused);
        var service = new SessionWorkspaceService(f.Store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance, queue);
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            var overview = await service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token);
            var row = overview.Records.Single(item => item.Session.Authority.SessionId == session).Work!;
            row.DispatchControl.Should().Be(work.Queue.DispatchControl);
            row.QueueRecords.Should().Equal(work.QueueRecords);
        }
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().Be(second);
        using (var root = HostActivity.BeginRoot(first.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            var forbidden = () => f.Store.AdmitAsync(first, 1, new(), () => true, f.Token).AsTask();
            await forbidden.Should().ThrowAsync<InvalidOperationException>();
        }
        var admittedOther = await AdmitAsync(f, second);
        await CompleteAsync(f, admittedOther);
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().Be(third);
        var resumed = (await queue.ExecuteCommandAsync(PauseCommand(paused, false), RequestOrigin.ActivatedVoice, () => true, f.Token)).Queue!;
        resumed.DispatchControl!.Paused.Should().BeFalse();
        resumed.DispatchControl.Revision.Should().Be(2);
        await f.Store.HoldDispatchControlAsync(paused, f.Token);
        (await f.Store.ReadQueueAsync(session, f.Token)).DispatchControl.Should().Be(resumed.DispatchControl,
            "a late failed old control receipt cannot change the newer resume");
        resumed.Entries.Should().Equal(first, later);
        (await f.Store.ReadQueueEntryAsync(session, first.Request.TaskId, f.Token))!.State.Should().Be(SessionQueueState.Pending);
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().Be(first);
        var observed = await queue.ExecuteCommandAsync(new(SessionCommandOperation.QueuePauseStatus, session.Value),
            RequestOrigin.LocalUi, () => true, f.Token);
        observed.Queue!.DispatchControl.Should().Be(resumed.DispatchControl);
        observed.Outcome.Should().Be("observed");
    }

    [WindowsFact]
    public async Task PauseAcknowledgesWhileCommittedCallbackIsHeldAndOtherSessionsContinueFairManualBatch()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var session = f.Request.SessionId;
        var other = (await WindowsSqliteSessionWorkspaceTests.Service(f, new())
            .CreateAsync(new("Independent"), RequestOrigin.LocalUi, f.Token)).Authority.SessionId;
        var first = await EnqueueAsync(f, session);
        var later = await EnqueueAsync(f, session);
        var second = await EnqueueAsync(f, other);
        var action = new HeldPauseAction(f.Token);
        using (action)
        {
            await using var queue = PauseQueue(f, new(), action);
            var snapshot = await f.Store.ReadQueueAsync(session, f.Token);
            var dispatch = queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueDispatch, session.Value, 1)
                { QueueRevision = snapshot.Revision }, RequestOrigin.LocalUi, () => true, f.Token);
            await action.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
            try
            {
                var current = await f.Store.ReadQueueAsync(session, f.Token);
                var active = current.Entries.Single(entry => entry.IsCurrent);
                var paused = await queue.ExecuteCommandAsync(PauseCommand(current, true), RequestOrigin.LocalUi, () => true, f.Token);
                paused.Queue!.DispatchControl!.Paused.Should().BeTrue();
                dispatch.IsCompleted.Should().BeFalse("pause owns only the short physical admission fence, never the callback/batch gate");
                (await f.Store.ReadQueueEntryAsync(session, first.Request.TaskId, f.Token)).Should().Be(active);
                action.Release.Set();
                var receipts = (await dispatch).QueueDispatch!;
                receipts.Select(receipt => receipt.Entry.Request.TaskId).Should().Equal(first.Request.TaskId, second.Request.TaskId);
                receipts.Should().OnlyContain(receipt => receipt.Entry.State == SessionQueueState.Succeeded && receipt.Version != null);
                (await f.Store.ReadQueueEntryAsync(session, later.Request.TaskId, f.Token)).Should().Be(later);
            }
            finally { action.Release.Set(); await dispatch; }
        }
    }

    [WindowsFact]
    public async Task PhysicalCommitFenceSerializesAdmissionThenPauseAndRejectsEverySubsequentAdmission()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        var first = await EnqueueAsync(f, f.Request.SessionId);
        var later = await EnqueueAsync(f, f.Request.SessionId);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        checkpoint.Commit = (connection, transaction) =>
        {
            using var read = connection.CreateCommand();
            read.Transaction = transaction;
            read.CommandText = "SELECT json_extract(envelope,'$.Audit.ActionId') FROM security_audit_events ORDER BY sequence DESC LIMIT 1;";
            if (read.ExecuteScalar() is not "queue.admit.version") { return; }
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10), f.Token)) { throw new TimeoutException("Held SQLite admission was not released."); }
        };
        var admission = AdmitAsync(f, first);
        await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(10), f.Token), f.Token);
        entered.IsSet.Should().BeTrue();
        try
        {
            var paused = Task.Run(async () =>
            {
                var snapshot = await f.Store.ReadQueueAsync(first.Request.SessionId, f.Token);
                await using var queue = PauseQueue(f, new(), new NeverAction());
                return await queue.ExecuteCommandAsync(PauseCommand(snapshot, true), RequestOrigin.LocalUi, () => true, f.Token);
            }, f.Token);
            paused.IsCompleted.Should().BeFalse();
            release.Set();
            var running = await admission;
            (await paused).Queue!.DispatchControl!.Paused.Should().BeTrue();
            await CompleteAsync(f, running);
            (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().BeNull();
            var afterAck = () => AdmitAsync(f, later);
            await afterAck.Should().ThrowAsync<InvalidOperationException>();
        }
        finally { release.Set(); await admission; }
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("run")]
    [InlineData("revision")]
    [InlineData("queue")]
    [InlineData("foreign")]
    [InlineData("origin")]
    [InlineData("privacy")]
    [InlineData("caller")]
    [InlineData("cancel")]
    public async Task PauseRefusesStaleRunGenerationForeignScopeAndLostOriginalAdmission(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var entry = await EnqueueAsync(f, f.Request.SessionId);
        var before = await f.Store.ReadQueueAsync(entry.Request.SessionId, f.Token);
        var command = PauseCommand(before, true);
        command = boundary switch
        {
            "generation" => command with { Generation = 2 },
            "run" => command with { QueueRunId = Guid.NewGuid() },
            "revision" => command with { QueueControlRevision = 1 },
            "queue" => command with { QueueRevision = before.Revision + 1 },
            "foreign" => command with { SessionId = Guid.NewGuid() },
            _ => command,
        };
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        if (boundary is "privacy") { access.CanControl = false; }
        using var cancel = new CancellationTokenSource();
        if (boundary is "cancel") { cancel.Cancel(); }
        await using var queue = PauseQueue(f, access, new NeverAction());
        var denied = () => queue.ExecuteCommandAsync(command, boundary is "origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi,
            () => boundary is not "caller", boundary is "cancel" ? cancel.Token : f.Token);
        await denied.Should().ThrowAsync<Exception>();
        (await f.Store.ReadQueueAsync(entry.Request.SessionId, f.Token)).Should().BeEquivalentTo(before);
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("cancel")]
    [InlineData("privacy")]
    public async Task FailedResumeHoldsRunOnlyAdmissionAndNeverClaimsUnpauseOrRenewsRetention(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        var entry = await EnqueueAsync(f, f.Request.SessionId);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using var queue = PauseQueue(f, access, new NeverAction());
        var paused = (await queue.ExecuteCommandAsync(PauseCommand(await f.Store.ReadQueueAsync(entry.Request.SessionId, f.Token), true),
            RequestOrigin.LocalUi, () => true, f.Token)).Queue!;
        var retention = await f.Store.ReadRetentionAsync(entry.Request.SessionId, f.Token);
        var auditCount = f.Count("security_audit_events");
        using var cancel = new CancellationTokenSource();
        checkpoint.Audit = (_, _) =>
        {
            if (boundary is "audit") { throw new IOException("Required audit unavailable."); }
            if (boundary is "cancel") { cancel.Cancel(); }
            else { access.ControlRevision++; }
        };
        f.Time.Now = f.Time.Now.AddMinutes(1);
        var denied = () => queue.ExecuteCommandAsync(PauseCommand(paused, false), RequestOrigin.LocalUi, () => true, cancel.Token);
        await denied.Should().ThrowAsync<Exception>();
        var held = await f.Store.ReadWorkAsync(entry.Request.SessionId, access.ControlRevision, new(), f.Token);
        held.Queue.DispatchControl!.Paused.Should().BeTrue();
        held.Queue.DispatchControl.Held.Should().BeTrue();
        held.QueueRecords.Single().Entry.Should().Be(entry);
        (await f.Store.FindReadyAsync(access.ControlRevision, new(), f.Token)).Should().BeNull();
        (await f.Store.ReadRetentionAsync(entry.Request.SessionId, f.Token)).Should().Be(retention);
        f.Count("security_audit_events").Should().Be(auditCount);
    }

    [WindowsFact]
    public async Task ResumeCannotRestorePrivateEpochOrPreviousRunPendingWorkAndOldCallbacksCannotChangeNewGeneration()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var entry = await EnqueueAsync(f, f.Request.SessionId);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using (var queue = PauseQueue(f, access, new NeverAction()))
        {
            var paused = (await queue.ExecuteCommandAsync(PauseCommand(await f.Store.ReadQueueAsync(entry.Request.SessionId, f.Token), true),
                RequestOrigin.LocalUi, () => true, f.Token)).Queue!;
            access.ControlRevision++;
            var stale = () => queue.ExecuteCommandAsync(PauseCommand(paused, false), RequestOrigin.LocalUi, () => true, f.Token);
            await stale.Should().ThrowAsync<InvalidOperationException>();
            (await f.Store.ReadWorkAsync(entry.Request.SessionId, access.ControlRevision, new(), f.Token))
                .Queue.DispatchControl!.Held.Should().BeTrue();
        }
        var old = await f.Store.ReadQueueAsync(entry.Request.SessionId, f.Token);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        await using var currentQueue = PauseQueue(f, access, new NeverAction());
        var fresh = await f.Store.ReadQueueAsync(entry.Request.SessionId, f.Token);
        fresh.DispatchControl!.Paused.Should().BeFalse();
        fresh.DispatchControl.RunId.Should().NotBe(old.DispatchControl!.RunId);
        var previous = () => currentQueue.ExecuteCommandAsync(PauseCommand(old, false), RequestOrigin.LocalUi, () => true, f.Token);
        await previous.Should().ThrowAsync<InvalidOperationException>();
        await currentQueue.ExecuteCommandAsync(PauseCommand(fresh, false), RequestOrigin.LocalUi, () => true, f.Token);
        var interrupted = await f.Store.ReadWorkAsync(entry.Request.SessionId, access.ControlRevision, new(), f.Token);
        interrupted.QueueRecords.Single().Eligibility.Should().Be(SessionQueueEligibility.InterruptedNoReplay);
        (await f.Store.FindReadyAsync(access.ControlRevision, new(), f.Token)).Should().BeNull();
        await new HostTaskCoordinator(f.Tasks).RecoverAsync(100, f.Token);
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, access);
        await service.ChangeLifecycleAsync(entry.Request.SessionId, new(1), false, RequestOrigin.LocalUi, f.Token);
        await service.ChangeLifecycleAsync(entry.Request.SessionId, new(2), true, RequestOrigin.LocalUi, f.Token);
        var staleGeneration = () => currentQueue.ExecuteCommandAsync(PauseCommand(fresh, true), RequestOrigin.LocalUi, () => true, f.Token);
        await staleGeneration.Should().ThrowAsync<InvalidOperationException>();
        (await f.Store.ReadQueueAsync(entry.Request.SessionId, f.Token)).DispatchControl!.Generation.Value.Should().Be(3);
    }

    private static SessionCommand PauseCommand(SessionQueueSnapshot snapshot, bool paused) =>
        new(paused ? SessionCommandOperation.QueuePause : SessionCommandOperation.QueueResume, snapshot.SessionId.Value, snapshot.Generation.Value)
        { QueueRevision = snapshot.Revision, QueueRunId = snapshot.DispatchControl!.RunId, QueueControlRevision = snapshot.DispatchControl.Revision };

    private static SessionQueueService PauseQueue(InteractionStorageFixture f, WindowsSqliteSessionWorkspaceTests.Access access,
        IDeterministicVersionQueueAction action) =>
        new(f.Store, f.Store, new(f.Tasks), access, action, new(), NullLogger<SessionQueueService>.Instance, f.Time);

    private sealed class HeldPauseAction(CancellationToken token) : IDeterministicVersionQueueAction, IDisposable
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new();
        private int reads;
        public CapabilityReply Observe(CancellationToken cancellation)
        {
            if (Interlocked.Increment(ref reads) == 1)
            {
                Entered.TrySetResult();
                if (!Release.Wait(TimeSpan.FromSeconds(10), token)) { throw new TimeoutException("Held fixed read was not released."); }
            }
            return new(CapabilityOutcome.Succeeded, "observed", Version: new("1.0.0", "Fixed local read."));
        }
        public void Dispose() => Release.Dispose();
    }
}
