using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Core.Storage;

/// <summary>Private consolidated exact-session provider policy authority.</summary>
public interface ISessionProviderProfileStore
{
    /// <summary>Reads validated desired state without admitting a turn or renewing activity.</summary>
    ValueTask<SessionProviderProfile> ReadProviderProfile(HostId<SessionIdentity> session, CancellationToken token);
    /// <summary>Commits an exact original-user proposal with requested/terminal audit and control receipt.
    /// A copy additionally requires the exact source observation at every destination commit boundary; it grants no source authority.</summary>
    ValueTask<SessionProviderProfile> CommitProviderProfile(HostRequest request, SessionProviderProfile expected,
        SessionProviderOverrides desired, Func<bool> admitted, CancellationToken token, SessionProviderProfile? source = null);
}
