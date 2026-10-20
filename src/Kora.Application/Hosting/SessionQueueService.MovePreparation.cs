using System.Diagnostics;

using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionQueueService
{
    internal async Task<(SessionWorkSnapshot Work, SessionQueueMoveReview Review, Func<bool> IsCurrent)> PrepareNativeMoveReviewAsync(
        SessionQueueMoveReviewCommand command, RequestOrigin origin, Func<bool> admitted, CancellationToken token)
    {
        if (command.Error is not null || command.SessionId == Guid.Empty || command.TaskId == Guid.Empty
            || command.NeighborId == Guid.Empty || command.TaskId == command.NeighborId
            || origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        { throw new InvalidOperationException("An exact original adjacent-pair preparation request is required."); }
        var session = new HostId<SessionIdentity>(command.SessionId);
        var revision = access.ControlRevision;
        var configurationRevision = ConfigurationRevision;
        var configuredLimits = configuration?.Get().Effective;
        bool Eligible() => !disposed && !token.IsCancellationRequested && admitted()
            && access.CanInspect && access.CanControl && access.ControlRevision == revision
            && ConfigurationRevision == configurationRevision
            && (configuration is null || configuredLimits is { } current && configuration.IsCurrent(current));
        ActivityLink[] preparationLinks = HostActivity.Current?.Activity is { } cause ? [new(cause.Context)] : [];
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), session, new(Guid.NewGuid()), origin),
            HostActivityLayer.Application, HostOperation.Request, preparationLinks);
        try
        {
            token.ThrowIfCancellationRequested();
            // Work and the revisioned review must describe the same source, not a later adjacent pair.
            var work = await ReadWorkAsync(session, revision, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            work.RequireSubject(session);
            RequireEligible(Eligible);
            if (!work.Session.Authority.IsActive || work.Queue.DispatchControl is not { } control)
            { throw new InvalidOperationException("The exact active queue/control source is unavailable."); }
            var selected = work.Queue.Entries.SingleOrDefault(entry => entry.Request.TaskId.Value == command.TaskId);
            var neighbor = work.Queue.Entries.SingleOrDefault(entry => entry.Request.TaskId.Value == command.NeighborId);
            if (selected is null || neighbor is null)
            { throw new InvalidOperationException("Both exact requested task IDs must belong to this observed queue."); }
            var exact = new SessionCommand(command.Up ? SessionCommandOperation.QueueMoveUp : SessionCommandOperation.QueueMoveDown,
                session.Value, work.Queue.Generation.Value)
            {
                QueueRevision = work.Queue.Revision, QueueRunId = control.RunId, QueueControlRevision = control.Revision,
                TaskId = command.TaskId, TaskRevision = selected.Revision.Value,
                QueueNeighborId = command.NeighborId, QueueNeighborRevision = neighbor.Revision.Value,
            };
            var review = await ReviewMoveAsync(exact, session, revision, Eligible, token).ConfigureAwait(false);
            _ = SessionCommandResult.Serialize(new("reviewed", SessionQueueMoveReviewCommand.Boundary)
                { Queue = review.Queue, QueueMoveReview = review });
            bool ReviewCurrent() => Eligible() && review.Selected.ExpiresAt > time.GetUtcNow()
                && review.Neighbor.ExpiresAt > time.GetUtcNow();
            await WithLimitsAsync(async _ =>
            {
                var metadata = await workspace.ReadMetadataAsync(session, token).ConfigureAwait(false);
                var current = await store.ReadQueueAsync(session, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                RequireEligible(Eligible);
                if (metadata != work.Session || !work.Queue.MatchesRecordedState(review.Queue)
                    || !review.Queue.MatchesRecordedState(current))
                { throw new InvalidOperationException("Original queue, metadata or pair changed; request a fresh command."); }
                var refusal = SessionQueueOrdering.Refusal(SessionQueueOrdering.Pending(current.Entries, session, current.Orders),
                    review.Selected, review.Neighbor, review.Up, control.RunId, current.Generation, revision, time.GetUtcNow());
                if (refusal is not null) { throw new InvalidOperationException(refusal); }
                // Only the native one-shot LocalUi confirmation may consume this handoff.
                // Original input/private/config admission remains a required binding, not confirmation authority.
                lock (causeGate) { moveReviews.Clear(); moveReviews[session] = (review, RequestOrigin.LocalUi, ReviewCurrent); }
                return true;
            }, token).ConfigureAwait(false);
            activity.Complete(HostOperationOutcome.Completed);
            return (work, review, ReviewCurrent);
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }
}
