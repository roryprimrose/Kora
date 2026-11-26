using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    public event Action<SessionPermissionPolicy>? SessionPermissionChanged;

    private WeakReference<Func<SessionWorkspaceEntry?>>? permissionControlTarget;

    public void BindPermissionControlTarget(Func<SessionWorkspaceEntry?> read) =>
        permissionControlTarget = new(read);

    public SessionWorkspaceEntry? PermissionControlTarget =>
        permissionControlTarget is { } reference && reference.TryGetTarget(out var read) ? read() : null;

    public bool SupportsSessionPermission => store is ISessionPermissionStore;

    private ISessionPermissionStore PermissionStore => store as ISessionPermissionStore
        ?? throw new InvalidOperationException("Authoritative session permission storage is unavailable.");

    public Task<SessionPermissionPolicy> ReadSessionPermissionAsync(HostId<SessionIdentity> session, CancellationToken token) =>
        ReadAsync(() => PermissionStore.ReadSessionPermissionAsync(session, token), token);

    public Task<bool> RecordAutomaticControlApprovalAsync(SessionPermissionPolicy expected, string actionId,
        string contentDigest, string promptCategory, Func<bool> admitted, CancellationToken token) =>
        ControlAsync(expected.SessionId, RequestOrigin.LocalUi, async (request, eligible) =>
        {
            await PermissionStore.RecordAutomaticApprovalAsync(request, expected,
                new AutomaticApprovalAudit(request, expected, actionId, HostOperationEffect.VoiceOrCallSettings,
                    promptCategory, contentDigest), eligible, token).ConfigureAwait(false);
            return true;
        }, token, admitted, existingSubject: true);

    public Task<SessionPermissionPolicy> SetSessionPermissionAsync(SessionPermissionPolicy expected,
        SessionPermissionLevel desired, Func<bool> admitted, CancellationToken token)
    {
        var origin = ProviderProfileOrigin(RequestOrigin.LocalUi);
        if (origin != RequestOrigin.LocalUi)
        { throw new InvalidOperationException("Start a fresh native permission review; voice/model input cannot be relabeled as a native confirmation."); }
        return SetReviewedSessionPermissionAsync(expected, desired, origin, admitted, token);
    }

    internal async Task<SessionPermissionPolicy> SetReviewedSessionPermissionAsync(SessionPermissionPolicy expected,
        SessionPermissionLevel desired, RequestOrigin origin, Func<bool> admitted, CancellationToken token)
    {
        origin = ProviderProfileOrigin(origin);
        var committed = await ControlAsync(expected.SessionId, origin,
            (request, eligible) => PermissionStore.SetSessionPermissionAsync(request, expected, desired, eligible, token),
            token, admitted, existingSubject: true).ConfigureAwait(false);
        SessionPermissionChanged?.Invoke(committed);
        return committed;
    }
}
