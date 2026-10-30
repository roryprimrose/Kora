using Kora.Application.Configuration;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>One separate original-native decision for one confirmed-policy retention application.</summary>
public sealed class SessionRetentionApplyService(
    SessionWorkspaceService workspace, SessionConversationService conversation,
    SessionRetentionConfigurationService configuration, ISessionWorkspaceAccess access)
{
    private readonly Lock gate = new();
    private Pending? pending;
    private long epoch;

    private sealed record Pending(SessionRetentionApplyReview Review,
        SessionRetentionConfigurationObservation Policy, long Control, Func<bool> OriginalAdmission);

    public void Discard(SessionRetentionApplyReview review)
    {
        lock (gate)
        {
            if (pending?.Review != review) { return; }
            pending = null;
            epoch++;
        }
    }

    public bool IsPending(SessionRetentionApplyReview review)
    {
        lock (gate)
        { return ReferenceEquals(pending?.Review, review) && access.CanControl && access.ControlRevision == pending.Control; }
    }

    public async Task<SessionRetentionApplyReview> ReviewAsync(SessionRetentionObservation expected,
        Func<bool> admitted, CancellationToken token)
    {
        SessionWorkspaceService.RequireNativeFileInput();
        long attempt;
        lock (gate) { pending = null; attempt = ++epoch; }
        var control = access.ControlRevision;
        var policy = configuration.GetObservation();
        bool Live() => access.CanControl && access.ControlRevision == control && admitted()
            && configuration.IsCurrentObservation(policy);
        if (!Live()) { throw new InvalidOperationException("Apply-now requires confirmed policy and private native ownership."); }
        var review = await conversation.WithIdleRetentionAsync(expected.State.SessionId,
            () => workspace.ReviewRetentionApplyAsync(expected.State.SessionId, token), token).ConfigureAwait(false);
        if (!review.Before.MatchesRecordedState(expected) || review.Settings != policy.Effective
            || review.PolicyRevision != policy.PolicyRevision)
        { throw new InvalidOperationException("The selected conversation or confirmed policy changed. Refresh and review again."); }
        token.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (epoch != attempt || !Live())
            { throw new InvalidOperationException("The original native retention review changed or closed."); }
            pending = new(review, policy, control, admitted);
        }
        return review;
    }

    public async Task<SessionRetentionApplyReceipt> ConfirmAsync(SessionRetentionApplyReview review,
        Func<bool> admitted, CancellationToken token)
    {
        SessionWorkspaceService.RequireNativeFileInput();
        Pending captured;
        long attempt;
        lock (gate)
        {
            captured = pending is { } value && ReferenceEquals(value.Review, review) ? value
                : throw new InvalidOperationException("No matching single-use native apply-now review. Review again.");
            pending = null;
            attempt = ++epoch;
        }
        bool Live() => Interlocked.Read(ref epoch) == attempt && access.CanControl
            && access.ControlRevision == captured.Control && captured.OriginalAdmission() && admitted()
            && configuration.IsCurrentObservation(captured.Policy);
        token.ThrowIfCancellationRequested();
        if (!Live()) { throw new InvalidOperationException("Original policy, owner, call, privacy or native source changed."); }
        return await conversation.WithIdleRetentionAsync(review.Before.State.SessionId,
            () => workspace.ApplyReviewedRetentionAsync(review, Live, token), token).ConfigureAwait(false);
    }
}
