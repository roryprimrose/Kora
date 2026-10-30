using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    private ISessionRetentionApplyStore RetentionApplications => store as ISessionRetentionApplyStore
        ?? throw new InvalidOperationException("Exact retention application is unavailable.");

    internal Task<SessionRetentionApplyReview> ReviewRetentionApplyAsync(HostId<SessionIdentity> session,
        CancellationToken token) =>
        ReadAsync(() => RetentionApplications.ReviewRetentionApplyAsync(session, token), token, retentionStatusOnly: true);

    internal async Task<SessionRetentionApplyReceipt> ApplyReviewedRetentionAsync(SessionRetentionApplyReview review,
        Func<bool> admitted, CancellationToken token)
    {
        RequireNativeFileInput();
        token.ThrowIfCancellationRequested();
        if (!admitted()) { throw new InvalidOperationException("Original retention application source is unavailable."); }
        await RetentionApplications.ValidateRetentionApplyAsync(review, token).ConfigureAwait(false);
        if (review.Effect != SessionRetentionApplyEffect.DeadlinesOnly && attachmentLifecycle is not null)
        { await attachmentLifecycle.RevokeForDisposition(review.Before.State.SessionId).ConfigureAwait(false); }
        var receipt = await ControlAsync(review.Before.State.SessionId, RequestOrigin.LocalUi,
            (request, eligible) => RetentionApplications.ApplyReviewedRetentionAsync(request, review, eligible, token),
            token, admitted, existingSubject: true, terminalCommitted: true).ConfigureAwait(false);
        InvalidateWorkOverview(receipt.Retention.State.SessionId);
        if (receipt.Effect == SessionRetentionApplyEffect.Delete)
        { SessionRetired?.Invoke(receipt.Retention.State.SessionId); }
        else if (receipt.Effect == SessionRetentionApplyEffect.Archive)
        { SessionLifecycleChanged?.Invoke(receipt.Retention.State.SessionId); }
        if (receipt.Effect != SessionRetentionApplyEffect.DeadlinesOnly && localEvents is not null)
        { await localEvents.RetireSessionAsync(receipt.Retention.State.SessionId, CancellationToken.None).ConfigureAwait(false); }
        return receipt;
    }
}
