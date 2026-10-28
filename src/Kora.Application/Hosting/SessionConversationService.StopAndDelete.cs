using System.Diagnostics;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    private SessionStopAndDeleteReview? stopAndDeleteReview;
    private long stopAndDeleteEpoch;
    private readonly HashSet<HostId<SessionIdentity>> stopAndDeleteHolds = [];
    private readonly Dictionary<HostId<SessionIdentity>, (ConversationCancellationTarget Target, ActivityLink Link)> joinedForDisposition = [];

    private ISessionStopAndDeleteStore StopAndDeleteStore => store as ISessionStopAndDeleteStore
        ?? throw new InvalidOperationException("The authoritative compound disposition reader is unavailable.");

    private void RequireConversationDispatchOpen(HostId<SessionIdentity> session)
    {
        lock (cancellationGate)
        {
            ExpireLifecycleBarrier();
            if (lifecycleHeld)
            { throw new InvalidOperationException("Application Exit/Restart is reviewing submitted work. Cancel that review before accepting or resuming requests."); }
            if (stopAndDeleteHolds.Contains(session))
            { throw new InvalidOperationException("This conversation is stopping for reviewed deletion. New dispatch is closed only for this conversation."); }
        }
    }

    public void DiscardStopAndDeleteReview(SessionStopAndDeleteReview review)
    {
        lock (cancellationGate)
        {
            if (!ReferenceEquals(stopAndDeleteReview, review)) { return; }
            stopAndDeleteReview = null;
            stopAndDeleteEpoch++;
        }
    }

    public async Task<SessionStopAndDeleteReview> ReviewStopAndDeleteAsync(SessionWorkspaceEntry target,
        RequestOrigin origin, Func<bool> admitted, CancellationToken token)
    {
        RequireOriginalSource(origin);
        // Existing disposition is native-confirmed only. Preserve the original channel, even after a later click.
        if (origin != RequestOrigin.LocalUi)
        { throw new InvalidOperationException("Stop and delete requires original local UI/typed input. Voice deletion is unavailable; no stop was requested."); }
        var revision = access.ControlRevision;
        bool Live() => access.CanControl && access.ControlRevision == revision && admitted();
        long epoch;
        lock (cancellationGate) { stopAndDeleteReview = null; epoch = ++stopAndDeleteEpoch; }
        if (!Live()) { throw new InvalidOperationException("Private compound review admission is unavailable."); }
        var running = await ReadRunningConversationAsync(target, Live, token).ConfigureAwait(false);
        (ConversationCancellationTarget Target, ActivityLink Link) stopped;
        bool hasStopped;
        lock (cancellationGate) { hasStopped = joinedForDisposition.TryGetValue(target.Authority.SessionId, out stopped); }
        var phase = running is { Phase: ConversationExecutionPhase.Running }
            ? SessionStopAndDeletePhase.Running : hasStopped && running is null
                ? SessionStopAndDeletePhase.WorkStopped
                : throw new InvalidOperationException("This operation requires exact running work or its host-confirmed compound stop. No plain Delete fallback is used.");
        var cancellation = phase == SessionStopAndDeletePhase.Running ? running!.Target : stopped.Target;
        var preview = await StopAndDeleteStore.PreviewStopAndDeleteAsync(target, cancellation, token).ConfigureAwait(false);
        var now = time.GetUtcNow();
        token.ThrowIfCancellationRequested();
        lock (cancellationGate)
        {
            if (!Live() || epoch != stopAndDeleteEpoch || preview.Session != target
                || preview.StoppedWork?.Target != cancellation
                || phase == SessionStopAndDeletePhase.Running
                    && (now < cancellation.CreatedAt || now >= cancellation.ExpiresAt
                        || !runningConversations.TryGetValue(target.Authority.SessionId, out var lease)
                        || lease.Target != cancellation || lease.Status.Phase != ConversationExecutionPhase.Running))
            { throw new InvalidOperationException("The running source changed during review. Nothing was stopped or deleted."); }
            var expires = phase == SessionStopAndDeletePhase.Running ? cancellation.ExpiresAt : now.AddMinutes(1);
            var link = phase == SessionStopAndDeletePhase.Running
                ? runningConversations[target.Authority.SessionId].Link : stopped.Link;
            return stopAndDeleteReview = new(preview, origin, revision, now, expires, phase, link);
        }
    }

    public async Task<SessionStopAndDeleteResult> ConfirmStopAndDeleteAsync(SessionStopAndDeleteReview review,
        Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(admitted);
        RequireOriginalSource(review.Origin);
        var target = review.Disposition.Session;
        var work = review.Disposition.StoppedWork
            ?? throw new InvalidOperationException("The exact reviewed running original is missing.");
        var accepted = false;
        var joined = false;
        var held = false;
        var deletionAttempted = false;
        long epoch = 0;
        bool Live() => !token.IsCancellationRequested && access.CanControl
            && access.ControlRevision == review.ControlRevision && admitted()
            && time.GetUtcNow() >= review.CreatedAt && time.GetUtcNow() < review.ExpiresAt
            && Interlocked.Read(ref stopAndDeleteEpoch) == epoch;
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), target.Authority.SessionId,
            work.Target.Request.TaskId, review.Origin), HostActivityLayer.Application, HostOperation.Request, [review.WorkLink]);
        try
        {
            ConversationCancellationLease? lease = null;
            lock (cancellationGate)
            {
                if (!ReferenceEquals(stopAndDeleteReview, review))
                { throw new InvalidOperationException("No matching single-use compound review. Review again."); }
                stopAndDeleteReview = null;
                epoch = ++stopAndDeleteEpoch;
                if (!Live())
                { throw new InvalidOperationException("The original source, channel, work or authority changed before confirmation."); }
                if (review.Phase == SessionStopAndDeletePhase.Running)
                {
                    if (!runningConversations.TryGetValue(target.Authority.SessionId, out lease)
                        || lease.Target != work.Target || lease.Status.Phase != ConversationExecutionPhase.Running)
                    { throw new InvalidOperationException("The reviewed running work changed before confirmation."); }
                }
                if (!stopAndDeleteHolds.Add(target.Authority.SessionId))
                { throw new InvalidOperationException("This exact conversation already has a compound operation."); }
                held = true;
            }
            var current = await StopAndDeleteStore.PreviewStopAndDeleteAsync(target, work.Target, token).ConfigureAwait(false);
            if (!Live() || current with { ConfirmationId = review.Disposition.ConfirmationId } != review.Disposition)
            { throw new InvalidOperationException("Retained data changed after review. No stop or deletion was requested."); }
            if (lease is not null)
            {
                var cancellationConfirmed = false;
                try
                {
                    await CancelRunningConversationAsync(work.Target, Live, token, review.Origin).ConfigureAwait(false);
                    cancellationConfirmed = true;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                { Failure(logger, exception.GetType().Name); }
                finally { accepted = lease.CancellationAccepted; }
                if (!accepted) { throw new InvalidOperationException("No cancellation was accepted. Nothing was deleted."); }
                // Cancellation/privacy closes deletion, not ownership of the already accepted physical join.
                var completion = await lease.JoinPhysicalCompletionAsync().ConfigureAwait(false);
                joined = true;
                if (!cancellationConfirmed || completion.Phase != ConversationExecutionPhase.Terminal
                    || completion.Outcome != ModelTurnOutcome.Cancelled || completion.TaskState != HostTaskState.Unknown)
                { throw new InvalidOperationException("Work stopped; not deleted. Confirmed original cancellation receipts are required."); }
                lock (cancellationGate) { joinedForDisposition[target.Authority.SessionId] = (work.Target, review.WorkLink); }
            }
            else { joined = true; }
            if (!Live()
                || await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false) != target)
            { throw new InvalidOperationException("Work stopped; not deleted. Fresh private review and confirmed cancellation receipts are required."); }
            deletionAttempted = true;
            var receipt = await workspace.CommitStoppedDispositionAsync(review.Disposition, review.Origin,
                Live, token).ConfigureAwait(false);
            lock (cancellationGate) { joinedForDisposition.Remove(target.Authority.SessionId); }
            activity.Complete(HostOperationOutcome.Completed);
            return new(SessionStopAndDeleteOutcome.Deleted, receipt);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Unknown);
            return new(deletionAttempted ? SessionStopAndDeleteOutcome.DeletionUnconfirmed
                : joined ? SessionStopAndDeleteOutcome.WorkStoppedNotDeleted
                : accepted ? SessionStopAndDeleteOutcome.StopUnconfirmed : SessionStopAndDeleteOutcome.NotStopped,
                Recovery: exception.Message);
        }
        finally
        {
            if (held) { lock (cancellationGate) { stopAndDeleteHolds.Remove(target.Authority.SessionId); } }
        }
    }
}
