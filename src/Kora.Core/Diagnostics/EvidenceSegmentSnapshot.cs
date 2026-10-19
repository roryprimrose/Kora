namespace Kora.Core.Diagnostics;

public sealed record EvidenceSegmentSnapshot(string Id, long AuditBase, EvidenceSnapshot Ceiling);
