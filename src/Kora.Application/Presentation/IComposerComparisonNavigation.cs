using Kora.Core.Hosting;

namespace Kora.Application.Presentation;

public interface IComposerComparisonNavigation
{
    ComposerComparisonLaunch? Capture(HostId<SessionIdentity> session, RequestOrigin origin, Func<bool> admitted);
}
