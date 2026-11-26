using Kora.Application.Infrastructure;
using Kora.Core.Hosting;
using Kora.Core.Authorization.SessionPermission;

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

    public AsyncCommand ReviewSpokenApprovalsCommand => reviewSpokenApprovals ??= new(async () =>
    {
        RequireNativeSpokenApprovalControl();
        spokenApprovalReview = (CallPolicyRevision, privacyObservation.Current.TopologyRevision,
            settingsOwnershipRevision(), !communicationPolicy.Current.Settings.AllowSpokenApprovalsDuringCalls);
        OnPropertyChanged(nameof(SpokenApprovalsStatus));
        if (CurrentPermissionControlTarget is { } target && sessionCommands is { SupportsSessionPermission: true } workspace)
        {
            SessionPermissionPolicy policy;
            using (Kora.Core.Diagnostics.HostActivity.HasScope ? null : Kora.Core.Diagnostics.HostActivity.BeginRoot(
                new(new(Guid.NewGuid()), target.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
                Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Storage))
            { policy = await workspace.ReadSessionPermissionAsync(target.Authority.SessionId, CancellationToken.None); }
            if (policy.Level == SessionPermissionLevel.ApproveAll)
            { await ConfirmSpokenApprovalsAsync((workspace, policy)); }
        }
    }, HandleCommandException);

    public AsyncCommand ConfirmSpokenApprovalsCommand => confirmSpokenApprovals ??= new(() => ConfirmSpokenApprovalsAsync(),
        HandleCommandException);

    private async Task ConfirmSpokenApprovalsAsync(
        (Kora.Application.Hosting.SessionWorkspaceService Workspace, SessionPermissionPolicy Policy)? automatic = null)
    {
        RequireNativeSpokenApprovalControl();
        var review = spokenApprovalReview
            ?? throw new InvalidOperationException("Review the exact device-local spoken-approval change first.");
        spokenApprovalReview = null;
        OnPropertyChanged(nameof(SpokenApprovalsStatus));
        if (review.CallRevision != CallPolicyRevision || review.PrivacyRevision != privacyObservation.Current.TopologyRevision
            || review.OwnershipRevision != settingsOwnershipRevision())
        { throw new InvalidOperationException("Call, privacy or ownership changed; start a fresh native spoken-approval review."); }
        var settings = communicationPolicy.Current.Settings with { AllowSpokenApprovalsDuringCalls = review.Desired };
        if (automatic is { } approval)
        {
            var digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(settings)));
            await approval.Workspace.RecordAutomaticControlApprovalAsync(approval.Policy, "call.spoken-approval.set", digest,
                "call-spoken-approval-control", () => CurrentPermissionControlTarget?.Authority.SessionId == approval.Policy.SessionId
                    && IsCallMutationHostEligible && review.CallRevision == CallPolicyRevision
                    && review.PrivacyRevision == privacyObservation.Current.TopologyRevision
                    && review.OwnershipRevision == settingsOwnershipRevision(), CancellationToken.None);
        }
        await SetCallSettingsAsync(settings, spokenApprovalDowngradeConfirmed: true, automaticPermission: automatic);
        OnPropertyChanged(nameof(SpokenApprovalsStatus));
    }

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
