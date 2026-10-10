using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Interaction;

[Collection("Host tracing")]
public sealed partial class LocalEventBrokerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Observation_watermark_follows_async_source_time_and_holds_a_rollback_without_fabricating_chronology(bool rollback)
    {
        using var f = new Fixture();
        f.BeforeRead = _ =>
        {
            f.Time.Now = f.Time.Now.AddMilliseconds(rollback ? -1 : 1);
            if (!rollback) { f.Events = [f.Events[0] with { ObservedAt = f.Time.Now }]; }
            return Task.CompletedTask;
        };
        await using var broker = f.Create();
        var result = await f.Observe(broker);
        if (rollback)
        {
            result.Reason.Should().Be(LocalEventReason.ClockRollback);
            result.Events.Should().BeEmpty();
            f.Saves.Should().Be(0);
            f.Audits.Should().BeEmpty();
        }
        else
        {
            result.Events.Single().Reason.Should().Be(LocalEventReason.Eligible);
            f.Saved!.HighWatermark.Should().Be(f.Events[0].ObservedAt);
            f.Saved.Receipts.Single().ChangedAt.Should().Be(f.Saved.HighWatermark);
            f.Saved.Validate();
        }
    }

    [Fact]
    public async Task A_misbound_host_source_cannot_retarget_native_selection_or_persist_a_foreign_session_notice()
    {
        using var f = new Fixture();
        var other = f.Event() with { SessionId = new(Guid.NewGuid()) };
        f.Events = [other with { Id = LocalEvent.Identity(other.Source, other.SessionId.Value, other.SubjectId) }];
        await using var broker = f.Create();
        await f.Reading(broker).Should().ThrowAsync<InvalidDataException>().WithMessage("*retarget*");
        f.Saves.Should().Be(0);
        f.Audits.Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_and_parallel_observations_are_deduplicated_and_changed_source_revisions_coalesce_without_replay()
    {
        using var f = new Fixture();
        await using var broker = f.Create();
        var outputs = await Task.WhenAll(f.Observe(broker), f.Observe(broker));
        outputs[0].Events.Single().Reason.Should().Be(LocalEventReason.Eligible);
        outputs[1].Events.Single().Reason.Should().Be(LocalEventReason.PresentedNoReplay);
        f.Saves.Should().Be(1);
        f.Saved!.Receipts.Should().ContainSingle();
        var prior = outputs[0].Events.Single().Event;
        f.Events = [prior with { SourceRevision = 2, Type = LocalEventType.Completed }];
        var next = (await f.Observe(broker)).Events.Single();
        next.Event.Id.Should().Be(prior.Id);
        next.Event.Revision.Should().Be(2);
        next.Reason.Should().Be(LocalEventReason.CategoryLimit);
        f.Saved.Receipts.Should().ContainSingle();
        var stale = () => broker.ExecuteAsync(new(LocalEventOperation.Dismiss, prior.Id, prior.Revision),
            RequestOrigin.LocalUi, () => true, f.Token);
        await stale.Should().ThrowAsync<InvalidOperationException>();
        f.Events = [prior];
        await f.Reading(broker).Should().ThrowAsync<InvalidDataException>();
        await f.Reading(broker).Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(LocalEventOperation.Status)]
    [InlineData(LocalEventOperation.Review)]
    [InlineData(LocalEventOperation.Dismiss)]
    [InlineData(LocalEventOperation.Defer)]
    public async Task Exact_typed_activated_and_native_routes_share_the_broker_without_question_or_work_authority(LocalEventOperation operation)
    {
        using var f = new Fixture();
        await using var broker = f.Create();
        var target = (await f.Observe(broker)).Events.Single().Event;
        var result = await broker.ExecuteAsync(new(operation, target.Id, target.Revision),
            RequestOrigin.ActivatedVoice, () => true, f.Token);
        if (operation is LocalEventOperation.Dismiss or LocalEventOperation.Defer)
        {
            result.Event.Revision.Should().Be(2);
            result.Reason.Should().Be(operation == LocalEventOperation.Dismiss ? LocalEventReason.Dismissed : LocalEventReason.Deferred);
        }
        else
        {
            result.Reason.Should().Be(LocalEventReason.PresentedNoReplay);
            f.Saves.Should().Be(1);
        }
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        var native = await broker.ExecuteNativeAsync(new(LocalEventOperation.Review, result.Event.Id, result.Event.Revision),
            () => true, f.Token);
        native.Should().Be(result);
        f.Audits.Should().OnlyContain(item => item.Event.Category == SecurityAuditCategory.ConfigurationWrite
            && item.Event.ApprovalId == null && item.Event.TargetId == "local-event.suppression");
        f.Audits.Should().OnlyContain(item => item.Session == f.Session.Value);
    }

    [Fact]
    public async Task Category_limits_and_spacing_are_independent_persistent_and_omissions_are_explicit()
    {
        using var f = new Fixture();
        f.Events = [.. Enumerable.Range(0, 4).Select(_ => f.Event(LocalEventType.Queued)),
            .. Enumerable.Range(0, 4).Select(_ => f.Event(LocalEventType.Failed)),
            f.Event(LocalEventType.UserAttention), f.Event(LocalEventType.MaintenanceAvailable)];
        await using var broker = f.Create();
        var first = await f.Observe(broker);
        first.Events.Should().HaveCount(LocalEventSnapshot.MaximumVisible);
        first.Omitted.Should().Be(2);
        first.Events.Count(item => item.Reason == LocalEventReason.Eligible).Should().Be(4);
        for (var index = 0; index < 4; index++)
        {
            f.Time.Now = f.Time.Now.AddMinutes(1);
            await f.Observe(broker);
        }
        f.Saved!.Budgets.Single(item => item.Category == LocalEventCategory.Work).Count.Should().Be(3);
        f.Saved.Budgets.Single(item => item.Category == LocalEventCategory.Failure).Count.Should().Be(2);
        f.Saved.Receipts.Count(item => item.Disposition == LocalEventDisposition.Presented).Should().Be(7);
        f.Events = [f.Events[^1]];
        var maintenance = await f.Observe(broker);
        maintenance.Events.Single().Reason.Should().Be(LocalEventReason.PresentedNoReplay);
        f.Events = [f.Event(LocalEventType.MaintenanceAvailable)];
        (await f.Observe(broker)).Events.Single().Reason.Should().Be(LocalEventReason.CategoryLimit);
        f.Time.Now = f.Time.Now.AddHours(1);
        (await f.Observe(broker)).Events.Single().Reason.Should().Be(LocalEventReason.Expired);
    }

    [Fact]
    public async Task Deferral_never_extends_source_deadline_restart_suppresses_presented_and_dismissed_and_rollback_holds()
    {
        using var f = new Fixture();
        var source = f.Events.Single() with { ExpiresAt = f.Time.Now.AddMinutes(5) };
        f.Events = [source];
        var broker = f.Create();
        var target = (await f.Observe(broker)).Events.Single().Event;
        var deferred = await broker.ExecuteAsync(new(LocalEventOperation.Defer, target.Id, target.Revision), RequestOrigin.LocalUi, () => true, f.Token);
        deferred.DeferredUntil.Should().Be(source.ExpiresAt);
        var stale = () => broker.ExecuteAsync(new(LocalEventOperation.Defer, target.Id, target.Revision), RequestOrigin.LocalUi, () => true, f.Token);
        await stale.Should().ThrowAsync<InvalidOperationException>();
        await broker.DisposeAsync();
        await using var restarted = f.Create();
        var snapshot = await f.Observe(restarted);
        snapshot.Events.Single().Reason.Should().Be(LocalEventReason.Deferred);
        var held = snapshot.Events.Single().Event;
        f.Time.Now = f.Time.Now.AddSeconds(-1);
        (await restarted.ExecuteAsync(new(LocalEventOperation.Status, held.Id, held.Revision), RequestOrigin.LocalUi, () => true, f.Token))
            .Reason.Should().Be(LocalEventReason.ClockRollback);
        (await f.Observe(restarted)).Reason.Should().Be(LocalEventReason.ClockRollback);
        f.Time.Now = source.ExpiresAt;
        await f.Observe(restarted);
        f.Saved!.Receipts.Single().Event.ExpiresAt.Should().Be(source.ExpiresAt);
    }

    [Theory]
    [InlineData(LocalEventOperation.Dismiss, LocalEventReason.Dismissed)]
    [InlineData(LocalEventOperation.Status, LocalEventReason.PresentedNoReplay)]
    public async Task Restart_never_replays_prior_effects_and_expired_subjects_are_truthful(LocalEventOperation operation, LocalEventReason reason)
    {
        using var f = new Fixture();
        var broker = f.Create();
        var target = (await f.Observe(broker)).Events.Single().Event;
        await broker.ExecuteAsync(new(operation, target.Id, target.Revision), RequestOrigin.LocalUi, () => true, f.Token);
        await broker.DisposeAsync();
        await using var restarted = f.Create();
        var current = (await f.Observe(restarted)).Events.Single();
        current.Reason.Should().Be(reason);
        f.Time.Now = target.ExpiresAt;
        var expired = await restarted.ExecuteAsync(new(LocalEventOperation.Review, current.Event.Id, current.Event.Revision),
            RequestOrigin.LocalUi, () => true, f.Token);
        expired.Reason.Should().Be(LocalEventReason.Expired);
        f.Saved!.Receipts.Should().ContainSingle();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("lock")]
    [InlineData("origin")]
    [InlineData("native-lifetime")]
    public async Task Source_and_original_channel_generations_are_revalidated_without_relabelling_or_unlock_replay(string boundary)
    {
        using var f = new Fixture();
        var visible = true;
        await using var broker = f.Create();
        var target = (await broker.ObserveAsync(f.Session, () => visible, f.Token)).Events.Single().Event;
        if (boundary is "native-lifetime") { visible = false; }
        else if (boundary is "privacy") { f.CanControl = false; }
        else { f.ControlRevision++; }
        var command = () => broker.ExecuteAsync(new(LocalEventOperation.Dismiss, target.Id, target.Revision),
            RequestOrigin.ActivatedVoice, () => boundary is not "origin", f.Token);
        await command.Should().ThrowAsync<InvalidOperationException>();
        f.Saves.Should().Be(1);
        f.CanControl = true;
        visible = true;
        (await f.Observe(broker)).Events.Single().Reason.Should().Be(LocalEventReason.PresentedNoReplay);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("revision")]
    [InlineData("profile")]
    [InlineData("done")]
    [InlineData("deletion")]
    public async Task A_retired_source_is_unavailable_not_a_current_or_empty_success(string change)
    {
        using var f = new Fixture();
        await using var broker = f.Create();
        var target = (await f.Observe(broker)).Events.Single().Event;
        f.Events = change is "revision" ? [target with { SourceRevision = 2 }] : [];
        var result = await broker.ExecuteAsync(new(LocalEventOperation.Review, target.Id, target.Revision),
            RequestOrigin.LocalUi, () => true, f.Token);
        result.Reason.Should().Be(LocalEventReason.SourceUnavailable);
        var again = () => broker.ExecuteAsync(new(LocalEventOperation.Defer, target.Id, target.Revision), RequestOrigin.LocalUi, () => true, f.Token);
        await again.Should().ThrowAsync<InvalidOperationException>();
        f.Saves.Should().Be(1);
    }

    [Theory]
    [InlineData("save")]
    [InlineData("audit-request")]
    [InlineData("audit-outcome")]
    [InlineData("confirm")]
    [InlineData("after-save-admission")]
    [InlineData("after-terminal-admission")]
    [InlineData("cancel")]
    public async Task Persistence_audit_cancellation_and_admission_races_hold_without_fabricated_rollback_or_replay(string boundary)
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        f.OnSave = () =>
        {
            if (boundary is "save") { throw new IOException("User content must never reach diagnostics."); }
            if (boundary is "after-save-admission") { f.ControlRevision++; }
            if (boundary is "cancel") { cancellation.Cancel(); }
        };
        f.OnAudit = entry =>
        {
            if (boundary is "audit-request" && entry.Outcome == SecurityAuditOutcome.Requested
                || boundary is "audit-outcome" && entry.Outcome == SecurityAuditOutcome.Succeeded)
            { throw new IOException("Unavailable trusted audit."); }
            if (boundary is "after-terminal-admission" && entry.Outcome == SecurityAuditOutcome.Succeeded) { f.ControlRevision++; }
        };
        if (boundary is "confirm") { f.OnConfirm = () => throw new IOException("Unconfirmed marker."); }
        await using var broker = f.Create();
        var observe = () => broker.ObserveAsync(f.Session, () => true, cancellation.Token);
        await observe.Should().ThrowAsync<Exception>();
        f.OnSave = null; f.OnAudit = null; f.OnConfirm = null;
        await f.Reading(broker).Should().ThrowAsync<InvalidOperationException>();
        if (f.Pending)
        {
            await using var restarted = f.Create();
            await f.Reading(restarted).Should().ThrowAsync<InvalidDataException>();
        }
        f.Audits.Count(item => item.Event.Outcome == SecurityAuditOutcome.Succeeded).Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task Source_race_capacity_and_missing_or_corrupt_state_fail_closed_without_eviction()
    {
        using var f = new Fixture();
        await using var broker = f.Create();
        f.BeforeCurrent = () => f.Events = [];
        await f.Reading(broker).Should().ThrowAsync<InvalidOperationException>();
        f.Saves.Should().Be(0);
        f.BeforeCurrent = null;
        f.Events = [f.Event()];
        await broker.DisposeAsync();
        f.Saved = LocalEventBrokerState.Empty(f.Time.Now) with
        {
            Receipts = Enumerable.Range(0, LocalEventBrokerState.MaximumReceipts)
                .Select(_ => new LocalEventReceipt(f.Event(), LocalEventDisposition.Dismissed, null, f.Time.Now)).ToArray(),
        };
        await using var capacity = f.Create();
        await f.Reading(capacity).Should().ThrowAsync<InvalidOperationException>().WithMessage("*capacity*");
        f.Saves.Should().Be(0);
        f.Saved.Receipts.Should().HaveCount(LocalEventBrokerState.MaximumReceipts);
        f.Saved = f.Saved with { Schema = 99 };
        await using var corrupt = f.Create();
        await f.Reading(corrupt).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Retention_removes_only_owned_session_suppression_before_authority_deletion_without_lock_inversion()
    {
        using var f = new Fixture();
        await using var broker = f.Create();
        await f.Observe(broker);
        var other = f.Event() with { SessionId = new(Guid.NewGuid()) };
        other = other with { Id = LocalEvent.Identity(other.Source, other.SessionId.Value, other.SubjectId) };
        f.Saved = f.Saved! with { Receipts = [.. f.Saved!.Receipts, new(other, LocalEventDisposition.Dismissed, null, f.Time.Now)] };
        await broker.DisposeAsync();
        await using var restarted = f.Create();
        var result = await restarted.WithRetirementAsync(async retire =>
        {
            await retire(f.Session, f.Token);
            f.Saved!.Receipts.Should().OnlyContain(item => item.Event.SessionId == other.SessionId);
            return true;
        }, f.Token);
        result.Should().BeTrue();
        await restarted.RetireSessionAsync(f.Session, f.Token);
        await restarted.RetireSessionAsync(other.SessionId, f.Token);
        f.Saved!.Receipts.Should().BeEmpty();
        f.Audits.Should().Contain(item => item.Event.ActionId == "local-event.retire");
    }

    [Fact]
    public async Task Cancellation_disposal_and_host_ambient_input_do_not_leave_admitted_callbacks()
    {
        using var f = new Fixture();
        var broker = f.Create();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.BeforeRead = async token => { entered.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); };
        var pending = f.Observe(broker);
        await entered.Task.WaitAsync(f.Token);
        await broker.DisposeAsync();
        Func<Task<LocalEventSnapshot>> pendingResult = () => pending;
        await pendingResult.Should().ThrowAsync<OperationCanceledException>();
        await broker.DisposeAsync();
        await f.Reading(broker).Should().ThrowAsync<ObjectDisposedException>();
        var disposed = () => broker.ExecuteAsync(new(LocalEventOperation.Status), RequestOrigin.LocalUi, () => true, f.Token);
        await disposed.Should().ThrowAsync<ObjectDisposedException>();
        f.BeforeRead = null;
        await using var fresh = f.Create();
        var target = (await f.Observe(fresh)).Events.Single().Event;
        foreach (var origin in new[] { RequestOrigin.HostSystem, (RequestOrigin)99 })
        {
            var wrong = () => fresh.ExecuteAsync(new(LocalEventOperation.Dismiss, target.Id, target.Revision), origin, () => true, f.Token);
            await wrong.Should().ThrowAsync<InvalidOperationException>();
        }
        var invalid = () => fresh.ExecuteAsync(new(LocalEventOperation.Invalid), RequestOrigin.LocalUi, () => true, f.Token);
        await invalid.Should().ThrowAsync<InvalidOperationException>();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request);
        var ambient = () => fresh.ExecuteAsync(new(LocalEventOperation.Dismiss, target.Id, target.Revision), RequestOrigin.LocalUi, () => true, f.Token);
        await ambient.Should().ThrowAsync<InvalidOperationException>();
        var native = () => fresh.ExecuteNativeAsync(new(LocalEventOperation.Dismiss, target.Id, target.Revision), () => true, f.Token);
        await native.Should().ThrowAsync<InvalidOperationException>();
    }

    internal sealed class Fixture : ILocalEventSource, ILocalEventStateStore, ISessionWorkspaceAccess, ISecurityAuditLog, IDisposable
    {
        private readonly ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        internal Fixture() { ActivitySource.AddActivityListener(listener); Events = [Event()]; }
        internal Clock Time { get; } = new();
        internal HostId<SessionIdentity> Session { get; set; } = new(Guid.NewGuid());
        internal IReadOnlyList<LocalEvent> Events { get; set; }
        internal LocalEventBrokerState? Saved { get; set; }
        internal bool Pending { get; private set; }
        internal int Saves { get; private set; }
        internal Action? OnSave { get; set; }
        internal Action? OnConfirm { get; set; }
        internal Action<SecurityAuditEvent>? OnAudit { get; set; }
        internal Action? BeforeCurrent { get; set; }
        internal Action? AfterCurrent { get; set; }
        internal Func<CancellationToken, Task>? BeforeRead { get; set; }
        internal List<(SecurityAuditEvent Event, Guid Session)> Audits { get; } = [];
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        public bool CanInspect => CanControl;
        public bool CanControl { get; set; } = true;
        private long controlRevision;
        internal Action? OnAdmissionRead { get; set; }
        public long ControlRevision { get { OnAdmissionRead?.Invoke(); return controlRevision; } set => controlRevision = value; }
        internal LocalEventBroker Create(ILogger<LocalEventBroker>? logger = null) => new(this, this, this, Time, this, logger ?? NullLogger<LocalEventBroker>.Instance);
        internal Task<LocalEventSnapshot> Observe(LocalEventBroker broker) => broker.ObserveAsync(Session, () => true, Token);
        internal Func<Task<LocalEventSnapshot>> Reading(LocalEventBroker broker) => () => Observe(broker);
        internal LocalEvent Event(LocalEventType type = LocalEventType.Queued)
        {
            var subject = Guid.NewGuid();
            var source = type == LocalEventType.UserAttention ? LocalEventSource.LocalVersionQuestion
                : type == LocalEventType.MaintenanceAvailable ? LocalEventSource.CachedMaintenance : LocalEventSource.LocalVersionQueue;
            return new(LocalEvent.Identity(source, Session.Value, subject), 1, source, type, Session,
                source == LocalEventSource.CachedMaintenance ? null : new(subject), subject, 1, 1, 0,
                source == LocalEventSource.CachedMaintenance ? RequestOrigin.HostSystem : RequestOrigin.LocalUi,
                source == LocalEventSource.LocalVersionQueue ? SessionQueueEligibility.Ready : null, Time.Now, Time.Now.AddMinutes(30));
        }
        public async Task<IReadOnlyList<LocalEvent>> ReadAsync(HostId<SessionIdentity> session, CancellationToken token)
        {
            if (BeforeRead is not null) { await BeforeRead(token); }
            token.ThrowIfCancellationRequested();
            return Events;
        }
        public Task<T> WithCurrentAsync<T>(HostId<SessionIdentity> session, IReadOnlyList<LocalEvent> expected,
            Func<T> observation, CancellationToken token)
        {
            BeforeCurrent?.Invoke();
            if (Events.Count != expected.Count || Events.Any(item => !expected.Any(item.SameSource)))
            { throw new InvalidOperationException("Source race."); }
            token.ThrowIfCancellationRequested();
            var result = observation();
            AfterCurrent?.Invoke();
            return Task.FromResult(result);
        }
        public LocalEventBrokerState? Load()
        {
            if (Pending) { throw new InvalidDataException("Unconfirmed state."); }
            Saved?.Validate();
            return Saved;
        }
        public void BeginWrite() => Pending = true;
        public void Save(LocalEventBrokerState state) { Saves++; Saved = state; OnSave?.Invoke(); }
        public void ConfirmWrite() { OnConfirm?.Invoke(); Pending = false; }
        public void Write(SecurityAuditEvent entry)
        {
            var live = HostActivity.RequireCurrent();
            live.CorrelationId.Should().Be(entry.CorrelationId);
            Audits.Add((entry, live.Request.SessionId.Value));
            OnAudit?.Invoke(entry);
        }
        public void Dispose() => listener.Dispose();
    }
    internal sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 4, 1, 55, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
