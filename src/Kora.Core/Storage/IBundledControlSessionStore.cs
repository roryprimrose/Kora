using Kora.Core.Authorization;
using Kora.Core.Hosting;

namespace Kora.Core.Storage;

public interface IBundledControlSessionStore
{
    ValueTask<WorkSessionAuthorization> CreateBundledControlSessionAsync(
        HostRequest request, Func<bool> admitted, CancellationToken cancellationToken);

    ValueTask<HostTaskRecord> AdmitBundledControlDispatchAsync(
        HostRequest request, HostRevision generation, Func<bool> admitted, CancellationToken cancellationToken);
}