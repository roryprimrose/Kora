using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.Interaction;

public sealed partial class LocalEventBroker
{
    // Internal native seam: neither model tools nor ambient HostSystem callbacks can change this choice.
    internal async Task<RoutineNoticeQuietState> ChangeRoutineQuietNativeAsync(
        HostId<SessionIdentity> session, bool enabled, long choiceRevision,
        Func<bool> nativeLifetime, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var original = HostActivity.RequireCurrent();
        var request = original.Request;
        if (request.Origin != RequestOrigin.LocalUi || request.SessionId != session || original.Activity!.IsStopped)
        { throw new InvalidOperationException("Quiet routine notices requires fresh exact original native selected-session input."); }
        var admission = access.ControlRevision;
        var retirement = Volatile.Read(ref retirementEpoch);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        var token = linked.Token;
        bool Admitted() => !disposed && !unavailable && !token.IsCancellationRequested
            && access.CanControl && access.ControlRevision == admission && nativeLifetime()
            && Volatile.Read(ref retirementEpoch) == retirement
            && !original.Activity.IsStopped && ReferenceEquals(HostActivity.RequireCurrent().Request, request);
        bool Eligible() => Admitted() && routineQuiet.Revision == choiceRevision;
        Require(Eligible);
        await serial.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Require(Eligible);
            using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Policy);
            var record = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
                enabled ? "local-event.routine-quiet.on" : "local-event.routine-quiet.clear",
                SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "local-event.routine-quiet");
            var requested = false;
            var terminal = false;
            try
            {
                state ??= store.Load() ?? LocalEventBrokerState.Empty(time.GetUtcNow());
                state.Validate();
                var now = time.GetUtcNow();
                if (now < state.HighWatermark) { throw new InvalidOperationException("Quiet control held by the UTC clock watermark."); }
                using var auditActivity = HostActivity.BeginAudit(request, record);
                audit.Write(record);
                requested = true;
                Require(Eligible);
                var observed = await source.ReadAsync(session, token).ConfigureAwait(false);
                await source.WithCurrentAsync(session, observed, () =>
                {
                    Require(Eligible);
                    now = time.GetUtcNow();
                    if (now < state.HighWatermark) { throw new InvalidOperationException("Quiet control held by the UTC clock watermark."); }
                    // Clear burns current muted sources too: it authorizes future new observations, not a backlog.
                    var proposed = ObserveReceipts(session, observed, now, enabled || routineQuiet.Enabled);
                    Commit(proposed, request, "local-event.quiet-suppression", Eligible, token);
                    Require(Eligible);
                    audit.Write(record.WithOutcome(SecurityAuditOutcome.Succeeded));
                    terminal = true;
                    Require(Eligible);
                    token.ThrowIfCancellationRequested();
                    routineQuiet = new(enabled, checked(choiceRevision + 1));
                    held = [];
                    selected = null;
                    return true;
                }, token).ConfigureAwait(false);
                Require(Admitted);
                token.ThrowIfCancellationRequested();
                auditActivity.Complete(HostOperationOutcome.Completed);
                activity.Complete(HostOperationOutcome.Completed);
                QuietChanged(logger, enabled, routineQuiet.Revision);
                return routineQuiet;
            }
            catch (Exception exception)
            {
                unavailable = true;
                held = [];
                if (requested && !terminal)
                {
                    using var failedAudit = HostActivity.BeginAudit(request, record);
                    audit.Write(record.WithOutcome(exception is OperationCanceledException
                        ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed, "quiet-control-unconfirmed"));
                    failedAudit.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
                }
                Failure(logger, exception.GetType().Name);
                activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
                throw;
            }
        }
        finally { serial.Release(); }
    }

    private LocalEventBrokerState ObserveReceipts(HostId<SessionIdentity> session,
        IReadOnlyList<LocalEvent> observed, DateTimeOffset now, bool suppressRoutine)
    {
        var receipts = state!.Receipts.ToList();
        foreach (var item in observed)
        {
            item.Validate();
            if (item.SessionId != session)
            { throw new InvalidDataException("A host source cannot retarget the selected session."); }
            var prior = receipts.SingleOrDefault(receipt => receipt.Event.Id == item.Id);
            if (prior is not null && prior.Event.SameSource(item)) { continue; }
            if (prior is not null && (item.Generation < prior.Event.Generation
                || item.Generation == prior.Event.Generation && item.SourceRevision < prior.Event.SourceRevision))
            { throw new InvalidDataException("An obsolete source revision cannot replace a host event."); }
            if (prior is not null) { receipts.Remove(prior); }
            if (receipts.Count == LocalEventBrokerState.MaximumReceipts)
            { throw new InvalidOperationException("Bounded event suppression capacity is full; nothing was evicted or presented."); }
            receipts.Add(new(item with { Revision = checked((prior?.Event.Revision ?? 0) + 1) },
                suppressRoutine && RoutineNoticeQuietState.Includes(item.Category)
                    ? LocalEventDisposition.RoutineSuppressed : LocalEventDisposition.Eligible, null, now));
        }
        if (suppressRoutine)
        {
            // Retain all previously observed session/revision watermarks without evicting or renewing expiry.
            receipts = receipts.Select(receipt =>
                RoutineNoticeQuietState.Includes(receipt.Event.Category)
                    && receipt.Disposition is LocalEventDisposition.Eligible or LocalEventDisposition.Deferred
                ? receipt with { Event = receipt.Event with { Revision = checked(receipt.Event.Revision + 1) },
                    Disposition = LocalEventDisposition.RoutineSuppressed, DeferredUntil = null, ChangedAt = now }
                : receipt).ToList();
        }
        return state with { Schema = receipts.Any(item => item.Disposition == LocalEventDisposition.RoutineSuppressed) ? 2 : state.Schema,
            HighWatermark = now, Receipts = receipts };
    }
}
