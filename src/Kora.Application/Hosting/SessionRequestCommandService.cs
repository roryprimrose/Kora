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
        Pending? previous;
        long attempt;
        lock (gate) { previous = pending; pending = null; attempt = ++epoch; }
        var control = access.ControlRevision;
        bool Live() => access.CanInspect && access.CanControl && access.ControlRevision == control && admitted();
        if (command.RequestUsesPendingReview)
        {
            if (command.SessionId is not null || command.RequestReviewId is not null
                || command.Operation is not (SessionCommandOperation.RequestConfirm or SessionCommandOperation.RequestCancel)
                || previous is null)
            { throw new InvalidOperationException("No single current host-held request review is available. Review an explicit conversation first."); }
            command = command with
            { SessionId = previous.Review.Target.Authority.SessionId.Value, RequestReviewId = previous.Review.Id };
        }
        if (command.SessionId is null && command.Operation == SessionCommandOperation.RequestReview
            && (command.RequestActiveTarget || command.RequestTargetName is not null))
        {
            using var resolution = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
            try
            {
                var resolved = await ResolveReadableAsync(command, Live, token).ConfigureAwait(false);
                command = command with
                { SessionId = resolved.Authority.SessionId.Value, Generation = resolved.Authority.Generation.Value,
                    MetadataRevision = resolved.Metadata?.Revision.Value ?? 0 };
                resolution.Complete(HostOperationOutcome.Completed);
            }
            catch { resolution.Complete(HostOperationOutcome.Failed); throw; }
        }
        var session = new HostId<SessionIdentity>(command.SessionId
            ?? throw new InvalidOperationException("An exact or unambiguously host-resolved conversation is required."));
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), session, new(Guid.NewGuid()), origin),
            HostActivityLayer.Application, HostOperation.Request);
        try
        {
            token.ThrowIfCancellationRequested();
            if (!Live()) { throw new InvalidOperationException("Original private input admission changed; initiate a fresh review."); }
            if (command.Operation == SessionCommandOperation.RequestReview)
            {
                if (!Enum.IsDefined(command.RequestProvider))
                { throw new InvalidOperationException("An exact supported request provider is required."); }
                var body = command.RequestBody ?? throw new InvalidOperationException("An original request body is required.");
                ConversationText.Validate(body, submission: true);
                var target = await workspace.ReadExactMetadataAsync(session, token).ConfigureAwait(false);
                if (target.Authority.SessionId != session || !target.Authority.IsActive
                    || target.Authority.Generation.Value != command.Generation
                    || (target.Metadata?.Revision.Value ?? 0) != command.MetadataRevision || !Live())
                { throw new InvalidOperationException("Review requires the exact current Active session, generation and metadata revision."); }
                var review = new SessionRequestReview(Guid.NewGuid(), target, body, origin, time.GetUtcNow().AddSeconds(60),
                    command.RequestProvider);
                lock (gate)
                {
                    if (epoch != attempt || !Live()) { throw new InvalidOperationException("Review admission changed."); }
                    pending = new(review, Live, control, new(activity.Activity!.Context));
                }
                activity.Complete(HostOperationOutcome.Completed);
                return new("review", "Review the exact original body, target, provider, origin and expiry. Confirm separately; no content is retained or dispatched yet. Copilot and Claude additionally require separate native approval of each final physical send.")
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
                receipt = previous.Review.Provider == SessionRequestReview.ProviderChoice.Claude
                    ? await conversation.SubmitReviewedClaudeAsync(previous.Review.Target, previous.Review.Body, OriginalLive,
                        Revalidate, [previous.Cause], previous.Review.Origin, token).ConfigureAwait(false)
                    : previous.Review.Provider == SessionRequestReview.ProviderChoice.Copilot
                    ? await conversation.SubmitReviewedCopilotAsync(previous.Review.Target, previous.Review.Body, OriginalLive,
                        Revalidate, [previous.Cause], previous.Review.Origin, token).ConfigureAwait(false)
                    : await conversation.SubmitAsync(previous.Review.Target, previous.Review.Body, OriginalLive, token,
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

    private async Task<SessionWorkspaceEntry> ResolveReadableAsync(SessionCommand command, Func<bool> live, CancellationToken token)
    {
        if (!live() || command.RequestActiveTarget && command.RequestTargetName is not null)
        { throw new InvalidOperationException("One explicit original conversation selector is required."); }
        Guid? after = null;
        var cursors = new HashSet<Guid>();
        var subjects = new HashSet<Guid>();
        SessionWorkspaceEntry? match = null;
        for (var pageNumber = 0; pageNumber < 16; pageNumber++)
        {
            var page = await workspace.ReadMetadataAsync(after, SessionPage<SessionWorkspaceEntry>.MaximumRecords, token).ConfigureAwait(false);
            if (!live()) { throw new InvalidOperationException("Original conversation selection admission changed."); }
            if (page.Records.IsDefault || page.Records.Length > SessionPage<SessionWorkspaceEntry>.MaximumRecords)
            { throw new InvalidDataException("Conversation inventory page is incomplete or oversized."); }
            foreach (var entry in page.Records)
            {
                if (!subjects.Add(entry.Authority.SessionId.Value))
                { throw new InvalidDataException("Conversation inventory repeated a subject."); }
                if (entry.Authority.IsActive && (command.RequestActiveTarget
                    || string.Equals(entry.Metadata?.Name.Value, command.RequestTargetName!.Value, StringComparison.Ordinal)))
                {
                    if (match is not null) { throw new InvalidOperationException("Conversation selection is ambiguous. Choose an exact unique readable name in Sessions."); }
                    match = entry;
                }
            }
            if (page.Next is null)
            { return match ?? throw new InvalidOperationException("No matching current Active conversation is available. Refresh Sessions."); }
            if (!cursors.Add(page.Next.Value)) { throw new InvalidDataException("Conversation inventory cursor repeated."); }
            after = page.Next;
        }
        throw new InvalidOperationException("Conversation inventory exceeds the bounded complete resolution limit. Use native Sessions to choose the exact conversation.");
    }

    private sealed record Pending(SessionRequestReview Review, Func<bool> SourceAdmitted, long ControlRevision, ActivityLink Cause);
}
