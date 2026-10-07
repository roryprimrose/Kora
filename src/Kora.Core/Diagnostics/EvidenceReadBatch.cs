namespace Kora.Core.Diagnostics;

public sealed record EvidenceReadBatch(EvidenceSnapshot Snapshot, IReadOnlyList<EvidenceCandidate> Candidates,
    EvidencePosition? ScannedThrough, bool HasMore, bool ScanLimitReached);
