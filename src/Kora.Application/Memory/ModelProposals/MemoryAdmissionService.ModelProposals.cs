using Kora.Core.Memory;

namespace Kora.Application.Memory;

internal sealed partial class MemoryAdmissionService
{
    internal void RetireModelProposal(MemoryRecord record)
    {
        lock (stateGate)
        {
            if (records.GetValueOrDefault(record.Id) == record && volatileIdentities.Contains(record.Id)
                && record.Lineage.Origin == MemoryProposalOrigin.Model)
            {
                records.Remove(record.Id);
                volatileIdentities.Remove(record.Id);
            }
        }
    }
}
