using Kora.Core.Authorization;
using Kora.Core.Hosting;

namespace Kora.Core.Storage;

public interface IAuditRetentionSessionStore
{
    ValueTask<WorkSessionAuthorization> CreateAuditRetentionSessionAsync(
        HostRequest request, Func<bool> admitted, CancellationToken cancellationToken);

    ValueTask<T> WithAuditRetentionSessionAsync<T>(HostRequest request, HostRevision generation,
        Func<T> operation, CancellationToken cancellationToken);
}
