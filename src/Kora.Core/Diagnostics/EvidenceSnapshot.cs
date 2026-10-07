namespace Kora.Core.Diagnostics;

public sealed record EvidenceSnapshot(long Log, long Audit, long Span, long Link);
