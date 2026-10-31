using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Application.Memory;

internal sealed partial class MemoryAdmissionService
{
    internal Task<MemoryResult> ProposeSessionCopyAsync(MemoryCandidate candidate, MemoryScope scope,
        MemoryRecord source, CancellationToken token) =>
        ExecuteAsync(MemoryOperation.Propose, null, default, scope, candidate, MemoryProposalOrigin.User,
            false, MemoryDestination.Unknown, token, sessionCopySource: source);

    internal Task<MemoryResult> ReviewSessionCopyAsync(MemoryRecord record, MemoryRecord source, bool accept,
        CancellationToken token) =>
        ExecuteAsync(MemoryOperation.Review, record.Id, record.Revision, null, null, MemoryProposalOrigin.User,
            accept, MemoryDestination.Unknown, token, sessionCopySource: source);

    internal Task<MemoryResult> AdmitSessionCopyAsync(MemoryRecord record, MemoryRecord source, CancellationToken token) =>
        ExecuteAsync(MemoryOperation.Admit, record.Id, record.Revision, null, null, MemoryProposalOrigin.User,
            false, MemoryDestination.Unknown, token, sessionCopySource: source);

    internal void RetireSessionCopy(MemoryRecord record)
    {
        lock (stateGate)
        {
            if (record.Lineage.CopiedFromSession is not null && volatileIdentities.Contains(record.Id)
                && records.GetValueOrDefault(record.Id) == record)
            {
                records.Remove(record.Id);
                volatileIdentities.Remove(record.Id);
            }
        }
    }
}
