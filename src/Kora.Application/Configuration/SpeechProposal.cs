using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

public sealed class SpeechProposal
{
    internal SpeechProposal(Guid owner, SpeechOption option, SpeechSelection selection,
        long revision, long callRevision, RequestOrigin origin, SecurityAuditInitiator initiator,
        SpokenSummaryLimits? summaryLimits = null)
    {
        Owner = owner;
        Option = option;
        Selection = selection;
        Revision = revision;
        CallRevision = callRevision;
        Origin = origin;
        Initiator = initiator;
        SummaryLimits = summaryLimits;
    }

    internal Guid Owner { get; }
    public SpeechOption Option { get; }
    public SpeechSelection Selection { get; }
    public SpokenSummaryLimits? SummaryLimits { get; }
    public long Revision { get; }
    public long CallRevision { get; }
    public RequestOrigin Origin { get; }
    public SecurityAuditInitiator Initiator { get; }
}
