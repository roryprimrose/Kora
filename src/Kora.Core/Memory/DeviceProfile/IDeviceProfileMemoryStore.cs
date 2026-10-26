using System.Collections.Immutable;
using Kora.Core.Hosting;

namespace Kora.Core.Memory.DeviceProfile;

/// <summary>Explicit profile-memory access; ordinary session consumers never receive these rows.</summary>
public interface IDeviceProfileMemoryStore
{
    ValueTask<MemoryResult> TransactDeviceProfileMemoryAsync(HostRequest request, MemoryBoundary boundary,
        Func<ImmutableArray<MemoryRecord>, MemoryStorageCommit> transition, Func<bool> admitted, CancellationToken token);
}
