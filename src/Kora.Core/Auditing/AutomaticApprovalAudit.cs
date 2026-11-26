using Kora.Core.Authorization;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.Auditing;

public sealed record AutomaticApprovalAudit
{
    public AutomaticApprovalAudit(HostRequest request, SessionPermissionPolicy policy, string actionId,
        HostOperationEffect effect, string promptCategory, string contentDigest,
        HostId<ProposalIdentity>? proposalId = null, HostRevision? proposalRevision = null,
        ExactOperationBinding? binding = null, string? destinationDigest = null,
        string? accountDigest = null, string? profileDigest = null, string? requestBodyDigest = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(policy);
        Request = request;
        Policy = policy;
        ActionId = InteractionValidation.Identifier(actionId);
        Effect = effect;
        PromptCategory = InteractionValidation.Identifier(promptCategory);
        ContentDigest = InteractionValidation.Digest(contentDigest);
        ProposalId = proposalId;
        ProposalRevision = proposalRevision;
        Binding = binding;
        DestinationDigest = destinationDigest is null ? null : InteractionValidation.Digest(destinationDigest);
        AccountDigest = accountDigest is null ? null : InteractionValidation.Digest(accountDigest);
        ProfileDigest = profileDigest is null ? null : InteractionValidation.Digest(profileDigest);
        RequestBodyDigest = requestBodyDigest is null ? null : InteractionValidation.Digest(requestBodyDigest);
        Validate();
    }

    public string Basis => "approve-all";
    public bool GrantsEvaluated => false;
    public HostRequest Request { get; }
    public SessionPermissionPolicy Policy { get; }
    public string ActionId { get; }
    public HostOperationEffect Effect { get; }
    public string PromptCategory { get; }
    public string ContentDigest { get; }
    public HostId<ProposalIdentity>? ProposalId { get; }
    public HostRevision? ProposalRevision { get; }
    public ExactOperationBinding? Binding { get; }
    public string? DestinationDigest { get; }
    public string? AccountDigest { get; }
    public string? ProfileDigest { get; }
    public string? RequestBodyDigest { get; }

    public void Validate()
    {
        if (Policy.SessionId != Request.SessionId || Policy.Level != SessionPermissionLevel.ApproveAll
            || Policy.Generation.Value <= 0 || Policy.Revision.Value <= 0
            || !Enum.IsDefined(Effect) || Effect is HostOperationEffect.Unknown or HostOperationEffect.Prohibited
            || (ProposalId is null) != (ProposalRevision is null)
            || ProposalRevision is { Value: <= 0 }
            || Binding is not null && !string.Equals(Binding.ActionId, ActionId, StringComparison.Ordinal)
            || RequestBodyDigest is not null && (DestinationDigest is null || AccountDigest is null || ProfileDigest is null))
        { throw new InvalidDataException("Automatic approval audit must bind a valid exact Approve all request and policy."); }
        ProposalId?.Validate();
    }
}
