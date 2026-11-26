using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Hosting;
using Kora.Core.Auditing;

namespace Kora.Core.Storage;

public interface ISessionPermissionStore
{
    ValueTask<SessionPermissionPolicy> ReadSessionPermissionAsync(
        HostId<SessionIdentity> session, CancellationToken token);
    ValueTask<SessionPermissionPolicy> SetSessionPermissionAsync(
        HostRequest request, SessionPermissionPolicy expected, SessionPermissionLevel desired,
        Func<bool> admitted, CancellationToken token);

    /// <summary>Commits a host-bound automatic approval under the exact current session policy.</summary>
    ValueTask RecordAutomaticApprovalAsync(HostRequest request, SessionPermissionPolicy expected,
        AutomaticApprovalAudit details, Func<bool> admitted, CancellationToken token) =>
        throw new InvalidOperationException("Trusted automatic approval audit storage is unavailable.");
}
