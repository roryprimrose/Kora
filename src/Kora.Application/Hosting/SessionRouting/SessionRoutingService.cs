using System.Collections.Immutable;

using Kora.Core.Conversation;
using Kora.Core.Conversation.SessionRouting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting.SessionRouting;

/// <summary>One original request, bounded metadata proposal and explicit destination review. Never reads conversation bodies.</summary>
public sealed class SessionRoutingService(
    SessionWorkspaceService workspace, SessionConversationService conversation,
    ISessionWorkspaceAccess access, TimeProvider time, ISessionRelatednessMatcher? matcher = null)
{
    private readonly Lock gate = new();
    private long revision;
    private SessionRoutingReview? current;
    private SessionRoutingSelection? selected;
    private SessionPage<SessionWorkspaceEntry>? inventory;
    private Func<bool> source = static () => false;
    private long control;
    private bool consumed;

    public void Retire()
    {
        lock (gate)
        {
            revision++;
            current = null;
            selected = null;
            inventory = null;
            source = static () => false;
        }
    }

    public bool IsCurrent(SessionRoutingReview review)
    {
        lock (gate) { return ReferenceEquals(current, review) && !consumed && Live() && time.GetUtcNow() < review.ExpiresAt; }
    }

    private bool Live() => access.CanInspect && access.CanControl
        && access.ControlRevision == control && source();

    public async Task<SessionRoutingReview> PrepareAsync(string body, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token)
    {
        SessionConversationService.RequireOriginalSource(origin);
        ConversationText.Validate(body, submission: true);
        long attempt;
        lock (gate)
        {
            Retire();
            consumed = false;
            source = admitted;
            control = access.ControlRevision;
            attempt = revision;
        }
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, origin);
        try
        {
            Require(attempt, token);
            var page = await ReadInventory(token).ConfigureAwait(false);
            Require(attempt, token);
            var snapshot = new SessionRouteSnapshot(attempt, [.. page.Records
                .Where(entry => entry.Authority.IsActive).Select(entry => new SessionRouteDescriptor(entry, Guid.NewGuid()))]);
            var proposal = matcher is null
                ? new(snapshot.Revision, SessionRouteMatch.Unavailable, [])
                : await matcher.MatchAsync(body, snapshot, token).ConfigureAwait(false);
            Require(attempt, token);
            var candidates = ValidateProposal(snapshot, proposal);
            var actual = await ReadInventory(token).ConfigureAwait(false);
            Require(attempt, token);
            if (!Same(page, actual))
            { throw new InvalidOperationException("Routing metadata changed during matching. No New fallback or submission was attempted."); }
            var review = new SessionRoutingReview(body, origin, snapshot, proposal.Match, candidates, time.GetUtcNow().AddSeconds(60),
                HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Request, origin));
            lock (gate)
            {
                Require(attempt, token);
                inventory = page;
                current = review;
            }
            activity.Complete(HostOperationOutcome.Completed);
            return review;
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    public async Task<SessionRoutingSelection> SelectAsync(SessionRoutingReview review,
        HostId<SessionIdentity>? destination, CancellationToken token)
    {
        using var activity = review.Continue();
        try
        {
            lock (gate)
            {
                if (ReferenceEquals(current, review)) { selected = null; }
            }
            await ValidateReview(review, token).ConfigureAwait(false);
            SessionWorkspaceEntry? target = null;
            if (destination is { } exact)
            {
                target = review.Snapshot.Descriptors.SingleOrDefault(item => item.Entry.Authority.SessionId == exact)?.Entry
                    ?? throw new InvalidOperationException("Correction requires an exact presented Active destination or New.");
                await ValidateTarget(target, review, token).ConfigureAwait(false);
            }
            lock (gate)
            {
                if (!IsCurrent(review)) { throw new InvalidOperationException("Original routing review expired or changed."); }
                selected = new(review, target);
                activity.Complete(HostOperationOutcome.Completed);
                return selected;
            }
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    public async Task<SessionRoutingReceipt> SubmitAsync(SessionRoutingSelection selection, CancellationToken token)
    {
        var review = selection.Review;
        long attempt;
        lock (gate)
        {
            if (!ReferenceEquals(selected, selection) || !IsCurrent(review))
            { throw new InvalidOperationException("The exact current destination acknowledgment is required. Nothing was submitted."); }
            consumed = true;
            attempt = revision;
        }
        bool OriginalLive()
        {
            lock (gate) { return ReferenceEquals(current, review) && revision == attempt && Live() && time.GetUtcNow() < review.ExpiresAt; }
        }
        using var activity = review.Continue();
        try
        {
            token.ThrowIfCancellationRequested();
            var page = await ReadInventory(token).ConfigureAwait(false);
            if (!OriginalLive() || inventory is null || !Same(inventory, page))
            { throw new InvalidOperationException("Reviewed routing metadata or original input changed. No fallback or retry."); }
            if (selection.Target is null)
            {
                selection.CreationRequest = HostRequest.Create(review.Origin);
                selection.Target = await workspace.CreateRoutingTargetAsync(selection.CreationRequest, OriginalLive, token).ConfigureAwait(false);
            }
            var target = selection.Target;
            async Task Revalidate(CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                var actual = await workspace.ReadExactMetadataAsync(target.Authority.SessionId, cancellation).ConfigureAwait(false);
                if (actual != target || !target.Authority.IsActive || !OriginalLive())
                { throw new InvalidOperationException("The acknowledged destination or original permission changed; inspect any committed creation. No rerouting."); }
            }
            await Revalidate(token).ConfigureAwait(false);
            var receipt = await conversation.SubmitAsync(target, review.Body, OriginalLive, token,
                Revalidate, origin: review.Origin).ConfigureAwait(false);
            activity.Complete(HostOperationOutcome.Completed);
            return new(target, receipt);
        }
        catch (OperationCanceledException exception)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            if (selection.CreationRequest is { } creation) { throw new SessionRoutingUnconfirmedException(creation, exception); }
            throw;
        }
        catch (Exception exception)
        {
            activity.Complete(HostOperationOutcome.Failed);
            if (selection.CreationRequest is { } creation) { throw new SessionRoutingUnconfirmedException(creation, exception); }
            throw;
        }
    }

    private void Require(long attempt, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (revision != attempt || !Live())
            { throw new InvalidOperationException("Original routing input, privacy, ownership or permission changed."); }
        }
    }

    private async Task ValidateReview(SessionRoutingReview review, CancellationToken token)
    {
        if (!IsCurrent(review)) { throw new InvalidOperationException("The original routing review is no longer available."); }
        var page = await ReadInventory(token).ConfigureAwait(false);
        if (!IsCurrent(review) || inventory is null || !Same(inventory, page))
        { throw new InvalidOperationException("The presented routing inventory changed. No target was selected."); }
    }

    private async Task ValidateTarget(SessionWorkspaceEntry target, SessionRoutingReview review, CancellationToken token)
    {
        var actual = await workspace.ReadExactMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false);
        if (actual != target || !target.Authority.IsActive || !IsCurrent(review))
        { throw new InvalidOperationException("The exact Active destination changed. No fallback was selected."); }
    }

    private async Task<SessionPage<SessionWorkspaceEntry>> ReadInventory(CancellationToken token)
    {
        var page = await workspace.ReadMetadataAsync(null, SessionPage<SessionWorkspaceEntry>.MaximumRecords, token).ConfigureAwait(false);
        if (page.Records.IsDefault || page.Records.Length > SessionPage<SessionWorkspaceEntry>.MaximumRecords
            || page.Records.Select(entry => entry.Authority.SessionId).Distinct().Count() != page.Records.Length
            || page.Records.Any(entry => entry.Authority.SessionId.Value == Guid.Empty || entry.Authority.Generation.Value < 1
                || entry.Metadata is { } metadata && (metadata.SessionId != entry.Authority.SessionId || metadata.Revision.Value < 1)))
        { throw new InvalidDataException("Routing requires a bounded, valid host metadata page with unique subjects."); }
        return page;
    }

    private static bool Same(SessionPage<SessionWorkspaceEntry> expected, SessionPage<SessionWorkspaceEntry> actual) =>
        expected.Next == actual.Next && expected.Records.SequenceEqual(actual.Records);

    private static ImmutableArray<SessionRouteDescriptor> ValidateProposal(SessionRouteSnapshot snapshot, SessionRouteProposal proposal)
    {
        if (proposal.ExpectedRevision != snapshot.Revision || !Enum.IsDefined(proposal.Match) || proposal.Candidates.IsDefault
            || proposal.Candidates.Length > snapshot.Descriptors.Length
            || proposal.Candidates.Distinct().Count() != proposal.Candidates.Length
            || proposal.Match == SessionRouteMatch.Clear && proposal.Candidates.Length != 1
            || proposal.Match == SessionRouteMatch.Ambiguous && proposal.Candidates.Length < 2
            || proposal.Match is SessionRouteMatch.NoMatch or SessionRouteMatch.Unavailable && !proposal.Candidates.IsEmpty)
        { throw new InvalidDataException("The routing proposal is malformed or bound to a stale registry revision."); }
        return [.. proposal.Candidates.Select(candidate => snapshot.Descriptors.SingleOrDefault(item => item.Candidate == candidate)
            ?? throw new InvalidDataException("Routing inference referenced unpresented, archived or mismatched metadata evidence."))];
    }
}
