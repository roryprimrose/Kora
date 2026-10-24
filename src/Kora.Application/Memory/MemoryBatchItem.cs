using Kora.Core.Memory;

namespace Kora.Application.Memory;

internal sealed record MemoryBatchItem(
    Guid Id, long ReviewedRevision, MemoryBatchState State, MemoryResult? ActionResult = null);
