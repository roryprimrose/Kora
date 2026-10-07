using Kora.Core.Authorization;
using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Host-only cached maintenance admission; grants no network, audio or execution authority.</summary>
public interface IMaintenanceControlSessionStore
{
    ValueTask<WorkSessionAuthorization> CreateMaintenanceControlSessionAsync(
        HostRequest request, Func<bool> admitted, CancellationToken cancellationToken);

    ValueTask<T> WithMaintenanceControlSessionAsync<T>(HostRequest request, HostRevision generation,
        Func<T> operation, CancellationToken cancellationToken);
}
