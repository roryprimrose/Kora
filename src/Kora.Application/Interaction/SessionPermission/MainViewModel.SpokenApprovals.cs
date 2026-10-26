using Kora.Application.Infrastructure;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private (long CallRevision, long PrivacyRevision, long OwnershipRevision, bool Desired)? spokenApprovalReview;
    private AsyncCommand? reviewSpokenApprovals;
    private AsyncCommand? confirmSpokenApprovals;
    private AsyncCommand? cancelSpokenApprovals;

    public string SpokenApprovalsStatus => spokenApprovalReview is { } review
        ? review.Desired
            ? "Review: enable spoken approvals during protected calls on this device. Deliberate spoken Once, Session and Always replies may grant authority. Confirm this downgrade explicitly; mandatory authentication and privacy remain."
            : "Review: disable spoken approvals during protected calls. Exact grants and session permission levels will not change."
        : communicationPolicy.Current.Settings.AllowSpokenApprovalsDuringCalls
            ? "Spoken approvals during calls: On. Exact-target readback and mandatory authentication still apply."
            : "Spoken approvals during calls: Off. During a protected call, use native UI approval. Grants and session levels are unchanged.";

    public AsyncCommand ReviewSpokenApprovalsCommand => reviewSpokenApprovals ??= new(() =>
    {
        RequireNativeSpokenApprovalControl();
        spokenApprovalReview = (CallPolicyRevision, privacyObservation.Current.TopologyRevision,
            settingsOwnershipRevision(), !communicationPolicy.Current.Settings.AllowSpokenApprovalsDuringCalls);
        OnPropertyChanged(nameof(SpokenApprovalsStatus));
        return Task.CompletedTask;
    }, HandleCommandException);

    public AsyncCommand ConfirmSpokenApprovalsCommand => confirmSpokenApprovals ??= new(async () =>
    {
        RequireNativeSpokenApprovalControl();
        var review = spokenApprovalReview
            ?? throw new InvalidOperationException("Review the exact device-local spoken-approval change first.");
        spokenApprovalReview = null;
        OnPropertyChanged(nameof(SpokenApprovalsStatus));
        if (review.CallRevision != CallPolicyRevision || review.PrivacyRevision != privacyObservation.Current.TopologyRevision
            || review.OwnershipRevision != settingsOwnershipRevision())
        { throw new InvalidOperationException("Call, privacy or ownership changed; start a fresh native spoken-approval review."); }
        await SetCallSettingsAsync(communicationPolicy.Current.Settings with { AllowSpokenApprovalsDuringCalls = review.Desired },
            spokenApprovalDowngradeConfirmed: true);
        OnPropertyChanged(nameof(SpokenApprovalsStatus));
    }, HandleCommandException);

    public AsyncCommand CancelSpokenApprovalsCommand => cancelSpokenApprovals ??= new(() =>
    {
        CancelSpokenApprovalReview();
        return Task.CompletedTask;
    }, HandleCommandException);

    public void CancelSpokenApprovalReview()
    {
        spokenApprovalReview = null;
        OnPropertyChanged(nameof(SpokenApprovalsStatus));
    }

    private void RequireNativeSpokenApprovalControl()
    {
        if (!IsCallMutationHostEligible || OriginalOrigin() != RequestOrigin.LocalUi
            || Kora.Core.Diagnostics.HostActivity.HasScope && Kora.Core.Diagnostics.HostActivity.Current is null
            || Kora.Core.Diagnostics.HostActivity.Current?.Request.InvocationId is not null)
        { throw new InvalidOperationException("A fresh original native settings control is required; voice/model origin cannot be relabeled."); }
    }
}
