namespace Kora.Core.Memory;

/// <summary>Exact original data and optional replacement; absence of a replacement is a read, not authority.</summary>
public sealed record MemoryStorageCommit(
    MemoryResult Result, MemoryRecord? Original, MemoryRecord? Replacement, MemoryRecord? ReviewedOriginal = null)
{
    /// <summary>Host-owned unpersisted identities, including volatile tombstones, counted alongside all durable identities.</summary>
    public int VolatileIdentityCount { get; init; }
}
