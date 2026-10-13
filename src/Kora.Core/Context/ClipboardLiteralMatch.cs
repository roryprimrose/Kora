namespace Kora.Core.Context;

/// <summary>One span bound to the exact immutable clipboard object, including its capture identity.</summary>
/// <param name="Source">Exact captured snapshot, not a live clipboard reference.</param>
/// <param name="Span">Original end-exclusive UTF-16 span.</param>
public sealed record ClipboardLiteralMatch(ClipboardSnapshot Source, SnapshotLiteralSpan Span);
