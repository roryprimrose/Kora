using Kora.Core.Hosting;

namespace Kora.Core.Diagnostics;

public interface IEvidenceReader
{
    ValueTask<EvidenceReadBatch> ReadAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
        HostRequest request, DateTimeOffset now, CancellationToken cancellationToken);
}
