namespace Kora.Core.Diagnostics;

public sealed record EvidenceSegment(string TraceId, string SpanId, EvidenceSegmentStatus Status,
    EvidenceReference? Record);
