using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Storage;
using Kora.Core.Tools;

namespace Kora.Application.Memory;

/// <summary>Only the private store's session scope is available; no profile/project/source selector is invented.</summary>
internal sealed class SessionMemoryScopeAccess(
    IMemoryStore store, ISessionWorkspaceAccess access, ICapabilityHostAccess host) : IMemoryScopeAccess
{
    private MemoryBoundary? boundary;

    public MemoryBoundary? Observe(HostRequest request) =>
        host.IsCurrentHost && access.CanControl && boundary?.Session == request.SessionId
        && boundary.Revision == access.ControlRevision ? boundary : null;

    public async ValueTask<MemoryBoundary?> ResolveAsync(HostRequest request, CancellationToken token)
    {
        boundary = null;
        if (!host.IsCurrentHost || !access.CanControl) { return null; }
        var revision = access.ControlRevision;
        var resolved = await store.ReadMemoryBoundaryAsync(request, revision, token).ConfigureAwait(false);
        boundary = resolved;
        return Observe(request);
    }
}
