using Kora.Core.Authorization;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.Interaction;

public sealed partial class HostAuthorizationService
{
    /// <summary>Independent consent/confirmation is resolved by the host before ordinary action policy.</summary>
    public async ValueTask<HostInteractionDecision> AuthorizeAsync(
        HostRequest request, bool independentConsentRequired, CancellationToken token)
    {
        SessionPermissionReviewRequest? review = null;
        var first = await transaction.RunAsync(request, "approval.session-policy",
            snapshot => Decide(snapshot, request, independentConsentRequired, null, out review), token).ConfigureAwait(false);
        if (review is null) { return first; }

        SessionPermissionReviewResult? result = null;
        var reason = "reviewer-unavailable-user-approval-required";
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15), time);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, budget.Token);
        try
        {
            result = await reviewer!.ReviewAsync(review, cancellation.Token).ConfigureAwait(false);
            reason = budget.IsCancellationRequested ? "reviewer-timeout-user-approval-required"
                : result is not null && result.IsValidFor(review)
                ? result.Verdict == SessionPermissionVerdict.Safe ? "model-safe" : "reviewer-requires-user-approval"
                : "reviewer-invalid-user-approval-required";
        }

        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { reason = "reviewer-timeout-user-approval-required"; }
        catch (Exception exception) when (exception is IOException or InvalidDataException or TimeoutException or InvalidOperationException)
        { reason = "reviewer-failed-user-approval-required"; }
        token.ThrowIfCancellationRequested();
        return await transaction.RunAsync(request, "approval.session-review-completed", snapshot =>
        {
            if (!Eligible(snapshot, request) || snapshot.Proposal != review.Proposal
                || snapshot.EffectivePermission != review.Policy
                || snapshot.Questions.Any(question => question.Proposal == review.Proposal)
                || snapshot.Admissions.Any(item => item.Proposal == review.Proposal))
            { return InteractionTransaction.Reject(snapshot, "review-retired", HostInteractionOutcome.Conflict); }
            if (string.Equals(reason, "model-safe", StringComparison.Ordinal))
            {
                return reviewer!.IsQualified
                    ? Admit(snapshot, SessionPermissionBasis.ModelSafe, HostInteractionOutcome.Approved, reason, reviewResult: result)
                    : PresentWithReason(snapshot, request, "reviewer-unavailable-user-approval-required");
            }
            return PresentWithReason(snapshot, request, reason);
        }, token).ConfigureAwait(false);
    }

    private (HostInteractionSnapshot, HostInteractionDecision) Decide(HostInteractionSnapshot snapshot,
        HostRequest request, bool independentConsentRequired, QuestionSpec? consentSpec, out SessionPermissionReviewRequest? review)
    {
        review = null;
        if (!Eligible(snapshot, request))
        { return InteractionTransaction.Reject(snapshot, "operation-not-admitted"); }
        var observed = ObserveContent(snapshot, request).Item1;
        var grant = FindApplicableGrant(observed, request);
        var mandatoryConfirmation = independentConsentRequired || observed.Proposal!.Effect == HostOperationEffect.VoiceOrCallSettings;
        if (grant is not null && !mandatoryConfirmation)
        { return Consume(observed, request, grant.Id, grant.Revision); }
        if (observed.Questions.Any(question => question.Proposal == observed.Proposal))
        { return InteractionTransaction.Reject(observed, "proposal-already-reviewed", HostInteractionOutcome.Conflict); }
        if (mandatoryConfirmation) { return Present(observed, request, consentSpec: consentSpec); }
        if (observed.EffectivePermission.Level == SessionPermissionLevel.ApproveAll)
        { return Admit(observed, SessionPermissionBasis.ApproveAll, HostInteractionOutcome.Approved, "approve-all"); }
        if (observed.EffectivePermission.Level == SessionPermissionLevel.ModelReview)
        {
            if (reviewer is not { IsQualified: true })
            { return PresentWithReason(observed, request, "reviewer-unavailable-user-approval-required"); }
            review = new(Guid.NewGuid(), observed.Proposal!, observed.EffectivePermission);
            return (observed, new(HostInteractionOutcome.Drafted, "separate-review-started"));
        }
        return Present(observed, request);
    }

    private OperationGrant? FindApplicableGrant(HostInteractionSnapshot snapshot, HostRequest request) =>
        snapshot.Grants.FirstOrDefault(item => Matches(snapshot, request, item));

    private bool Matches(HostInteractionSnapshot snapshot, HostRequest request, OperationGrant grant) =>
        grant.Status == OperationGrantStatus.Active && grant.ApprovedProposal.Binding == snapshot.Proposal!.Binding
        && grant.ApprovedProposal.Effect == snapshot.Proposal.Effect
        && (grant.Scope != OperationGrantScope.Once || grant.ApprovedProposal == snapshot.Proposal)
        && (grant.Scope != OperationGrantScope.Session
            || grant.ApprovedProposal.Request.SessionId == request.SessionId && grant.SessionGeneration == snapshot.Session.Generation);

    private (HostInteractionSnapshot, HostInteractionDecision) PresentWithReason(
        HostInteractionSnapshot snapshot, HostRequest request, string reason)
    {
        var (next, decision) = Present(snapshot, request);
        return (next, decision with { Reason = reason });
    }

    private static (HostInteractionSnapshot, HostInteractionDecision) Admit(
        HostInteractionSnapshot snapshot, SessionPermissionBasis basis, HostInteractionOutcome outcome, string reason,
        OperationGrant? grant = null, SessionPermissionReviewResult? reviewResult = null)
    {
        if (snapshot.Admissions.Any(item => item.Proposal == snapshot.Proposal))
        { return InteractionTransaction.Reject(snapshot, "invocation-already-admitted", HostInteractionOutcome.Conflict); }
        var receipt = new SessionPermissionAdmission(Guid.NewGuid(), snapshot.Proposal!, snapshot.EffectivePermission,
            basis, grant?.Id, grant?.Revision, Review: reviewResult);
        return (snapshot with { Admissions = snapshot.Admissions.Add(receipt) },
            new(outcome, reason, Grant: grant, Admission: receipt));
    }

    /// <summary>Atomically commits one dispatch use; repeated validation cannot mint another invocation.</summary>
    public ValueTask<HostInteractionDecision> DispatchAsync(
        HostRequest request, SessionPermissionAdmission receipt, CancellationToken token) =>
        transaction.RunAsync(request, "approval.session-dispatch", snapshot =>
        {
            var index = snapshot.Admissions.IndexOf(receipt);
            if (index < 0 || receipt.Dispatched || !AdmissionCurrent(snapshot, request, receipt))
            { return InteractionTransaction.Reject(snapshot, "admission-retired", HostInteractionOutcome.Conflict); }
            var dispatched = receipt with { Dispatched = true };
            return (snapshot with { Admissions = snapshot.Admissions.SetItem(index, dispatched) },
                new(HostInteractionOutcome.Consumed, "single-invocation-dispatch-committed", Admission: dispatched));
        }, token);

    public ValueTask<HostInteractionDecision> ValidateAdmissionAsync(
        HostRequest request, SessionPermissionAdmission receipt, CancellationToken token) =>
        transaction.RunAsync(request, "approval.session-revalidate", snapshot =>
            snapshot.Admissions.Contains(receipt) && AdmissionCurrent(snapshot, request, receipt)
                ? (snapshot, new(HostInteractionOutcome.Approved, "single-invocation-admission-current", Admission: receipt))
                : InteractionTransaction.Reject(snapshot, "admission-retired"), token);

    private bool AdmissionCurrent(HostInteractionSnapshot snapshot, HostRequest request, SessionPermissionAdmission receipt) =>
        Eligible(snapshot, request) && snapshot.Proposal == receipt.Proposal && snapshot.EffectivePermission == receipt.Policy
        && (receipt.GrantId is null || snapshot.Grants.Any(grant =>
            grant.Id == receipt.GrantId && grant.Revision == receipt.GrantRevision
            && grant.Status is OperationGrantStatus.Active or OperationGrantStatus.Consumed));
}
