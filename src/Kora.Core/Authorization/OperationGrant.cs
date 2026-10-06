using Kora.Core.Hosting;

namespace Kora.Core.Authorization;

// No expiry/retention field: Perpetual records belong to an independent retained partition.
public sealed record OperationGrant(
    HostId<ApprovalIdentity> Id,
    HostRevision Revision,
    HostOperationProposal ApprovedProposal,
    OperationGrantScope Scope,
    HostRevision SessionGeneration,
    RequestOrigin CreatorChannel,
    DateTimeOffset CreatedAt,
    OperationGrantStatus Status = OperationGrantStatus.Active,
    long UseCount = 0,
    string? RevocationReason = null,
    DateTimeOffset? LastUsedAt = null)
{
    public void Validate()
    {
        Id.Validate();
        if (ApprovedProposal is null || Revision.Value <= 0 || SessionGeneration.Value <= 0 || UseCount < 0
            || !Enum.IsDefined(Scope) || !Enum.IsDefined(Status)
            || CreatorChannel is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        {
            throw new InvalidDataException("Invalid persisted operation grant.");
        }
    }
}
