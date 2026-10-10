namespace Kora.Core.Memory;

// This is untrusted data only: it carries no identity, scope, lineage or admission authority.
public sealed record MemoryCandidate(MemoryContentClass ContentClass, string? Value);
