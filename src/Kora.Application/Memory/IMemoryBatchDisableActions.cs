using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Application.Memory;

/// <summary>Exact-session original-user actions consumed by the sequential disable workflow.</summary>
internal interface IMemoryBatchDisableActions
{
    /// <summary>Inspects one exact revision under fresh private session admission.</summary>
    Task<MemoryRecord> InspectAsync(HostId<SessionIdentity> session, MemorySummary selection,
        Func<bool> admitted, CancellationToken token);

    /// <summary>Disables one unchanged record through its authoritative transition and audit.</summary>
    /// <remarks>The callback preserves a returned mutation result before later presentation fences.</remarks>
    Task DisableAsync(HostId<SessionIdentity> session, MemoryRecord record, Func<bool> admitted,
        Action<MemoryResult> completed, CancellationToken token);
}
