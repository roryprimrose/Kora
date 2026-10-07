using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

public sealed class AssistantNameProposal
{
    internal AssistantNameProposal(Guid owner, string value, long revision,
        long callRevision, HostRequest request, SecurityAuditInitiator initiator)
    {
        Owner = owner;
        Value = value;
        Revision = revision;
        CallRevision = callRevision;
        Request = request;
        Initiator = initiator;
    }

    internal Guid Owner { get; }
    internal HostRequest Request { get; }
    public string Value { get; }
    public long Revision { get; }
    public long CallRevision { get; }
    public RequestOrigin Origin => Request.Origin;
    public SecurityAuditInitiator Initiator { get; }
}
