namespace Kora.Core.Diagnostics;

public sealed record EvidencePosition(long CommittedTicks, EvidenceSource Source, string Id, int Ordinal,
    string? CommitDigest = null);
