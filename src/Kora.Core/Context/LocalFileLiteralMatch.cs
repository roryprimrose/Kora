namespace Kora.Core.Context;

/// <summary>One end-exclusive original UTF-16 span, not a retrieval citation or occurrence index.</summary>
/// <param name="Source">The exact admitted original-byte source identity.</param>
/// <param name="Start">The original UTF-16 start offset.</param>
/// <param name="Length">The literal query's unchanged UTF-16 length.</param>
public sealed record LocalFileLiteralMatch(LocalFileReference Source, int Start, int Length)
{
    internal SnapshotLiteralSpan? ComparisonSpan { get; init; }
}