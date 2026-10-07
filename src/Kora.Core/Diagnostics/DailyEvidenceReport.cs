namespace Kora.Core.Diagnostics;

public sealed record DailyEvidenceReport(
    string? SnapshotId, int Files, long Bytes, int Lines,
    int UnsupportedRecords, int AuditMirrors, int IngestionGaps);
