using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionQueueService
{
    private readonly Dictionary<HostId<SessionIdentity>, (SessionQueueRequeueReview Review, RequestOrigin Origin, Func<bool> Eligible)> requeueReviews = [];
    private long ConfigurationRevision => configuration?.Get().Revision ?? 0;

    private async Task<SessionCommandResult> ReviewRequeueAsync(SessionCommand command, HostId<SessionIdentity> session,
        long admissionRevision, RequestOrigin origin, Func<bool> eligible, CancellationToken token)
    {
        lock (causeGate) { requeueReviews.Clear(); }
        var snapshot = await store.ReadQueueAsync(session, token).ConfigureAwait(false);
        if (snapshot.Generation.Value != command.Generation || snapshot.Revision != command.QueueRevision
            || snapshot.DispatchControl is not { } control || control.RunId != command.QueueRunId || control.Revision != command.QueueControlRevision)
        {
            throw new InvalidOperationException("Refresh the exact queue, run and pause revisions before reviewing expired replacement.");
        }
        var fresh = new HostRequest(new(Guid.NewGuid()), session, new(Guid.NewGuid()), origin);
        var preview = await WithLimitsAsync(current => store.ReviewExpiredAsync(snapshot, RequireTask(command),
            new(command.TaskRevision), fresh, admissionRevision, current, ConfigurationRevision,
            () => eligible() && (configuration is null || configuration.IsCurrent(current)), token).AsTask(), token).ConfigureAwait(false);
        var result = new SessionCommandResult("reviewed", RequeueDisclosure) { Queue = snapshot, QueueRequeueReview = preview };
        _ = SessionCommandResult.Serialize(result);
        token.ThrowIfCancellationRequested();
        RequireEligible(eligible);
        lock (causeGate) { requeueReviews[session] = (preview, origin, eligible); }
        return result;
    }

    private (SessionQueueRequeueReview Review, Func<bool> Eligible) ConsumeRequeueReview(SessionCommand command, HostId<SessionIdentity> session,
        RequestOrigin origin, bool discard = false)
    {
        lock (causeGate)
        {
            if (!requeueReviews.Remove(session, out var pending) || pending.Origin != origin || !pending.Eligible()
                || pending.Review.Id != command.QueueRequeueReviewId
                || !discard && (pending.Review.Queue.Generation.Value != command.Generation || pending.Review.Queue.Revision != command.QueueRevision
                    || pending.Review.Queue.DispatchControl!.RunId != command.QueueRunId
                    || pending.Review.Queue.DispatchControl.Revision != command.QueueControlRevision
                    || pending.Review.Original.Request.TaskId.Value != command.TaskId
                    || pending.Review.Original.Revision.Value != command.TaskRevision
                    || pending.Review.FreshRequest.RequestId.Value != command.FreshRequestId
                    || pending.Review.FreshRequest.TaskId.Value != command.FreshTaskId))
            {
                throw new InvalidOperationException("The exact expired replacement review is stale or changed. No source was removed or fresh decision admitted.");
            }
            return (pending.Review, pending.Eligible);
        }
    }

    private const string RequeueDisclosure = "Remove only the exact expired current-run pending source from dispatch, retaining Removed queue history and Cancelled task outcome. "
        + "Enqueue the separately reviewed NEW request/task, same bounded profile and original exact prerequisite, at the session tail with a fresh captured pending deadline. "
        + "Existing dependents still reference the removed task and remain blocked; no cascade, retarget, replay, old deadline/result rewrite, automatic dispatch or pause change. "
        + "Future active budget is captured only at later normal manual dispatch.";
}
