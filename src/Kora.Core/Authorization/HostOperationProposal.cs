using Kora.Core.Hosting;

namespace Kora.Core.Authorization;

public sealed record HostOperationProposal
{
    public HostOperationProposal(HostRequest request, HostId<ProposalIdentity> proposalId, HostRevision revision,
        ExactOperationBinding binding, HostOperationEffect effect, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(binding);
        proposalId.Validate();
        if (request.InvocationId is null || revision.Value <= 0 || !Enum.IsDefined(effect))
        {
            throw new InvalidDataException("An exact host invocation, proposal revision and known effect classification are required.");
        }
        Request = request;
        ProposalId = proposalId;
        Revision = revision;
        Binding = binding;
        Effect = effect;
        ExpiresAt = expiresAt;
    }

    public HostRequest Request { get; }
    public HostId<ProposalIdentity> ProposalId { get; }
    public HostRevision Revision { get; }
    public ExactOperationBinding Binding { get; }
    public HostOperationEffect Effect { get; }
    public DateTimeOffset ExpiresAt { get; }
}
