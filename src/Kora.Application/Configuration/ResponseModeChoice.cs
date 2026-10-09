using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed class ResponseModeChoice
{
    internal ResponseModeChoice(ResponseOutputMode mode, long revision, Guid owner, WorkSessionAuthorization session,
        RequestOrigin origin, Func<bool> remainsAdmitted)
    {
        Mode = mode;
        Revision = revision;
        Owner = owner;
        Session = session;
        Origin = origin;
        RemainsAdmitted = remainsAdmitted;
    }

    public ResponseOutputMode Mode { get; }
    public string Label => Mode.ToString();
    internal long Revision { get; }
    internal Guid Owner { get; }
    internal WorkSessionAuthorization Session { get; }
    internal RequestOrigin Origin { get; }
    internal Func<bool> RemainsAdmitted { get; }
}
