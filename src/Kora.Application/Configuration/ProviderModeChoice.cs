using Kora.Core.Authorization;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

public sealed class ProviderModeChoice
{
    internal ProviderModeChoice(ModelProviderMode mode, long revision, Guid owner, WorkSessionAuthorization session,
        RequestOrigin origin, Func<bool> remainsAdmitted)
    {
        Mode = mode;
        Revision = revision;
        Owner = owner;
        Session = session;
        Origin = origin;
        RemainsAdmitted = remainsAdmitted;
    }

    public ModelProviderMode Mode { get; }
    public string Label => Mode.ToString();
    internal long Revision { get; }
    internal Guid Owner { get; }
    internal WorkSessionAuthorization Session { get; }
    internal RequestOrigin Origin { get; }
    internal Func<bool> RemainsAdmitted { get; }
}
