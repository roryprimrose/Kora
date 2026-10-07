namespace Kora.Core.Diagnostics;

public sealed record EvidenceReadCheckpoint(EvidenceSnapshot Snapshot, EvidencePosition? After);
