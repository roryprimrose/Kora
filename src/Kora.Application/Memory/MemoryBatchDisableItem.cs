using Kora.Core.Memory;

namespace Kora.Application.Memory;

internal sealed record MemoryBatchDisableItem(
    Guid Id, long ReviewedRevision, MemoryBatchDisableState State, MemoryResult? ActionResult = null);
