using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed class SpeechTextChoice
{
    internal SpeechTextChoice(SpeechTextMode mode, long revision, Guid owner,
        WorkSessionAuthorization session, RequestOrigin origin, Func<bool> eligible)
    {
        Mode = mode;
        Revision = revision;
        Owner = owner;
        Session = session;
        Origin = origin;
        Eligible = eligible;
    }
    public SpeechTextMode Mode { get; }
    public string Label => Mode.ToString();
    internal long Revision { get; }
    internal Guid Owner { get; }
    internal WorkSessionAuthorization Session { get; }
    internal RequestOrigin Origin { get; }
    internal Func<bool> Eligible { get; }
}
