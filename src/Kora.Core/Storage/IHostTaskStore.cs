using Kora.Core.Hosting;

namespace Kora.Core.Storage;

public interface IHostTaskStore
{
    ValueTask CommitAsync(HostTaskRecord record, long expectedRevision, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<HostTaskRecord>> ReadIncompleteAsync(int limit, CancellationToken cancellationToken);
}
