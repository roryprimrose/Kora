using System.Diagnostics;

using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>Source-bound passive retention review and one-shot handoff to the native-only action.</summary>
public sealed class SessionRetentionCommandService(
    SessionWorkspaceService workspace, ISessionWorkspaceAccess access, TimeProvider time)
{
    private readonly Lock gate = new();
    private Pending? pending;
    private long epoch;

    public void Retire()
    {
        lock (gate) { pending = null; epoch++; }
    }

    public async Task<SessionCommandResult> ExecuteAsync(SessionCommand command, RequestOrigin origin,
        Func<bool> admitted, Func<SessionRetentionReview, Func<bool>, CancellationToken, Task> openNativeReview,
        CancellationToken token)
    {
        Pending? previous;
        long attempt;
        lock (gate) { previous = pending; pending = null; attempt = ++epoch; }
        SessionConversationService.RequireOriginalSource(origin);
        var session = new HostId<SessionIdentity>(command.SessionId
            ?? throw new InvalidOperationException("An exact session ID is required."));
        var control = access.ControlRevision;
        bool Live() => access.CanInspect && access.ControlRevision == control && admitted();
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), session, new(Guid.NewGuid()), origin),
            HostActivityLayer.Application, HostOperation.Policy);
        try
        {
            token.ThrowIfCancellationRequested();
            if (!Live()) { throw new InvalidOperationException("Original private input changed; initiate a fresh review."); }
            if (command.Operation is SessionCommandOperation.RetentionStatus or SessionCommandOperation.RetentionReview)
            {
                var observed = await workspace.ReadRetentionAsync(session, token).ConfigureAwait(false);
                if (!Live()) { throw new InvalidOperationException("Retention observation admission changed."); }
                if (command.Operation == SessionCommandOperation.RetentionStatus)
                {
                    activity.Complete(HostOperationOutcome.Completed);
                    return new("observed", "Exact passive retention metadata only. No maintenance, activity renewal or cleanup eligibility is inferred.")
                    { Retention = observed };
                }
                if (!access.CanControl || observed.Removed || observed.State.Purged)
                { throw new InvalidOperationException("Current native control admission and a nonremoved, nonpurged session are required."); }
                var review = new SessionRetentionReview(Guid.NewGuid(), observed, command.KeepRetention, origin, time.GetUtcNow().AddSeconds(60));
                lock (gate)
                {
                    if (epoch != attempt || !Live()) { throw new InvalidOperationException("Retention review admission changed."); }
                    pending = new(review, Live, control, new(activity.Activity!.Context));
                }
                activity.Complete(HostOperationOutcome.Completed);
                return new("review", "Review the exact unchanged UTC dates, generation, exemption audit revision, original channel and expiry. "
                    + "Ordinary retention may archive/delete soon, including already-due dates. Confirm once to open the exact native review; "
                    + "a genuine NEW native decision is required to change the SESSION hold, not an operation grant or memory permission.")
                { RetentionReview = review };
            }
            if (previous is null || previous.Review.Id != command.RetentionReviewId
                || previous.Review.Observation.State.SessionId != session)
            { throw new InvalidOperationException("The exact pending retention review is missing or consumed. Review again; no replay."); }
            if (command.Operation == SessionCommandOperation.RetentionCancel)
            {
                activity.Complete(HostOperationOutcome.Completed);
                return new("cancelled", "Memory-only retention review discarded. No native review opened or hold changed.");
            }
            if (command.Operation != SessionCommandOperation.RetentionConfirm)
            { throw new InvalidOperationException("Only exact retention status, review, confirm or cancel is supported."); }
            bool OriginalLive()
            {
                lock (gate)
                {
                    return epoch == attempt && Live() && access.CanControl && previous.SourceAdmitted()
                        && access.ControlRevision == previous.ControlRevision && time.GetUtcNow() < previous.Review.ExpiresAt;
                }
            }
            if (!OriginalLive()) { throw new InvalidOperationException("Original retention source or review expired; initiate a fresh review."); }
            var actual = await workspace.ReadRetentionAsync(session, token).ConfigureAwait(false);
            if (!actual.MatchesRecordedState(previous.Review.Observation) || !OriginalLive())
            { throw new InvalidOperationException("Reviewed retention dates, generation, exemption audit revision or source changed."); }
            // Opening is not a native decision. Preserve the source and link its cause; never call the hold writer here.
            using var handoff = HostActivity.BeginRoot(new(new(Guid.NewGuid()), session, new(Guid.NewGuid()), previous.Review.Origin),
                HostActivityLayer.Application, HostOperation.Presentation, [previous.Cause, new(activity.Activity!.Context)]);
            try
            {
                await openNativeReview(previous.Review, OriginalLive, token).ConfigureAwait(false);
                if (!OriginalLive()) { throw new InvalidOperationException("Native review handoff is unconfirmed. Refresh; no automatic retry."); }
                handoff.Complete(HostOperationOutcome.Completed);
            }
            catch
            {
                handoff.Complete(HostOperationOutcome.Failed);
                throw;
            }
            activity.Complete(HostOperationOutcome.Completed);
            return new("native-review", "Exact native retention review opened. NO hold change or audit commit is claimed. "
                + "Only a genuine new native confirmation uses the existing authoritative action; the original command was consumed.")
            { RetentionReview = previous.Review };
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    private sealed record Pending(SessionRetentionReview Review, Func<bool> SourceAdmitted, long ControlRevision, ActivityLink Cause);
}
