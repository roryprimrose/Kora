using System.Globalization;

using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private SessionRetentionApplyReview? applyRetentionReview;
    public bool CanReviewRetentionApply => CanRefreshRetention && access.CanControl && retentionApply is not null
        && retentionReview is not { CommandOrigin: Kora.Core.Hosting.RequestOrigin.ActivatedVoice }
        && retentionObservation is { State.Perpetual: false, State.Purged: false, Removed: false, WorkHoldObserved: false }
        && RetentionSubjectMatches;
    public bool CanConfirmRetentionApply => CanReviewRetentionApply && applyRetentionReview is { } review
        && retentionApply!.IsPending(review);
    public string RetentionApplyPreview => applyRetentionReview is { } review
        ? DescribeRetentionApplication(review)
        : "Apply the confirmed current policy to ONE ordinary idle conversation. Preview first; separate native confirmation is mandatory. "
            + "No activity renewal, preference save, bulk cleanup, work cancellation or model/voice authority.";

    public Task ReviewRetentionApplyAsync() => RunAsync(async () =>
    {
        ClearRetentionApplication();
        if (retentionReview is { CommandOrigin: Kora.Core.Hosting.RequestOrigin.ActivatedVoice })
        { throw new InvalidOperationException("Original voice-origin retention cannot become apply-now authority. Start a fresh native metadata review."); }
        retentionReview = null;
        var expected = retentionObservation ?? throw new InvalidOperationException("Refresh the exact retention metadata first.");
        var epoch = selectionEpoch;
        var control = access.ControlRevision;
        var composer = Interlocked.Read(ref composerBufferRevision);
        bool Live() => !closed && access.CanControl && access.ControlRevision == control
            && selectionEpoch == epoch && RetentionSubjectMatches
            && Interlocked.Read(ref composerBufferRevision) == composer && !HasUnsentRetentionInput(expected.State.SessionId);
        if (!Live()) { throw new InvalidOperationException("Unsent input is held. Explicitly resolve it before a fresh apply-now review."); }
        var review = await (retentionApply ?? throw new InvalidOperationException("Apply-now is unavailable."))
            .ReviewAsync(expected, Live, lifetime.Token);
        if (!Live()) { retentionApply.Discard(review); throw new OperationCanceledException("Original native selection changed."); }
        applyRetentionReview = review;
        status = "Preview only. No policy, activity, work, history or preference changed. Confirm the exact disclosed action separately.";
    }, activitySession: retentionSubject);

    public void CancelRetentionApplication()
    {
        ClearRetentionApplication();
        NotifyRetention();
    }

    private void ClearRetentionApplication()
    {
        if (applyRetentionReview is { } review) { retentionApply?.Discard(review); }
        applyRetentionReview = null;
    }

    public Task ConfirmRetentionApplyAsync() => RunAsync(async () =>
    {
        var review = applyRetentionReview ?? throw new InvalidOperationException("Preview one exact retention application first.");
        applyRetentionReview = null;
        var receipt = await retentionApply!.ConfirmAsync(review,
            () => !closed && access.CanControl && RetentionSubjectMatches
                && !HasUnsentRetentionInput(review.Before.State.SessionId), lifetime.Token);
        if (receipt.Effect != SessionRetentionApplyEffect.DeadlinesOnly)
        {
            revokeHistoryDetails?.Invoke(receipt.Retention.State.SessionId);
            revokeConversationDetails?.Invoke(receipt.Retention.State.SessionId);
            sessions = null;
            ClearSelection();
        }
        else { retentionObservation = receipt.Retention; }
        status = "Confirmed policy/deadlines, audited request and terminal receipt, exact source readback and "
            + receipt.Effect + " completed for this conversation only. No meaningful activity renewed. "
            + "Independent Perpetual grants, audit, diagnostics and original files remain unchanged.";
    }, activitySession: retentionSubject);

    private bool HasUnsentRetentionInput(Kora.Core.Hosting.HostId<Kora.Core.Hosting.SessionIdentity> session) =>
        selected?.Authority.SessionId == session && ComposerDirty
        || unsent.TryGetValue(session, out var buffer) && (buffer.Body.Length != 0 || buffer.Held || buffer.SourceHeld)
        || selected?.Authority.SessionId == session && (conversationHeld || conversationSourceHeld);

    private static string DescribeRetentionApplication(SessionRetentionApplyReview review) =>
        "Exact conversation: " + review.Before.State.SessionId.Value.ToString("D")
        + "\nOriginal meaningful activity UTC: " + review.Before.State.LastMeaningfulActivity.ToString("O", CultureInfo.InvariantCulture)
        + "\nArchive due UTC: " + review.Before.State.ArchiveDue.ToString("O", CultureInfo.InvariantCulture)
        + " -> " + review.After.ArchiveDue.ToString("O", CultureInfo.InvariantCulture)
        + "\nDelete due UTC: " + review.Before.State.DeleteDue.ToString("O", CultureInfo.InvariantCulture)
        + " -> " + review.After.DeleteDue.ToString("O", CultureInfo.InvariantCulture)
        + "\nFixed cutoff UTC: " + review.Cutoff.ToString("O", CultureInfo.InvariantCulture)
        + " | expires UTC: " + review.ExpiresAt.ToString("O", CultureInfo.InvariantCulture)
        + "\nSeparate confirmation will execute: " + review.Effect
        + (review.Effect == SessionRetentionApplyEffect.Delete
            ? "\n" + SessionDispositionPreview.Scope
            : review.Effect == SessionRetentionApplyEffect.Archive
                ? "\nArchive to Done; end session-scoped grant eligibility and reviewed session-memory use, clear host observations. Retain conversation bodies, attachment, saved history and independent Perpetual grants."
                : "\nChange only this conversation's snapshotted policy/deadlines. No immediate archival/deletion. Existing guarded future maintenance remains applicable.")
        + "\nInventory: messages " + review.Inventory.ConversationMessages.ToString(CultureInfo.InvariantCulture)
        + ", saved legacy drafts " + review.Inventory.SavedConversationDrafts.ToString(CultureInfo.InvariantCulture)
        + ", attachments " + review.Inventory.Attachments.ToString(CultureInfo.InvariantCulture)
        + ", questions " + review.Inventory.Questions.ToString(CultureInfo.InvariantCulture)
        + ", scoped grants " + review.Inventory.ScopedGrants.ToString(CultureInfo.InvariantCulture)
        + ", observations " + review.Inventory.Observations.ToString(CultureInfo.InvariantCulture)
        + ", waits " + review.Inventory.Waits.ToString(CultureInfo.InvariantCulture)
        + ", provider overrides " + review.Inventory.ProviderProfiles.ToString(CultureInfo.InvariantCulture)
        + ". No live/queued/question/Unknown work or uncertain copy inventory may be confirmed.";
}
