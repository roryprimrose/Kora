using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Tools;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Fact]
    public async Task Queue_commands_and_native_calls_share_exact_service_without_inference_or_content_logging()
    {
        using var fixture = new Fixture();
        await using var queue = fixture.QueueService();
        var workspace = new SessionWorkspaceService(fixture, new(fixture), fixture, fixture.Logger, queue);
        var command = fixture.QueueCommand(SessionCommandOperation.QueueEnqueue);
        var accepted = await workspace.ExecuteCommandAsync(command, RequestOrigin.LocalUi, () => true, fixture.Token);
        accepted.Queue!.Entries.Should().ContainSingle().Which.Request.TaskId.Value.Should().Be(command.TaskId!.Value);
        accepted.Queue.Revision.Should().Be(1);
        var listed = await workspace.ExecuteCommandAsync(fixture.QueueCommand(SessionCommandOperation.QueueList),
            RequestOrigin.LocalUi, () => true, fixture.Token);
        listed.Queue.Should().BeEquivalentTo(accepted.Queue);
        var observed = await queue.ExecuteCommandAsync(command with { Operation = SessionCommandOperation.QueueStatus },
            RequestOrigin.ActivatedVoice, () => true, fixture.Token);
        observed.QueueEntry.Should().Be(accepted.Queue.Entries.Single());
        var completed = await queue.ExecuteCommandAsync(fixture.QueueCommand(SessionCommandOperation.QueueDispatch) with { QueueRevision = 1 },
            RequestOrigin.LocalUi, () => true, fixture.Token);
        completed.QueueDispatch.Should().ContainSingle().Which.Version!.Version.Should().Be("1.0.0");
        completed.Queue!.Entries.Should().BeEmpty();
        fixture.QueueActions.Should().Be(1);
        fixture.ActionRequests.Should().ContainSingle().Which.TaskId.Value.Should().Be(command.TaskId.Value);
        fixture.ActionLinks.Should().ContainSingle().Which.Should().BeGreaterThan(0);
        var unsupported = () => fixture.Service.ExecuteCommandAsync(command, RequestOrigin.LocalUi, () => true, fixture.Token);
        await unsupported.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unavailable*");
    }

    [Theory]
    [InlineData(SessionCommandOperation.QueueCancel)]
    [InlineData(SessionCommandOperation.QueueRemove)]
    [InlineData(SessionCommandOperation.QueueClear)]
    public async Task Pending_removal_preserves_identity_and_audit_while_never_executing(SessionCommandOperation operation)
    {
        using var fixture = new Fixture();
        await using var queue = fixture.QueueService();
        var enqueue = fixture.QueueCommand(SessionCommandOperation.QueueEnqueue);
        await queue.ExecuteCommandAsync(enqueue, RequestOrigin.LocalUi, () => true, fixture.Token);
        var result = await queue.ExecuteCommandAsync(enqueue with { Operation = operation, QueueRevision = 1, TaskRevision = 1 },
            RequestOrigin.LocalUi, () => true, fixture.Token);
        result.Queue!.Entries.Should().BeEmpty();
        fixture.QueueActions.Should().Be(0);
        fixture.QueueRows.Single().State.Should().Be(operation == SessionCommandOperation.QueueCancel
            ? SessionQueueState.Cancelled : SessionQueueState.Removed);
    }

    [Theory]
    [InlineData("read", false)]
    [InlineData("enqueue", false)]
    [InlineData("find", true)]
    [InlineData("admit", true)]
    [InlineData("complete", true)]
    [InlineData("action", true)]
    [InlineData("dispatch-read", true)]
    public async Task Required_persistence_audit_and_callback_failures_stop_further_dispatch(string failure, bool dispatch)
    {
        using var fixture = new Fixture();
        await using var queue = fixture.QueueService();
        if (dispatch)
        {
            await queue.ExecuteCommandAsync(fixture.QueueCommand(SessionCommandOperation.QueueEnqueue), RequestOrigin.LocalUi, () => true, fixture.Token);
        }
        fixture.QueueFailure = failure;
        var command = fixture.QueueCommand(dispatch ? SessionCommandOperation.QueueDispatch : failure is "read"
            ? SessionCommandOperation.QueueList : SessionCommandOperation.QueueEnqueue) with { QueueRevision = dispatch ? 1 : 0 };
        var execute = () => queue.ExecuteCommandAsync(command, RequestOrigin.LocalUi, () => true, fixture.Token);
        await execute.Should().ThrowAsync<IOException>();
        fixture.QueueEvents.Should().ContainSingle().Which.Id.Should().Be(184);
        fixture.QueueMessages.Single().Should().NotContain("1.0.0");
        fixture.QueueRows.Should().NotContain(entry => entry.State == SessionQueueState.Succeeded);
    }

    [Theory]
    [InlineData("initial")]
    [InlineData("read")]
    [InlineData("revision")]
    [InlineData("action")]
    [InlineData("callback")]
    public async Task Ownership_privacy_and_revision_changes_deny_without_replay_or_late_success(string boundary)
    {
        using var fixture = new Fixture();
        await using var queue = fixture.QueueService();
        if (boundary is "action" or "callback")
        {
            await queue.ExecuteCommandAsync(fixture.QueueCommand(SessionCommandOperation.QueueEnqueue), RequestOrigin.LocalUi, () => true, fixture.Token);
            if (boundary is "action") { fixture.BeforeAdmit = () => fixture.CanControl = false; }
            else { fixture.OnObserve = () => fixture.CanControl = false; }
        }
        else if (boundary is "initial") { fixture.CanControl = false; }
        else { fixture.AfterQueueRead = () => { if (boundary is "revision") { fixture.AdvanceRevision(); } else { fixture.CanInspect = false; } }; }
        var command = fixture.QueueCommand(boundary is "action" or "callback" ? SessionCommandOperation.QueueDispatch
            : boundary is "initial" ? SessionCommandOperation.QueueEnqueue : SessionCommandOperation.QueueList)
            with { QueueRevision = boundary is "action" or "callback" ? 1 : 0 };
        var execute = () => queue.ExecuteCommandAsync(command, RequestOrigin.LocalUi, () => true, fixture.Token);
        await execute.Should().ThrowAsync<InvalidOperationException>();
        fixture.QueueRows.Should().NotContain(entry => entry.State == SessionQueueState.Succeeded);
    }

    [Fact]
    public async Task Unsupported_inputs_stale_queue_and_disposal_fail_closed()
    {
        using var fixture = new Fixture();
        var queue = fixture.QueueService();
        var command = fixture.QueueCommand(SessionCommandOperation.QueueEnqueue);
        var nullCommand = () => queue.ExecuteCommandAsync(null!, RequestOrigin.LocalUi, () => true, fixture.Token);
        await nullCommand.Should().ThrowAsync<ArgumentNullException>();
        var nullAdmission = () => queue.ExecuteCommandAsync(command, RequestOrigin.LocalUi, null!, fixture.Token);
        await nullAdmission.Should().ThrowAsync<ArgumentNullException>();
        foreach (var malformed in new[]
        {
            command with { SessionId = null }, command with { WorkRequestId = null }, command with { TaskId = null },
            command with { Operation = SessionCommandOperation.Create },
            command with { Operation = SessionCommandOperation.QueueDispatch, QueueRevision = 100 },
            command with { Operation = SessionCommandOperation.QueueDispatch, Generation = 2 },
        })
        {
            var execute = () => queue.ExecuteCommandAsync(malformed, RequestOrigin.LocalUi, () => true, fixture.Token);
            await execute.Should().ThrowAsync<InvalidOperationException>();
        }
        var system = () => queue.ExecuteCommandAsync(command, RequestOrigin.HostSystem, () => true, fixture.Token);
        await system.Should().ThrowAsync<InvalidOperationException>();
        var unadmitted = () => queue.ExecuteCommandAsync(command, RequestOrigin.LocalUi, () => false, fixture.Token);
        await unadmitted.Should().ThrowAsync<InvalidOperationException>();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var cancel = () => queue.ExecuteCommandAsync(command, RequestOrigin.LocalUi, () => true, cancelled.Token);
        await cancel.Should().ThrowAsync<OperationCanceledException>();
        await queue.DisposeAsync();
        await queue.DisposeAsync();
        var disposed = () => queue.ExecuteCommandAsync(command, RequestOrigin.LocalUi, () => true, fixture.Token);
        await disposed.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Foreign_resolved_session_cannot_establish_queue_authority()
    {
        using var fixture = new Fixture { ForeignTaskSession = true };
        await using var queue = fixture.QueueService();
        var inspect = () => queue.ExecuteCommandAsync(fixture.QueueCommand(SessionCommandOperation.QueueList),
            RequestOrigin.LocalUi, () => true, fixture.Token);
        await inspect.Should().ThrowAsync<InvalidDataException>();
        fixture.QueueRows.Should().BeEmpty();
    }

    [Fact]
    public async Task Failed_local_observation_has_truthful_failed_receipt_and_manual_dispatch_is_bounded()
    {
        using var fixture = new Fixture { QueueActionOutcome = CapabilityOutcome.Failed };
        await using var queue = fixture.QueueService();
        await queue.ExecuteCommandAsync(fixture.QueueCommand(SessionCommandOperation.QueueEnqueue), RequestOrigin.ActivatedVoice, () => true, fixture.Token);
        var reply = await queue.ExecuteCommandAsync(fixture.QueueCommand(SessionCommandOperation.QueueDispatch) with { QueueRevision = 1 },
            RequestOrigin.LocalUi, () => true, fixture.Token);
        reply.QueueDispatch!.Single().Entry.State.Should().Be(SessionQueueState.Failed);
        reply.QueueDispatch!.Single().Version.Should().BeNull();
        var unknown = await queue.ExecuteCommandAsync(fixture.QueueCommand(SessionCommandOperation.QueueStatus),
            RequestOrigin.LocalUi, () => true, fixture.Token);
        unknown.QueueEntry.Should().BeNull();
        var empty = await queue.ExecuteCommandAsync(fixture.QueueCommand(SessionCommandOperation.QueueDispatch) with { QueueRevision = fixture.QueueRevision },
            RequestOrigin.LocalUi, () => true, fixture.Token);
        empty.QueueDispatch.Should().BeEmpty();
    }

    [Fact]
    public async Task Fresh_dispatch_is_bounded_to_32_and_retained_causes_are_optional_not_authority()
    {
        using var fixture = new Fixture();
        var now = fixture.QueueTime.GetUtcNow();
        for (var index = 0; index < 33; index++)
        {
            fixture.QueueRows.Add(new(HostRequest.Create(RequestOrigin.LocalUi), new(1), new(1), index + 1,
                SessionQueueState.Pending, fixture.QueueRun, 1, now, now.AddMinutes(30)));
        }
        await using var queue = fixture.QueueService();
        var result = await queue.ExecuteCommandAsync(fixture.QueueCommand(SessionCommandOperation.QueueDispatch),
            RequestOrigin.LocalUi, () => true, fixture.Token);
        result.QueueDispatch.Should().HaveCount(32);
        fixture.QueueRows.Should().ContainSingle(entry => entry.IsPending);
        fixture.ActionRequests.Should().HaveCount(32);
    }

    [Fact]
    public async Task Active_budget_begins_at_admission_and_late_read_cannot_be_success()
    {
        using var fixture = new Fixture();
        await using var queue = fixture.QueueService(slots: 2, clock: fixture.QueueTime);
        var command = fixture.QueueCommand(SessionCommandOperation.QueueEnqueue) with { DependencyTaskId = Guid.NewGuid() };
        var accepted = await queue.ExecuteCommandAsync(command, RequestOrigin.LocalUi, () => true, fixture.Token);
        accepted.Queue!.Entries.Single().Dependency!.Value.Value.Should().Be(command.DependencyTaskId!.Value);
        fixture.QueueRows[0] = fixture.QueueRows[0] with { Dependency = null };
        fixture.QueueTime.Ticks = TimeSpan.FromMinutes(4).Ticks;
        fixture.OnObserve = () => fixture.QueueTime.Ticks += SessionQueuePolicy.ActiveDeadline.Ticks;
        var result = await queue.ExecuteCommandAsync(fixture.QueueCommand(SessionCommandOperation.QueueDispatch) with { QueueRevision = 1 },
            RequestOrigin.LocalUi, () => true, fixture.Token);
        result.QueueDispatch!.Single().Entry.State.Should().Be(SessionQueueState.Failed);
    }

    [Fact]
    public async Task Drain_keeps_the_first_failure_and_awaits_every_already_admitted_callback()
    {
        using var fixture = new Fixture();
        var now = fixture.QueueTime.GetUtcNow();
        var entry = new SessionQueueEntry(fixture.Request, new(1), new(1), 1, SessionQueueState.Succeeded,
            fixture.QueueRun, 1, now, now.AddMinutes(30));
        var receipt = new SessionQueueDispatchReceipt(entry, null);
        var original = new IOException("Original storage uncertainty.");
        var late = new IOException("Late callback failure.");
        var receipts = new List<SessionQueueDispatchReceipt>();
        (await SessionQueueService.DrainAsync([Task.FromResult(receipt), Task.FromException<SessionQueueDispatchReceipt>(late)],
            receipts, original)).Should().BeSameAs(original);
        receipts.Should().ContainSingle().Which.Should().Be(receipt);
        (await SessionQueueService.DrainAsync([Task.FromException<SessionQueueDispatchReceipt>(late)], [], null))
            .Should().BeSameAs(late);
    }

    [Fact]
    public async Task Disposal_closes_admission_and_awaits_current_callbacks_without_late_success()
    {
        using var fixture = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var queue = fixture.QueueService();
        await queue.ExecuteCommandAsync(fixture.QueueCommand(SessionCommandOperation.QueueEnqueue), RequestOrigin.LocalUi, () => true, fixture.Token);
        fixture.OnObserve = () => { entered.Set(); release.Wait(fixture.Token); };
        var dispatch = queue.ExecuteCommandAsync(fixture.QueueCommand(SessionCommandOperation.QueueDispatch) with { QueueRevision = 1 },
            RequestOrigin.LocalUi, () => true, fixture.Token);
        await Task.Run(() => entered.Wait(fixture.Token), fixture.Token);
        var disposing = queue.DisposeAsync().AsTask();
        disposing.IsCompleted.Should().BeFalse();
        release.Set();
        var outcome = () => dispatch;
        await outcome.Should().ThrowAsync<InvalidOperationException>();
        await disposing;
        fixture.QueueRows.Should().NotContain(entry => entry.State == SessionQueueState.Succeeded);
    }

    private sealed partial class Fixture : ISessionQueueStore, IDeterministicVersionQueueAction
    {
        internal List<SessionQueueEntry> QueueRows { get; } = [];
        internal long QueueRevision { get; private set; }
        internal string? QueueFailure { get; set; }
        internal Action? OnObserve { get; set; }
        internal Action? BeforeAdmit { get; set; }
        internal Action? AfterQueueRead { get; set; }
        internal CapabilityOutcome QueueActionOutcome { get; init; } = CapabilityOutcome.Succeeded;
        internal int QueueActions { get; private set; }
        internal List<HostRequest> ActionRequests { get; } = [];
        internal List<int> ActionLinks { get; } = [];
        internal List<EventId> QueueEvents { get; } = [];
        internal List<string> QueueMessages { get; } = [];
        private readonly Guid queueRun = Guid.NewGuid();
        internal Guid QueueRun => queueRun;
        internal QueueClock QueueTime { get; } = new();
        internal SessionQueueService QueueService(int slots = 1, TimeProvider? clock = null) =>
            new(this, this, new(this), this, this, new(executionSlots: slots), new QueueLogger(this), clock);
        internal SessionCommand QueueCommand(SessionCommandOperation operation) => new(operation, Request.SessionId.Value, 1)
        {
            WorkRequestId = Guid.NewGuid(), TaskId = Guid.NewGuid(), TaskRevision = 1,
        };
        private void QueueCheck(string boundary)
        {
            if (string.Equals(QueueFailure, boundary, StringComparison.Ordinal)) { throw new IOException("Private queue boundary failed."); }
        }
        public ValueTask<SessionQueueSnapshot> ReadQueueAsync(HostId<SessionIdentity> session, CancellationToken token)
        {
            QueueCheck("read");
            QueueCheck("dispatch-read");
            AfterQueueRead?.Invoke();
            return ValueTask.FromResult(QueueSnapshot(session));
        }
        private SessionQueueSnapshot QueueSnapshot(HostId<SessionIdentity> session) =>
            new(session, new(1), QueueRevision, QueueRows.Where(entry => entry.IsPending || entry.IsCurrent).ToArray());
        public ValueTask<SessionQueueEntry?> ReadQueueEntryAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task, CancellationToken token) =>
            ValueTask.FromResult(QueueRows.SingleOrDefault(entry => entry.Request.TaskId == task));
        public ValueTask<SessionQueueSnapshot> EnqueueAsync(HostRequest control, HostRequest work, HostRevision generation,
            long expectedRevision, long admissionRevision, HostId<TaskIdentity>? dependency, SessionQueueLimits limits,
            Func<bool> eligible, CancellationToken token)
        {
            QueueCheck("enqueue");
            if (!eligible()) { throw new InvalidOperationException("Private admission changed."); }
            var now = DateTimeOffset.UtcNow;
            QueueRows.Add(new(work, generation, new(1), ++QueueRevision, SessionQueueState.Pending, queueRun,
                admissionRevision, now, now.AddMinutes(30), dependency));
            return ValueTask.FromResult(QueueSnapshot(control.SessionId));
        }
        public ValueTask<SessionQueueSnapshot> RemovePendingAsync(HostRequest control, HostRevision generation,
            long expectedRevision, HostId<TaskIdentity>? task, HostRevision? entryRevision, SessionQueueState outcome,
            Func<bool> eligible, CancellationToken token)
        {
            foreach (var entry in QueueRows.Where(entry => entry.IsPending && (task is null || entry.Request.TaskId == task)).ToArray())
            {
                QueueRows[QueueRows.IndexOf(entry)] = entry with { State = outcome, Revision = new(2) };
                QueueRevision++;
            }
            return ValueTask.FromResult(QueueSnapshot(control.SessionId));
        }
        public ValueTask<SessionQueueEntry?> FindReadyAsync(long admissionRevision, SessionQueueLimits limits, CancellationToken token)
        {
            QueueCheck("find");
            return ValueTask.FromResult(SessionQueuePolicy.SelectReady(QueueRows,
                new Dictionary<HostId<TaskIdentity>, HostTaskState>(), queueRun, DateTimeOffset.UtcNow, admissionRevision, limits));
        }
        public ValueTask<SessionQueueEntry> AdmitAsync(SessionQueueEntry expected, long admissionRevision,
            SessionQueueLimits limits, Func<bool> eligible, CancellationToken token)
        {
            QueueCheck("admit");
            BeforeAdmit?.Invoke();
            if (!eligible()) { throw new InvalidOperationException("Private admission changed."); }
            var next = expected with { State = SessionQueueState.Running, Revision = new(2), DispatchOrder = ++QueueRevision };
            QueueRows[QueueRows.IndexOf(expected)] = next;
            return ValueTask.FromResult(next);
        }
        public ValueTask<SessionQueueEntry> CompleteAsync(SessionQueueEntry expected, SessionQueueState outcome,
            Func<bool> eligible, CancellationToken token)
        {
            QueueCheck("complete");
            var next = expected with { State = outcome, Revision = new(3) };
            QueueRows[QueueRows.IndexOf(expected)] = next;
            QueueRevision++;
            return ValueTask.FromResult(next);
        }
        public CapabilityReply Observe(CancellationToken token)
        {
            QueueCheck("action");
            QueueActions++;
            ActionRequests.Add(HostActivity.RequireCurrent().Request);
            ActionLinks.Add(HostActivity.RequireCurrent().Activity!.Links.Count());
            OnObserve?.Invoke();
            return new(QueueActionOutcome, "observed", Version: QueueActionOutcome == CapabilityOutcome.Succeeded
                ? new("1.0.0", "Not a provider receipt.") : null);
        }
        private sealed class QueueLogger(Fixture fixture) : ILogger<SessionQueueService>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                fixture.QueueEvents.Add(eventId);
                fixture.QueueMessages.Add(formatter(state, exception));
            }
        }
        internal sealed class QueueClock : TimeProvider
        {
            internal long Ticks { get; set; }
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            public override long GetTimestamp() => Ticks;
            public override DateTimeOffset GetUtcNow() => new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
        }
    }
}
