using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Application.Memory;

/// <summary>Exact-session original-user actions consumed by the sequential management workflow.</summary>
internal interface IMemoryBatchActions
{
    /// <summary>Inspects one exact revision under fresh private session admission.</summary>
    Task<MemoryRecord> InspectAsync(HostId<SessionIdentity> session, MemorySummary selection,
        Func<bool> admitted, CancellationToken token);

    /// <summary>Applies one unchanged record's authoritative transition and audit.</summary>
    /// <remarks>The callback preserves a returned mutation result before later presentation fences.</remarks>
    Task ApplyAsync(MemoryBatchOperation operation, HostId<SessionIdentity> session, MemoryRecord record, Func<bool> admitted,
        Action<MemoryResult> completed, CancellationToken token);
}
