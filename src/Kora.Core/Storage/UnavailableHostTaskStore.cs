using Kora.Core.Hosting;

namespace Kora.Core.Storage;

public sealed class UnavailableHostTaskStore : IHostTaskStore
{
    public ValueTask CommitAsync(HostTaskRecord record, long expectedRevision, CancellationToken cancellationToken) =>
        ValueTask.FromException(new StorageAdmissionException());

    public ValueTask<IReadOnlyList<HostTaskRecord>> ReadIncompleteAsync(int limit, CancellationToken cancellationToken) =>
        ValueTask.FromException<IReadOnlyList<HostTaskRecord>>(new StorageAdmissionException());
}
