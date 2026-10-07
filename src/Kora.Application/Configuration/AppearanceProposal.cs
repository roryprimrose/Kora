using Kora.Core.Auditing;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed class AppearanceProposal
{
    internal AppearanceProposal(Guid owner, AppearanceOption option, AppearanceValue value, long revision,
        SecurityAuditInitiator initiator)
    {
        Owner = owner;
        Option = option;
        Value = value;
        Revision = revision;
        Initiator = initiator;
    }

    internal Guid Owner { get; }
    public AppearanceOption Option { get; }
    public AppearanceValue Value { get; }
    public long Revision { get; }
    public SecurityAuditInitiator Initiator { get; }
}
