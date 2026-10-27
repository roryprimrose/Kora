using Kora.Core.Authorization;
using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Canonical original-input control domain for local relatedness inference, never a user destination.</summary>
public interface ISessionRoutingControlStore : ILocalContextTaskStore
{
    ValueTask<WorkSessionAuthorization> CreateRoutingControlSessionAsync(
        HostRequest request, Func<bool> admitted, CancellationToken token);

    ValueTask<T> WithRoutingControlSessionAsync<T>(
        HostRequest request, HostRevision generation, Func<T> operation, CancellationToken token);
}
