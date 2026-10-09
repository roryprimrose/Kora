namespace Kora.Core.Memory;

/// <summary>Exact original data and optional replacement; absence of a replacement is a read, not authority.</summary>
public sealed record MemoryStorageCommit(
    MemoryResult Result, MemoryRecord? Original, MemoryRecord? Replacement, MemoryRecord? ReviewedOriginal = null);
