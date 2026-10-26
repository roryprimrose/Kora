using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    public bool SupportsSessionPermission => store is ISessionPermissionStore;

    private ISessionPermissionStore PermissionStore => store as ISessionPermissionStore
        ?? throw new InvalidOperationException("Authoritative session permission storage is unavailable.");

    public Task<SessionPermissionPolicy> ReadSessionPermissionAsync(HostId<SessionIdentity> session, CancellationToken token) =>
        ReadAsync(() => PermissionStore.ReadSessionPermissionAsync(session, token), token);

    public Task<SessionPermissionPolicy> SetSessionPermissionAsync(SessionPermissionPolicy expected,
        SessionPermissionLevel desired, Func<bool> admitted, CancellationToken token)
    {
        var origin = ProviderProfileOrigin(RequestOrigin.LocalUi);
        if (origin != RequestOrigin.LocalUi)
        { throw new InvalidOperationException("Start a fresh native permission review; voice/model input cannot be relabeled as a native confirmation."); }
        return ControlAsync(expected.SessionId, origin,
            (request, eligible) => PermissionStore.SetSessionPermissionAsync(request, expected, desired, eligible, token),
            token, admitted, existingSubject: true);
    }
}
