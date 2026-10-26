using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Hosting;

namespace Kora.Core.Storage;

public interface ISessionPermissionStore
{
    ValueTask<SessionPermissionPolicy> ReadSessionPermissionAsync(
        HostId<SessionIdentity> session, CancellationToken token);
    ValueTask<SessionPermissionPolicy> SetSessionPermissionAsync(
        HostRequest request, SessionPermissionPolicy expected, SessionPermissionLevel desired,
        Func<bool> admitted, CancellationToken token);
}
