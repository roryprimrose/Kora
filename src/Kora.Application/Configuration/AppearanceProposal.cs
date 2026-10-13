using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

public sealed class AppearanceProposal
{
    internal AppearanceProposal(Guid owner, AppearanceOption option, AppearanceValue value, long revision,
        SecurityAuditInitiator initiator, RequestOrigin? originalOrigin = null, Func<bool>? remainsEligible = null)
    {
        Owner = owner;
        Option = option;
        Value = value;
        Revision = revision;
        Initiator = initiator;
        OriginalOrigin = originalOrigin;
        RemainsEligible = remainsEligible;
    }

    internal Guid Owner { get; }
    public AppearanceOption Option { get; }
    public AppearanceValue Value { get; }
    public long Revision { get; }
    public SecurityAuditInitiator Initiator { get; }
    internal RequestOrigin? OriginalOrigin { get; }
    internal Func<bool>? RemainsEligible { get; }
}
