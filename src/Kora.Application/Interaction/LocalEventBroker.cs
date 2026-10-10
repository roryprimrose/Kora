using System.Diagnostics;

using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Interaction;

public sealed partial class LocalEventBroker(
    ILocalEventSource source, ILocalEventStateStore store, ISessionWorkspaceAccess access,
    TimeProvider time, ISecurityAuditLog audit, ILogger<LocalEventBroker> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim serial = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Lock disposalGate = new();
    private Task? disposal;
    private LocalEventBrokerState? state;
    private IReadOnlyList<LocalEvent> held = [];
    private HostId<SessionIdentity>? selected;
    private long heldAdmission;
    private long retirementEpoch;
    private Func<bool> heldLifetime = static () => false;
    private volatile bool disposed;
    private volatile bool unavailable;
    private volatile RoutineNoticeQuietState routineQuiet = new(false, 0);

    public RoutineNoticeQuietState RoutineQuiet => routineQuiet;
    public bool IsAvailable => !disposed && !unavailable;
    public bool IsCurrent(LocalEventSnapshot snapshot) =>
        IsAvailable && access.CanControl && snapshot.AdmissionRevision == access.ControlRevision && snapshot.RoutineQuiet == routineQuiet;

    public async Task<LocalEventSnapshot> ObserveAsync(HostId<SessionIdentity> session,
        Func<bool> nativeLifetime, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        var token = linked.Token;
        await serial.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var revision = access.ControlRevision;
            var epoch = Volatile.Read(ref retirementEpoch);
            bool Eligible() => !disposed && !unavailable && access.CanControl
                && access.ControlRevision == revision && nativeLifetime() && Volatile.Read(ref retirementEpoch) == epoch;
            Require(Eligible);
            var request = new HostRequest(new(Guid.NewGuid()), session, new(Guid.NewGuid()), RequestOrigin.HostSystem);
            using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Presentation, Causes());
            try
            {
                state ??= store.Load() ?? LocalEventBrokerState.Empty(time.GetUtcNow());
                state.Validate();
                var now = time.GetUtcNow();
                if (now < state.HighWatermark)
                {
                    held = [];
                    activity.Complete(HostOperationOutcome.Failed);
                    return new([], 0, LocalEventReason.ClockRollback) { RoutineQuiet = routineQuiet, AdmissionRevision = revision };
                }
                var observed = await source.ReadAsync(session, token).ConfigureAwait(false);
                now = time.GetUtcNow();
                if (now < state.HighWatermark)
                {
                    held = [];
                    activity.Complete(HostOperationOutcome.Failed);
                    return new([], 0, LocalEventReason.ClockRollback) { RoutineQuiet = routineQuiet, AdmissionRevision = revision };
                }
                var snapshot = await source.WithCurrentAsync(session, observed, () =>
                {
                    Require(Eligible);
                    token.ThrowIfCancellationRequested();
                    var proposed = ObserveReceipts(session, observed, now, routineQuiet.Enabled);
                    var receipts = proposed.Receipts.ToList();
                    proposed = proposed with { Receipts = receipts };
                    var views = new List<LocalEventView>();
                    var current = observed.Select(item => receipts.Single(receipt => receipt.Event.Id == item.Id))
                        .OrderByDescending(item => item.Event.Priority).ThenBy(item => item.Event.ObservedAt)
                        .ThenBy(item => item.Event.Id).ToArray();
                    foreach (var receipt in current)
                    {
                        if (routineQuiet.Enabled && RoutineNoticeQuietState.Includes(receipt.Event.Category)) { continue; }
                        var reason = proposed.Reason(receipt, now);
                        if (reason == LocalEventReason.Eligible)
                        {
                            var presented = receipt with { Disposition = LocalEventDisposition.Presented, DeferredUntil = null, ChangedAt = now };
                            receipts[receipts.IndexOf(receipt)] = presented;
                            var budget = proposed.Budgets.SingleOrDefault(item => item.Category == receipt.Event.Category);
                            var budgets = proposed.Budgets.Where(item => item.Category != receipt.Event.Category).ToList();
                            budgets.Add(budget is null || now - budget.WindowStart >= LocalEventBrokerState.Window
                                ? new(receipt.Event.Category, now, 1, now)
                                : budget with { Count = budget.Count + 1, LastPresentation = now });
                            proposed = proposed with { Budgets = budgets };
                            views.Add(new(receipt.Event, reason, receipt.DeferredUntil));
                        }
                    }
                    foreach (var receipt in current.Where(item =>
                        !(routineQuiet.Enabled && RoutineNoticeQuietState.Includes(item.Event.Category))
                        && views.All(view => view.Event.Id != item.Event.Id))
                        .Take(LocalEventSnapshot.MaximumVisible - views.Count))
                    {
                        views.Add(new(receipt.Event, proposed.Reason(receipt, now), receipt.DeferredUntil));
                    }
                    if (!string.Equals(LocalEventBrokerState.Serialize(proposed), LocalEventBrokerState.Serialize(state), StringComparison.Ordinal))
                    { Commit(proposed, request, "local-event.observe", Eligible, token); }
                    Require(Eligible);
                    selected = session;
                    heldAdmission = revision;
                    heldLifetime = nativeLifetime;
                    held = current.Select(item => receipts.Single(receipt => receipt.Event.Id == item.Event.Id).Event).ToArray();
                    return new LocalEventSnapshot(views, Math.Max(0, current.Length - views.Count), LocalEventReason.Eligible)
                    {
                        RoutineQuiet = routineQuiet,
                        AdmissionRevision = revision,
                        RoutineOmitted = current.Count(item => routineQuiet.Enabled && RoutineNoticeQuietState.Includes(item.Event.Category)),
                        RoutineSuppressed = current.Count(item => item.Disposition == LocalEventDisposition.RoutineSuppressed),
                    };
                }, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                Require(Eligible);
                Observed(logger, snapshot.Events.Count, snapshot.Omitted);
                activity.Complete(HostOperationOutcome.Completed);
                return snapshot;
            }
            catch (OperationCanceledException)
            {
                held = [];
                activity.Complete(HostOperationOutcome.Cancelled);
                throw;
            }
            catch (Exception exception)
            {
                held = [];
                Failure(logger, exception.GetType().Name);
                activity.Complete(HostOperationOutcome.Failed);
                throw;
            }
        }
        finally { serial.Release(); }
    }

    public Task<LocalEventView> ExecuteAsync(LocalEventCommand command, RequestOrigin origin,
        Func<bool> originalChannel, CancellationToken cancellationToken) =>
        ExecuteCoreAsync(command, origin, originalChannel, native: false, cancellationToken);

    internal Task<LocalEventView> ExecuteNativeAsync(LocalEventCommand command,
        Func<bool> nativeLifetime, CancellationToken cancellationToken) =>
        ExecuteCoreAsync(command, RequestOrigin.LocalUi, nativeLifetime, native: true, cancellationToken);

    private async Task<LocalEventView> ExecuteCoreAsync(LocalEventCommand command, RequestOrigin origin,
        Func<bool> originalChannel, bool native, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        // This service is not exported to models/tools; ambient provenance cannot be relabelled as UI.
        if ((native ? HostActivity.RequireCurrent().Request.Origin != RequestOrigin.LocalUi
                : HostActivity.Current is { } current && current.Request.Origin != origin)
            || origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || command.Operation is not (LocalEventOperation.Status or LocalEventOperation.Review or LocalEventOperation.Dismiss or LocalEventOperation.Defer))
        { throw new InvalidOperationException("Exact events require fresh original local user input."); }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        var token = linked.Token;
        await serial.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var target = held.SingleOrDefault(item => item.Id == command.Id && item.Revision == command.Revision)
                ?? throw new InvalidOperationException("No host-held exact event ID/revision. Refresh the native selected work surface.");
            bool Eligible() => !disposed && !unavailable && access.CanControl && originalChannel()
                && access.ControlRevision == heldAdmission && selected == target.SessionId && heldLifetime();
            Require(Eligible);
            var request = new HostRequest(new(Guid.NewGuid()), target.SessionId,
                new(Guid.NewGuid()), origin);
            using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request, Causes());
            try
            {
                var now = time.GetUtcNow();
                if (now < state!.HighWatermark)
                {
                    activity.Complete(HostOperationOutcome.Failed);
                    return new(target, LocalEventReason.ClockRollback, null);
                }
                if (now >= target.ExpiresAt)
                {
                    activity.Complete(HostOperationOutcome.Failed);
                    return new(target, LocalEventReason.Expired, null);
                }
                var observed = await source.ReadAsync(target.SessionId, token).ConfigureAwait(false);
                if (!observed.Any(target.SameSource))
                {
                    held = [];
                    activity.Complete(HostOperationOutcome.Failed);
                    return new(target, LocalEventReason.SourceUnavailable, null);
                }
                var result = await source.WithCurrentAsync(target.SessionId, observed, () =>
                {
                    Require(Eligible);
                    token.ThrowIfCancellationRequested();
                    var receipt = state.Receipts.Single(item => item.Event.Id == target.Id);
                    if (receipt.Event.Revision != command.Revision || !receipt.Event.SameSource(target))
                    { throw new InvalidOperationException("The exact event revision changed during admission."); }
                    if (command.Operation is LocalEventOperation.Status or LocalEventOperation.Review)
                    {
                        return new LocalEventView(target, state.Reason(receipt, now), receipt.DeferredUntil);
                    }
                    if (command.Operation == LocalEventOperation.Defer
                        && (receipt.Disposition == LocalEventDisposition.RoutineSuppressed
                            || routineQuiet.Enabled && RoutineNoticeQuietState.Includes(target.Category)))
                    { throw new InvalidOperationException("Quiet-suppressed routine notices cannot be deferred into a replay backlog."); }
                    var until = command.Operation == LocalEventOperation.Defer
                        ? (now + LocalEventBrokerState.Deferral < target.ExpiresAt
                            ? now + LocalEventBrokerState.Deferral : target.ExpiresAt) : (DateTimeOffset?)null;
                    var changed = receipt with
                    {
                        Event = target with { Revision = checked(target.Revision + 1) },
                        Disposition = until is null ? LocalEventDisposition.Dismissed : LocalEventDisposition.Deferred,
                        DeferredUntil = until,
                        ChangedAt = now,
                    };
                    var proposed = state with { HighWatermark = now,
                        Receipts = state.Receipts.Select(item => item.Event.Id == target.Id ? changed : item).ToArray() };
                    Commit(proposed, request, command.Operation == LocalEventOperation.Dismiss
                        ? "local-event.dismiss" : "local-event.defer", Eligible, token);
                    held = held.Select(item => item.Id == target.Id ? changed.Event : item).ToArray();
                    return new LocalEventView(changed.Event, proposed.Reason(changed, now), until);
                }, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                Require(Eligible);
                activity.Complete(HostOperationOutcome.Completed);
                return result;
            }
            catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
            catch { activity.Complete(HostOperationOutcome.Failed); throw; }
        }
        finally { serial.Release(); }
    }

    private static void Require(Func<bool> eligible)
    {
        if (!eligible())
        { throw new InvalidOperationException("Local event original channel, native lifetime, call/privacy/lock/owner admission changed or state is unavailable."); }
    }

    private static ActivityLink[] Causes() => HostActivity.Current?.Activity is { } cause ? [new(cause.Context)] : [];

    public Task RetireSessionAsync(HostId<SessionIdentity> session, CancellationToken cancellationToken) =>
        WithRetirementAsync(async retire =>
        {
            await retire(session, cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    /// <summary>Keep broker-before-authority lock order while retention withdraws and deletes a source.</summary>
    public async Task<T> WithRetirementAsync<T>(
        Func<Func<HostId<SessionIdentity>, CancellationToken, Task>, Task<T>> retirement,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        var token = linked.Token;
        await serial.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await retirement(async (session, retirementToken) =>
            {
                Interlocked.Increment(ref retirementEpoch);
                if (selected == session) { held = []; selected = null; }
                using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), session,
                    new(Guid.NewGuid()), RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Retention);
                try
                {
                    state ??= store.Load() ?? LocalEventBrokerState.Empty(time.GetUtcNow());
                    if (!state.Receipts.Any(item => item.Event.SessionId == session))
                    {
                        activity.Complete(HostOperationOutcome.Completed);
                        return;
                    }
                    var revision = access.ControlRevision;
                    bool Eligible() => !disposed && !unavailable && access.CanControl && access.ControlRevision == revision;
                    Require(Eligible);
                    var now = time.GetUtcNow();
                    var proposed = state with { HighWatermark = now > state.HighWatermark ? now : state.HighWatermark,
                        Receipts = state.Receipts.Where(item => item.Event.SessionId != session).ToArray() };
                    await Task.Run(() => Commit(proposed, activity.Request, "local-event.retire", Eligible, retirementToken),
                        retirementToken).ConfigureAwait(false);
                    activity.Complete(HostOperationOutcome.Completed);
                }
                catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
                catch { activity.Complete(HostOperationOutcome.Failed); throw; }
            }).ConfigureAwait(false);
        }
        finally { serial.Release(); }
    }

    private void Commit(LocalEventBrokerState proposed, HostRequest request, string action, Func<bool> eligible, CancellationToken token)
    {
        var record = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
            action, SecurityAuditOutcome.Requested, request.Origin switch
            {
                RequestOrigin.LocalUi => SecurityAuditInitiator.LocalUser,
                RequestOrigin.ActivatedVoice => SecurityAuditInitiator.VoiceCommand,
                _ => SecurityAuditInitiator.System,
            }, "local-event.suppression");
        using var activity = HostActivity.BeginAudit(request, record);
        var requested = false;
        var terminal = false;
        try
        {
            Require(eligible);
            token.ThrowIfCancellationRequested();
            audit.Write(record);
            requested = true;
            Require(eligible);
            token.ThrowIfCancellationRequested();
            store.BeginWrite();
            store.Save(proposed);
            Require(eligible);
            token.ThrowIfCancellationRequested();
            audit.Write(record.WithOutcome(SecurityAuditOutcome.Succeeded));
            terminal = true;
            Require(eligible);
            token.ThrowIfCancellationRequested();
            store.ConfirmWrite();
            state = proposed;
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception)
        {
            unavailable = true;
            if (requested && !terminal)
            {
                audit.Write(record.WithOutcome(exception is OperationCanceledException
                    ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed, "suppression-unconfirmed"));
            }
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        lock (disposalGate) { return new(disposal ??= CloseAsync()); }
    }

    private async Task CloseAsync()
    {
        disposed = true;
        await lifetime.CancelAsync().ConfigureAwait(false);
        await serial.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        held = [];
        selected = null;
        serial.Dispose();
        lifetime.Dispose();
    }
}
