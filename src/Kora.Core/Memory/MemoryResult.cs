namespace Kora.Core.Memory;

public sealed record MemoryResult(
    MemoryOutcome Outcome, MemoryReason Reason, MemoryRecord? Record = null, MemoryUse? Use = null);
