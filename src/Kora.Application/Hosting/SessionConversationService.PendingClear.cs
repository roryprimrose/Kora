using System.Collections.Immutable;

using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    private readonly Dictionary<HostId<SessionIdentity>, ConversationPendingClearReview> pendingClearReviews = [];

    private static ImmutableArray<ConversationPendingClearEntry> PendingClearEntries(
        HostId<SessionIdentity> session, ConversationLane lane) =>
        [.. Snapshot(session, lane).Turns.Where(item => item.Phase == ConversationPendingPhase.Pending)
            .Select(item => new ConversationPendingClearEntry(item.Submission.Message.Request,
                item.Submission.Task.Revision, item.Submission.Message.Revision, item.Submission.Message.Sequence,
                item.Position, item.ExpiresAt))];

    public void DiscardPendingClearReview(ConversationPendingClearReview review)
    {
        lock (cancellationGate)
        {
            if (pendingClearReviews.GetValueOrDefault(review.Snapshot.Session) == review)
            { pendingClearReviews.Remove(review.Snapshot.Session); }
        }
    }

    public async Task<ConversationPendingClearReview> ReviewPendingClearAsync(SessionWorkspaceEntry target,
        RequestOrigin origin, Func<bool> admitted, CancellationToken token)
    {
        RequireOriginalSource(origin);
        var control = access.ControlRevision;
        var created = time.GetUtcNow();
        bool Live() => !token.IsCancellationRequested && access.CanInspect && access.CanControl
            && access.ControlRevision == control && admitted();
        if (!Live()) { throw new InvalidOperationException("Current original-user private control is required to review ordinary pending work."); }
        var actual = await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false);
        ConversationPendingClearReview review;
        lock (cancellationGate)
        {
            RequireConversationDispatchOpen(target.Authority.SessionId);
            if (pendingClosed || !Live() || actual != target || !target.Authority.IsActive
                || !conversationLanes.TryGetValue(target.Authority.SessionId, out var lane)
                || lane.Entries.Any(item => item.Retained is null || item.CancelRequested))
            { throw new InvalidOperationException("Refresh the exact Active ordinary conversation after its current admission or cancellation finishes."); }
            var entries = PendingClearEntries(target.Authority.SessionId, lane);
            if (entries.IsEmpty)
            { throw new InvalidOperationException("No current never-dispatched ordinary requests to review. Running or Unknown work is not a pending-only clear."); }
            var expires = entries.Min(item => item.ExpiresAt);
            var snapshot = new ConversationPendingClearSnapshot(Guid.NewGuid(), target.Authority.SessionId,
                target.Authority.Generation, target.Metadata?.Revision.Value ?? 0, lane.Revision, origin,
                created, expires, entries, runningConversations.GetValueOrDefault(target.Authority.SessionId)?.Status);
            _ = SessionCommandResult.Serialize(new("review", PendingClearImpact) { PendingClearReview = snapshot });
            review = new(target, snapshot, control, Live);
            pendingClearReviews[target.Authority.SessionId] = review;
        }
        try
        {
            await ValidatePendingClearSourcesAsync(review, () => true, token).ConfigureAwait(false);
            return review;
        }
        catch
        {
            DiscardPendingClearReview(review);
            throw;
        }
    }

    public const string PendingClearImpact =
        "Clear ONLY every listed never-dispatched ordinary pending request after separate confirmation. "
        + "Positions, identities and count are the complete reviewed set. Running/already-dispatching work and other conversations continue. "
        + "Original USER messages, task/audit history, grants and retention remain. No provider, resource lease, capture or physical stop. "
        + "New arrivals, dispatch, expiry or changed original authority require fresh review. Existing dispatch holds are not released. "
        + "Bodies are not in this snapshot; inspect an original separately with the existing conversation reader.";

    private void RequirePendingClearCurrent(ConversationPendingClearReview review, Func<bool> admitted)
    {
        var snapshot = review.Snapshot;
        var now = time.GetUtcNow();
        var lane = conversationLanes[snapshot.Session];
        if (pendingClosed || !access.CanInspect || !access.CanControl
            || access.ControlRevision != review.ControlRevision || !review.SourceCurrent() || !admitted()
            || now < snapshot.CreatedAt || now >= snapshot.ExpiresAt
            || lane.Revision != snapshot.LaneRevision
            || lane.Entries.Any(item => item.Retained is null))
        { throw new InvalidOperationException("The exact pending set, original source, private control or review expiry changed. Review again; no running or unreviewed request was cancelled."); }
    }

    private async Task ValidatePendingClearSourcesAsync(ConversationPendingClearReview review,
        Func<bool> admitted, CancellationToken token)
    {
        if (await store.ReadMetadataAsync(review.Snapshot.Session, token).ConfigureAwait(false) != review.Target)
        { throw new InvalidOperationException("The reviewed session ownership, generation or metadata changed."); }
        foreach (var entry in review.Snapshot.Entries)
        {
            var observed = await store.ReadTaskAsync(entry.Request.SessionId, entry.Request.TaskId, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (observed is null || !observed.CurrentSource || observed.Generation != review.Target.Authority.Generation
                || observed.Task.Request != entry.Request || observed.Task.Revision != entry.TaskRevision
                || observed.Task.State != HostTaskState.IntentRecorded)
            { throw new InvalidOperationException("A reviewed original is no longer an exact current-run never-dispatched intent. No active cancellation fallback."); }
            lock (cancellationGate) { RequirePendingClearCurrent(review, admitted); }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003",
        Justification = "Joins only captured never-dispatched host workers and their durable readback, never the active provider.")]
    public async Task<ConversationPendingClearResult> ConfirmPendingClearAsync(ConversationPendingClearReview review,
        Func<bool> admitted, CancellationToken token)
    {
        RequireOriginalSource(review.Snapshot.Origin);
        var snapshot = review.Snapshot;
        ConversationLane lane;
        PendingConversation[] pending;
        lock (cancellationGate)
        {
            if (pendingClearReviews.GetValueOrDefault(snapshot.Session) != review)
            { throw new InvalidOperationException("The exact single-use pending-only clear review is missing or consumed."); }
            pendingClearReviews.Remove(snapshot.Session);
            RequireConversationDispatchOpen(snapshot.Session);
            token.ThrowIfCancellationRequested();
            RequirePendingClearCurrent(review, admitted);
            lane = conversationLanes[snapshot.Session];
            pending = snapshot.Entries.Select(entry => lane.Entries.Single(item =>
                item.Retained!.Message.Request == entry.Request)).ToArray();
            // This reason only closes dispatch/admission for the transaction; it never releases a recovery/manual hold.
            lane.PendingClearHeld = true;
        }
        var requested = false;
        var outcome = "changed-before-cancellation";
        ConversationPendingClearResult result;
        var notificationFailed = false;
        bool CancellationCurrent() => !token.IsCancellationRequested && review.SourceCurrent() && admitted()
            && access.CanInspect && access.CanControl && access.ControlRevision == review.ControlRevision
            && time.GetUtcNow() >= snapshot.CreatedAt && time.GetUtcNow() < snapshot.ExpiresAt;
        try
        {
            await ValidatePendingClearSourcesAsync(review, admitted, token).ConfigureAwait(false);
            lock (cancellationGate)
            {
                RequirePendingClearCurrent(review, admitted);
                // Audit uncertainty retains the independent recovery hold, even if no withdrawal was accepted.
                lane.Paused = true;
                outcome = "audit-or-authority-unconfirmed";
                foreach (var entry in pending)
                {
                    using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), snapshot.Session,
                        entry.Retained!.Task.Request.TaskId, snapshot.Origin), HostActivityLayer.Application,
                        HostOperation.Request, [entry.Cause]);
                    AuditPendingControl("conversation.pending-clear", () =>
                    {
                        RequirePendingClearCurrent(review, admitted);
                        return !token.IsCancellationRequested;
                    }, snapshot.Origin);
                    activity.Complete(HostOperationOutcome.Completed);
                }
                RequirePendingClearCurrent(review, admitted);
                token.ThrowIfCancellationRequested();
                foreach (var entry in pending)
                {
                    entry.PendingCancellationCurrent = CancellationCurrent;
                    entry.PendingCancellationTarget = review.Target;
                    entry.CancelRequested = true;
                }
                requested = true;
                lane.Pulse();
            }
            await Task.WhenAll(pending.Select(item => item.Worker)).ConfigureAwait(false);
            var completions = await Task.WhenAll(pending.Select(item => item.Completed.Task)).ConfigureAwait(false);
            var facts = snapshot.Entries.Zip(completions, (entry, completion) =>
            {
                var receipt = completion.Receipt;
                var known = completion.Failure is null && receipt is not null
                    && receipt.Message.Request == entry.Request && receipt.Task.Request == entry.Request
                    && receipt.Task.IsTerminal && receipt.Task.State != HostTaskState.Unknown
                    && receipt.Message.Execution is null;
                var cancelled = known && receipt!.Task.State == HostTaskState.Cancelled
                    && receipt.Message.Outcome == ModelTurnOutcome.Cancelled && receipt.Message.Reason == ModelTurnReason.CallerCancelled;
                return new ConversationPendingClearOutcome(entry, true, known ? receipt!.Task.State : null,
                    cancelled, cancelled ? "cancelled-retained" : known ? "changed-terminal-retained" : "unknown-inspect-exact-original");
            }).ToImmutableArray();
            outcome = facts.All(item => item.Confirmed && item.RetainedState == HostTaskState.Cancelled)
                ? "reviewed-pending-cancelled" : "partial-or-unconfirmed";
            result = PendingClearResult(snapshot, lane, facts, outcome);
        }
        catch (Exception exception) when (!requested && exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            result = PendingClearResult(snapshot, lane,
                [.. snapshot.Entries.Select(entry => new ConversationPendingClearOutcome(entry, false, null, false, "not-requested"))], outcome);
        }
        finally
        {
            lock (cancellationGate)
            {
                lane.PendingClearHeld = false;
                lane.Pulse();
                try { PendingConversationChanged?.Invoke(Snapshot(snapshot.Session, lane)); }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    Failure(logger, exception.GetType().Name);
                    notificationFailed = true;
                }
            }
        }
        return notificationFailed ? result with { Outcome = "partial-or-unconfirmed" } : result;
    }

    private ConversationPendingClearResult PendingClearResult(ConversationPendingClearSnapshot snapshot, ConversationLane lane,
        ImmutableArray<ConversationPendingClearOutcome> outcomes, string outcome)
    {
        lock (cancellationGate)
        {
            return new(snapshot, outcomes, PendingClearEntries(snapshot.Session, lane),
                runningConversations.GetValueOrDefault(snapshot.Session)?.Status, lane.Paused, outcome);
        }
    }
}
