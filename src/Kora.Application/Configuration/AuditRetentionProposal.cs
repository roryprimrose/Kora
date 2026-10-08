using Kora.Core.Authorization;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

public sealed class AuditRetentionProposal
{
    internal AuditRetentionProposal(AuditRetentionDays? value, long revision, long callRevision,
        WorkSessionAuthorization session, RequestOrigin origin, Func<bool> eligible)
    {
        Value = value;
        Revision = revision;
        CallRevision = callRevision;
        Session = session;
        Origin = origin;
        Eligible = eligible;
    }

    public AuditRetentionDays? Value { get; }
    internal long Revision { get; }
    internal long CallRevision { get; }
    internal WorkSessionAuthorization Session { get; }
    internal RequestOrigin Origin { get; }
    internal Func<bool> Eligible { get; }
}
