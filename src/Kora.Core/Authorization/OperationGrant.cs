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
            || CreatorChannel is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || RevocationReason is { } reason && (reason.Length is < 1 or > 128
                || reason.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-'))))
        {
            throw new InvalidDataException("Invalid persisted operation grant.");
        }

    }

    public OperationGrant Revoke(string reason)
    {
        Validate();
        if (Status != OperationGrantStatus.Active)
        {
            throw new InvalidOperationException("Only active exact authority can be revoked; terminal records cannot be restored.");
        }
        var revoked = this with
        {
            Revision = new(checked(Revision.Value + 1)),
            Status = OperationGrantStatus.Revoked,
            RevocationReason = reason,
        };
        revoked.Validate();
        return revoked;
    }
}
