using Kora.Core.Authorization;
using Kora.Core.Communication;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

public sealed class InCallFeedbackChoice
{
    internal InCallFeedbackChoice(InCallFeedbackMode mode, long revision, long callRevision,
        WorkSessionAuthorization session, RequestOrigin origin, Func<bool> eligible)
    {
        Mode = mode;
        Revision = revision;
        CallRevision = callRevision;
        Session = session;
        Origin = origin;
        Eligible = eligible;
    }

    public InCallFeedbackMode Mode { get; }
    public string Label => Mode.ToString();
    internal long Revision { get; }
    internal long CallRevision { get; }
    internal WorkSessionAuthorization Session { get; }
    internal RequestOrigin Origin { get; }
    internal Func<bool> Eligible { get; }
}
