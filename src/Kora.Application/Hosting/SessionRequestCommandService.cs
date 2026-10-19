using System.Diagnostics;

using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>Bounded original-source review and one-shot confirmation for the canonical retained-submit action.</summary>
public sealed class SessionRequestCommandService(
    SessionWorkspaceService workspace, SessionConversationService conversation,
    ISessionWorkspaceAccess access, TimeProvider time)
{
    private readonly Lock gate = new();
    private Pending? pending;
    private long epoch;

    public void Retire()
    {
        lock (gate) { pending = null; epoch++; }
    }

    public async Task<SessionCommandResult> ExecuteAsync(SessionCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token)
    {
        SessionConversationService.RequireOriginalSource(origin);
        var session = new HostId<SessionIdentity>(command.SessionId
            ?? throw new InvalidOperationException("An exact session ID is required."));
        Pending? previous;
        long attempt;
        lock (gate) { previous = pending; pending = null; attempt = ++epoch; }
        var control = access.ControlRevision;
        bool Live() => access.CanInspect && access.CanControl && access.ControlRevision == control && admitted();
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), session, new(Guid.NewGuid()), origin),
            HostActivityLayer.Application, HostOperation.Request);
        try
        {
            token.ThrowIfCancellationRequested();
            if (!Live()) { throw new InvalidOperationException("Original private input admission changed; initiate a fresh review."); }
            if (command.Operation == SessionCommandOperation.RequestReview)
            {
                var body = command.RequestBody ?? throw new InvalidOperationException("An original request body is required.");
                ConversationText.Validate(body, submission: true);
                var target = await workspace.ReadExactMetadataAsync(session, token).ConfigureAwait(false);
                if (target.Authority.SessionId != session || !target.Authority.IsActive
                    || target.Authority.Generation.Value != command.Generation
                    || (target.Metadata?.Revision.Value ?? 0) != command.MetadataRevision || !Live())
                { throw new InvalidOperationException("Review requires the exact current Active session, generation and metadata revision."); }
                var review = new SessionRequestReview(Guid.NewGuid(), target, body, origin, time.GetUtcNow().AddSeconds(60));
                lock (gate)
                {
                    if (epoch != attempt || !Live()) { throw new InvalidOperationException("Review admission changed."); }
                    pending = new(review, Live, control, new(activity.Activity!.Context));
                }
                activity.Complete(HostOperationOutcome.Completed);
                return new("review", "Review the exact original body, target, origin and expiry. Confirm separately using the exact session and review IDs; no content is retained or dispatched yet.")
                { RequestReview = review };
            }
            if (previous is null || previous.Review.Id != command.RequestReviewId
                || previous.Review.Target.Authority.SessionId != session)
            { throw new InvalidOperationException("The exact pending review is missing or consumed. Initiate a fresh review; no replay."); }
            if (command.Operation == SessionCommandOperation.RequestCancel)
            {
                activity.Complete(HostOperationOutcome.Completed);
                return new("cancelled", "The exact memory-only request review was discarded. No submission or session change.");
            }
            if (command.Operation != SessionCommandOperation.RequestConfirm)
            { throw new InvalidOperationException("Only explicit request review, confirm or cancel is supported."); }
            bool OriginalLive()
            {
                lock (gate)
                {
                    return epoch == attempt && Live() && previous.SourceAdmitted()
                        && access.ControlRevision == previous.ControlRevision && time.GetUtcNow() < previous.Review.ExpiresAt;
                }
            }
            async Task Revalidate(CancellationToken cancellation)
            {
                if (!OriginalLive()) { throw new InvalidOperationException("The original request source or review expired. Review again; later confirmation grants no new authority."); }
                var actual = await workspace.ReadExactMetadataAsync(session, cancellation).ConfigureAwait(false);
                if (actual != previous.Review.Target || !OriginalLive())
                { throw new InvalidOperationException("The reviewed session generation, metadata or original admission changed."); }
            }
            await Revalidate(token).ConfigureAwait(false);
            // A local confirmation may confirm voice content, but cannot convert its original source to LocalUi.
            using var submission = HostActivity.BeginRoot(new(new(Guid.NewGuid()), session, new(Guid.NewGuid()), previous.Review.Origin),
                HostActivityLayer.Application, HostOperation.Request, [previous.Cause, new(activity.Activity!.Context)]);
            ConversationSubmissionReceipt receipt;
            try
            {
                receipt = await conversation.SubmitAsync(previous.Review.Target, previous.Review.Body, OriginalLive, token,
                    Revalidate, [previous.Cause], previous.Review.Origin).ConfigureAwait(false);
                submission.Complete(HostOperationOutcome.Completed);
            }
            catch
            {
                submission.Complete(HostOperationOutcome.Failed);
                throw;
            }
            activity.Complete(HostOperationOutcome.Completed);
            return new("retained", "Exact original USER request retained with its original channel and terminal HOST/task outcome. Inspect these receipts; no fallback or automatic replay.")
            { RequestSubmission = receipt };
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

    private sealed record Pending(SessionRequestReview Review, Func<bool> SourceAdmitted, long ControlRevision, ActivityLink Cause);
}
