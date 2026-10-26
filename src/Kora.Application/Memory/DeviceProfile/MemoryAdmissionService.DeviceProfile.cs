using System.Collections.Immutable;
using Kora.Core.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Memory;
using Kora.Core.Memory.DeviceProfile;

namespace Kora.Application.Memory;

internal sealed partial class MemoryAdmissionService
{
    internal async Task<MemoryScope> ResolveDeviceProfileScopeAsync(CancellationToken token)
    {
        var boundary = await scopes.ResolveAsync(HostActivity.RequireCurrent().Request, token).ConfigureAwait(false);
        return MemoryScope.DeviceProfile(boundary?.Profile
            ?? throw new InvalidOperationException("The current private Device-profile boundary is unavailable."));
    }

    private ValueTask<MemoryResult> TransactAsync(HostRequest request, MemoryBoundary boundary,
        Func<ImmutableArray<MemoryRecord>, MemoryStorageCommit> transition, Func<bool> admitted, CancellationToken token) =>
        scopeKind == MemoryScopeKind.DeviceProfile
            ? (storage as IDeviceProfileMemoryStore
                ?? throw new InvalidOperationException("Authoritative Device-profile memory storage is unavailable."))
                .TransactDeviceProfileMemoryAsync(request, boundary, transition, admitted, token)
            : storage!.TransactMemoryAsync(request, boundary, transition, admitted, token);
}
