using Kora.Core.Authorization;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

public sealed class SessionQueueConfigurationProposal
{
    internal SessionQueueConfigurationProposal(SessionQueuePreferences value, SessionQueueOption option,
        long revision, long callRevision, WorkSessionAuthorization session, RequestOrigin origin, Func<bool> eligible)
    {
        Value = value;
        Option = option;
        Revision = revision;
        CallRevision = callRevision;
        Session = session;
        Origin = origin;
        Eligible = eligible;
    }

    public SessionQueuePreferences Value { get; }
    internal SessionQueueOption Option { get; }
    internal long Revision { get; }
    internal long CallRevision { get; }
    internal WorkSessionAuthorization Session { get; }
    internal RequestOrigin Origin { get; }
    internal Func<bool> Eligible { get; }
}
