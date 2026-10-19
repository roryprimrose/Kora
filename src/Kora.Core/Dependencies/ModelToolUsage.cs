namespace Kora.Core.Dependencies;

/// <summary>Transport-owned complete byte charges: the greater of native and canonical framing, never tokens.</summary>
public sealed record ModelToolUsage(int InputUtf8Bytes, int OutputUtf8Bytes);
