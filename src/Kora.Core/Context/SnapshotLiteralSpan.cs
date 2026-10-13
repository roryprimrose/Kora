namespace Kora.Core.Context;

/// <summary>One end-exclusive UTF-16 span, not an occurrence index.</summary>
/// <param name="Start">Original UTF-16 start.</param>
/// <param name="Length">Unchanged literal length.</param>
public sealed record SnapshotLiteralSpan(int Start, int Length);
