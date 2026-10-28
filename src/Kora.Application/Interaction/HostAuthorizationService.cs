using Kora.Core.Authorization;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Network;
using Kora.Core.Storage;

namespace Kora.Application.Interaction;

public sealed partial class HostAuthorizationService(
    IHostInteractionStore store,
    TimeProvider time,
    IPreapprovedUriConfiguration? preapprovedUris = null,
    Kora.Core.Authorization.SessionPermission.ISessionPermissionReviewer? reviewer = null)
{
    private readonly InteractionTransaction transaction = new(store);
    private static readonly QuestionSpec ApprovalSpec = new("Approve this exact host-resolved operation?",
        QuestionKind.SingleChoice, [new("once", "Once"), new("session", "This session"), new("perpetual", "Always")],
        purpose: "authorization", sourceId: "host-operation");

    public async ValueTask<ExactGrantBatchDecision> RevokeExactBatchAsync(HostRequest request, HostRevision controlGeneration,
        ExactGrantBatch preview, Func<bool> admitted, CancellationToken cancellationToken)
    {
        preview.Validate();
        if (store is not IExactGrantStore exact || request.Origin != RequestOrigin.LocalUi
            || !ReferenceEquals(HostActivity.RequireCurrent().Request, request))
        {
            throw new InvalidOperationException("Exact batch revocation requires fresh original native user intent.");
        }
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
            "approval.revoke-exact-batch", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser,
            request.TaskId.Value.ToString("D"));
        using var activity = HostActivity.BeginAudit(request, requested);
        try
        {
            var result = await exact.RevokeExactGrantBatchAsync(request, controlGeneration, preview, requested,
                admitted, cancellationToken).ConfigureAwait(false);
            activity.Complete(result.Outcome == HostInteractionOutcome.Revoked
                ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            return result;
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

    public async ValueTask<HostInteractionDecision> RevokeExactAsync(HostRequest request, HostRevision controlGeneration,
        ExactGrantInspection preview, Func<bool> admitted, CancellationToken cancellationToken)
    {
        if (store is not IExactGrantStore exact || request.Origin != RequestOrigin.LocalUi
            || !ReferenceEquals(HostActivity.RequireCurrent().Request, request))
        {
            throw new InvalidOperationException("Exact revocation requires fresh host-resolved original native user intent.");
        }
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
            "approval.revoke-exact", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser,
            request.TaskId.Value.ToString("D"), preview.Grant.Id.Value);
        using var activity = HostActivity.BeginAudit(request, requested);
        try
        {
            var result = await exact.RevokeExactGrantAsync(request, controlGeneration, preview, requested,
                admitted, cancellationToken).ConfigureAwait(false);
            activity.Complete(result.Outcome == HostInteractionOutcome.Revoked
                ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            return result;
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

    public ValueTask<HostInteractionDecision> PresentAsync(HostRequest request, CancellationToken cancellationToken) =>
        transaction.RunAsync(request, "approval.present", snapshot => Present(snapshot, request), cancellationToken);

    /// <summary>Requires a user grant, without automatic session-policy or model-review approval.</summary>
    public ValueTask<HostInteractionDecision> RequestGrantAsync(
        HostRequest request, QuestionSpec spec, CancellationToken cancellationToken) =>
        transaction.RunAsync(request, "approval.tool-grant", snapshot =>
        {
            if (!Eligible(snapshot, request))
            { return InteractionTransaction.Reject(snapshot, "operation-not-admitted"); }
            var observed = ObserveContent(snapshot, request).Item1;
            var grant = observed.Grants.FirstOrDefault(item => Matches(observed, request, item));
            return grant is null ? Present(observed, request, consentSpec: spec)
                : Consume(observed, request, grant.Id, grant.Revision);
        }, cancellationToken);

    public ValueTask<HostInteractionDecision> PresentMcpOnceAsync(HostRequest request, Mcp.McpReview review,
        CancellationToken cancellationToken) => transaction.RunAsync(request, "approval.mcp-once", snapshot =>
    {
        if (!Eligible(snapshot, request) || !review.IsBoundTo(snapshot.Proposal!)
            || snapshot.Questions.Any(question => question.Proposal == snapshot.Proposal))
        { return InteractionTransaction.Reject(snapshot, "exact-mcp-proposal-not-current"); }
        var spec = new QuestionSpec("Review the complete exact HTTPS MCP " + review.Stage
            + " record, including destination, delegated account, schema and any outgoing arguments. "
            + "Choose a duration for this exact mapping, account, schema and arguments only.",
            QuestionKind.SingleChoice, [new("once", "Once"), new("session", "This session"), new("perpetual", "Always")],
            purpose: "authorization", sourceId: "mcp-https-read-only");
        return RequestExternalGrant(snapshot, request, spec);
    }, cancellationToken);

    public ValueTask<HostInteractionDecision> PresentWebPageOnceAsync(
        HostRequest request, Uri address, CancellationToken cancellationToken) =>
        transaction.RunAsync(request, "approval.web-page-once", snapshot =>
        {
            if (!Eligible(snapshot, request) || !WebPageAccessBinding.Matches(snapshot.Proposal!, address)
                || snapshot.Questions.Any(question => question.Proposal == snapshot.Proposal))
            { return InteractionTransaction.Reject(snapshot, "exact-page-proposal-not-current"); }
            var spec = new QuestionSpec(
                "Approve retrieval of this exact public page for the original local model request? "
                + "Other destinations and redirects are not approved. Page text stays local.\n" + address.AbsoluteUri,
                QuestionKind.SingleChoice, [new("once", "Once"), new("session", "This session"), new("perpetual", "Always")],
                purpose: "authorization", sourceId: "local-web-page");
            return RequestExternalGrant(snapshot, request, spec);
        }, cancellationToken);

    private (HostInteractionSnapshot, HostInteractionDecision) RequestExternalGrant(
        HostInteractionSnapshot snapshot, HostRequest request, QuestionSpec spec)
    {
        var observed = ObserveContent(snapshot, request).Item1;
        var grant = observed.Grants.FirstOrDefault(item => Matches(observed, request, item));
        return grant is null ? Present(observed, request, consentSpec: spec)
            : Consume(observed, request, grant.Id, grant.Revision);
    }

    public ValueTask<HostInteractionDecision> RequestWebPageAccessAsync(
        HostRequest request,
        Uri address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        return transaction.RunAsync(
            request,
            "approval.web-page-access",
            snapshot => RequestWebPageAccess(snapshot, request, address),
            cancellationToken);
    }

    private (HostInteractionSnapshot, HostInteractionDecision) RequestWebPageAccess(
        HostInteractionSnapshot snapshot,
        HostRequest request,
        Uri address)
    {
        if (!Eligible(snapshot, request))
        {
            return InteractionTransaction.Reject(snapshot, "web-destination-not-admitted");
        }
        var proposal = snapshot.Proposal!;
        if (!WebPageAccessBinding.Matches(proposal, address))
        {
            return InteractionTransaction.Reject(snapshot, "web-destination-not-admitted");
        }
        if (preapprovedUris is null)
        {
            return InteractionTransaction.Reject(snapshot, "preapproved-uri-policy-unavailable");
        }
        if (preapprovedUris.IsPreapproved(address))
        {
            return (snapshot, new(HostInteractionOutcome.Approved, "preapproved-address"));
        }
        return Present(snapshot, request, eligibilityConfirmed: true);
    }

    private (HostInteractionSnapshot, HostInteractionDecision) Present(
        HostInteractionSnapshot snapshot,
        HostRequest request,
        bool eligibilityConfirmed = false, QuestionSpec? consentSpec = null)
    {
        if (!eligibilityConfirmed && !Eligible(snapshot, request))
        {
            return InteractionTransaction.Reject(snapshot, "operation-not-admitted");
        }
        var proposal = snapshot.Proposal!;
        if (snapshot.Questions.Any(q => q.Proposal == proposal))
        {
            return InteractionTransaction.Reject(snapshot, "duplicate-proposal", HostInteractionOutcome.Conflict);
        }
        var spec = consentSpec ?? (proposal.Effect == HostOperationEffect.VoiceOrCallSettings
            ? new QuestionSpec("Confirm this exact protected control operation. Session action policy cannot supply this confirmation.",
                QuestionKind.SingleChoice, [new("once", "Once")], purpose: "authorization", sourceId: "host-operation")
            : ApprovalSpec);
        var question = new HostQuestionRecord(new(request, new(Guid.NewGuid()), new(1)),
            spec, snapshot.Session.Generation, proposal.ExpiresAt, Proposal: proposal);
        return (snapshot with { Questions = snapshot.Questions.Add(question) },
            new(HostInteractionOutcome.Presented, "approval-presented", question));
    }

    public ValueTask<HostInteractionDecision> ApproveAsync(HostQuestionKey key, QuestionAnswer answer,
        RequestOrigin channel, CancellationToken cancellationToken) =>
        transaction.RunAsync(key.Request, "approval.confirm", snapshot => Approve(snapshot, key, answer, channel), cancellationToken);

    private (HostInteractionSnapshot, HostInteractionDecision) Approve(HostInteractionSnapshot snapshot,
        HostQuestionKey key, QuestionAnswer answer, RequestOrigin channel)
    {
        var index = InteractionTransaction.FindQuestion(snapshot, key);
        if (index < 0)
        {
            return InteractionTransaction.Reject(snapshot, "question-conflict", HostInteractionOutcome.Conflict);
        }
        var question = snapshot.Questions[index];
        if (question.Status != QuestionStatus.Pending || question.SessionGeneration != snapshot.Session.Generation)
        {
            return InteractionTransaction.Reject(snapshot, "question-closed", HostInteractionOutcome.Conflict);
        }
        if (question.ExpiresAt <= time.GetUtcNow())
        {
            return HostQuestionService.Replace(snapshot, question with { Key = key.Next(), Status = QuestionStatus.Expired },
                HostInteractionOutcome.Expired, "question-expired");
        }
        if (!snapshot.Policy.AllowsApprovalChannel(channel))
        { return InteractionTransaction.Reject(snapshot, "approval-channel-unavailable-use-native-ui"); }
        if (!Eligible(snapshot, key.Request) || question.Proposal is null || question.Proposal != snapshot.Proposal
            || !question.Spec.Accepts(answer, submitting: true))
        {
            return InteractionTransaction.Reject(snapshot, "approval-not-applicable");
        }
        var scope = answer.Choices[0] switch
        {
            "once" => OperationGrantScope.Once,
            "session" => OperationGrantScope.Session,
            "perpetual" => OperationGrantScope.Perpetual,
            _ => throw new InvalidDataException("The host approval schema is invalid."),
        };
        var grant = new OperationGrant(new(Guid.NewGuid()), new(1), question.Proposal, scope,
            snapshot.Session.Generation, channel, time.GetUtcNow());
        var answered = question with { Key = key.Next(), Status = QuestionStatus.Answered, Draft = answer, AnswerChannel = channel };
        return (snapshot with { Questions = snapshot.Questions.SetItem(index, answered), Grants = snapshot.Grants.Add(grant) },
            new(HostInteractionOutcome.Approved, "exact-approval-committed", answered, grant));
    }

    public ValueTask<HostInteractionDecision> ConsumeAsync(HostRequest request, HostId<ApprovalIdentity> grantId,
        HostRevision expectedRevision, CancellationToken cancellationToken) =>
        transaction.RunAsync(request, "approval.consume", snapshot => Consume(snapshot, request, grantId, expectedRevision), cancellationToken);

    private (HostInteractionSnapshot, HostInteractionDecision) Consume(HostInteractionSnapshot snapshot,
        HostRequest request, HostId<ApprovalIdentity> grantId, HostRevision expectedRevision)
    {
        var index = snapshot.Grants.FindIndex(grant => grant.Id == grantId);
        if (index < 0)
        {
            return InteractionTransaction.Reject(snapshot, "grant-not-found");
        }
        var grant = snapshot.Grants[index];
        if (grant.Status != OperationGrantStatus.Active || grant.Revision != expectedRevision)
        {
            return InteractionTransaction.Reject(snapshot, "grant-conflict", HostInteractionOutcome.Conflict);
        }
        var proposal = snapshot.Proposal;
        if (proposal is not null && grant.ApprovedProposal.Binding.HasObservedContentChange(proposal.Binding))
        {
            return Revoke(snapshot, index, grant, "observed-content-change");
        }
        if (grant.Scope is OperationGrantScope.Once or OperationGrantScope.Session
            && grant.ApprovedProposal.Request.SessionId == snapshot.Session.SessionId
            && (!snapshot.Session.IsActive || grant.SessionGeneration != snapshot.Session.Generation))
        {
            return Revoke(snapshot, index, grant, "work-session-ended");
        }
        if (!Eligible(snapshot, request) || grant.ApprovedProposal.Binding != proposal!.Binding
            || grant.ApprovedProposal.Effect != proposal.Effect
            || (grant.Scope == OperationGrantScope.Once && grant.ApprovedProposal != proposal)
            || (grant.Scope == OperationGrantScope.Session && grant.ApprovedProposal.Request.SessionId != request.SessionId)
            || (proposal.Effect == HostOperationEffect.VoiceOrCallSettings && grant.Scope != OperationGrantScope.Once))
        {
            return InteractionTransaction.Reject(snapshot, "grant-not-applicable");
        }
        if (snapshot.Admissions.Any(item => item.Proposal == snapshot.Proposal))
        { return InteractionTransaction.Reject(snapshot, "invocation-already-admitted", HostInteractionOutcome.Conflict); }
        var consumed = grant with
        {
            Revision = new(checked(grant.Revision.Value + 1)),
            UseCount = checked(grant.UseCount + 1),
            Status = grant.Scope == OperationGrantScope.Once ? OperationGrantStatus.Consumed : OperationGrantStatus.Active,
            LastUsedAt = time.GetUtcNow(),
        };
        var next = snapshot with { Grants = snapshot.Grants.SetItem(index, consumed) };
        return Admit(next, Kora.Core.Authorization.SessionPermission.SessionPermissionBasis.ExistingGrant,
            HostInteractionOutcome.Consumed, "grant-use-committed", consumed);
    }

    public ValueTask<HostInteractionDecision> ObserveContentAsync(HostRequest request, CancellationToken cancellationToken) =>
        transaction.RunAsync(request, "approval.content-change", snapshot => ObserveContent(snapshot, request), cancellationToken);

    private static (HostInteractionSnapshot, HostInteractionDecision) ObserveContent(HostInteractionSnapshot snapshot, HostRequest request)
    {
        if (snapshot.Proposal is not { } proposal || proposal.Request != request)
        {
            return InteractionTransaction.Reject(snapshot, "operation-not-admitted");
        }
        var grants = snapshot.Grants;
        for (var index = 0; index < grants.Length; index++)
        {
            var grant = grants[index];
            if (grant.Status == OperationGrantStatus.Active
                && grant.ApprovedProposal.Binding.HasObservedContentChange(proposal.Binding))
            {
                grants = grants.SetItem(index, grant with
                {
                    Revision = new(checked(grant.Revision.Value + 1)),
                    Status = OperationGrantStatus.Revoked,
                    RevocationReason = "observed-content-change"
                });
            }
        }
        return (snapshot with { Grants = grants }, new(HostInteractionOutcome.Revoked, "content-observation-committed"));
    }

    // Editing removes old authority; replacement needs a fresh reviewed proposal/confirmation, never an in-place widening.
    public ValueTask<HostInteractionDecision> RevokeAsync(HostRequest request, HostId<ApprovalIdentity> grantId,
        HostRevision expectedRevision, CancellationToken cancellationToken) =>
        transaction.RunAsync(request, "approval.revoke", snapshot => Remove(snapshot, grantId, expectedRevision), cancellationToken);

    private static (HostInteractionSnapshot, HostInteractionDecision) Remove(HostInteractionSnapshot snapshot,
        HostId<ApprovalIdentity> grantId, HostRevision expectedRevision)
    {
        var index = snapshot.Grants.FindIndex(grant => grant.Id == grantId);
        if (!InteractionTransaction.CanInteract(snapshot) || index < 0)
        {
            return InteractionTransaction.Reject(snapshot, "grant-not-found");
        }
        var grant = snapshot.Grants[index];
        if (grant.Revision != expectedRevision || grant.Status == OperationGrantStatus.Revoked)
        {
            return InteractionTransaction.Reject(snapshot, "grant-conflict", HostInteractionOutcome.Conflict);
        }
        return Revoke(snapshot, index, grant, "explicit-remove-or-edit");
    }

    private bool Eligible(HostInteractionSnapshot snapshot, HostRequest request) =>
        InteractionTransaction.CanInteract(snapshot) && snapshot.Policy.OtherMandatoryGatesSatisfied
        && snapshot.Proposal is { } proposal && proposal.Request == request && proposal.ExpiresAt > time.GetUtcNow()
        && proposal.Effect is not (HostOperationEffect.Unknown or HostOperationEffect.Prohibited)
        && (proposal.Effect != HostOperationEffect.VoiceOrCallSettings
            || snapshot.Policy.AllowsVoiceOrCallSettings(request.Origin));

    private static (HostInteractionSnapshot, HostInteractionDecision) Revoke(
        HostInteractionSnapshot snapshot, int index, OperationGrant grant, string reason)
    {
        var revoked = grant with
        {
            Revision = new(checked(grant.Revision.Value + 1)),
            Status = OperationGrantStatus.Revoked,
            RevocationReason = reason
        };
        return (snapshot with { Grants = snapshot.Grants.SetItem(index, revoked) },
            new(HostInteractionOutcome.Revoked, reason, Grant: revoked));
    }
}