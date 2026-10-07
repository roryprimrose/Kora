using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Passive bounded reads of committed typed audits; never admits a task, answer or grant.</summary>
public interface ICommittedAuthorityAuditReader
{
    ValueTask<EvidenceReadBatch> ReadAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
        HostRequest request, DateTimeOffset now, CancellationToken cancellationToken);
}
